"""Chronological constrained selection. Never tune on the reserved holdout."""
import csv,gc,hashlib,io,itertools,json,math,subprocess,time
from pathlib import Path
from datetime import datetime,timedelta
from session_context import TAIPEI
import numpy as np
import torch
from torch.utils.data import Dataset,DataLoader
from sklearn.ensemble import HistGradientBoostingClassifier,HistGradientBoostingRegressor
from execution_alpha import AlphaForecast,pooled_features,project_signals,masked_loss,export_head,tree_predict
from sequence_dataset import build_samples,FEATURE_NAMES_V2,aggregate_source
from sequence_model import ACCEPTANCE_CRITERIA
from train_sequence_trend import write_csv,replay,day_groups,totals
from research_resource_guard import limited_read,limited_write,Budget,safe_cleanup


def coverage(session):
    start=datetime.fromtimestamp(session['first'],TAIPEI).replace(hour=7,minute=45,second=0,microsecond=0)
    if session['id'].endswith('night'):
        start=start.replace(hour=16,minute=0);end=(start+timedelta(days=1)).replace(hour=4,minute=55)
    else:end=start.replace(hour=14,minute=40)
    return session['first']<=start.timestamp()+5 and session['last']>=end.timestamp()-5


def load_days(manifests,role=None):
    days=[];session_keys=set()
    for m in manifests:
        if role is not None and m['role']!=role:continue
        for session in m['sessions']:
            if session['id'] in session_keys:raise ValueError('Overlapping session caches across source files')
            session_keys.add(session['id'])
            with np.load(limited_read(session['cache'])) as z:
                x=z['x'];mid=z['mid'];times=z['times'];valid=z['valid'];quotes=z['quotes'];trades=z['trades']
            s=build_samples(x,mid,times,valid)
            days.append(dict(id=session['id'],role=m['role'],samples=s,quotes=quotes,trades=trades,fullSession=coverage(session) and m['riskReplayReliable'],sourceReliable=m['riskReplayReliable'],source=str(m['source']),sourceBytes=m['sourceBytes'],sourceMtimeNs=m['sourceMtimeNs'],sourceSha256=m['sourceSha256']))
    return days


def prepare_labels(root,config,day):
    temp=root/'replay-temp';temp.mkdir(exist_ok=True)
    tape=temp/(day['id']+'-labels-trades.csv');targets=temp/(day['id']+'-labels-targets.csv');output=temp/(day['id']+'-labels.csv')
    s=day['samples'];indices=s.indices[::config['labelStride']]
    write_csv(tape,day['trades'],['time','price','sequence'],root)
    write_csv(targets,([int(i),s.times[i],day['quotes'][i,1],day['quotes'][i,2]] for i in indices),['index','time','bid','ask'],root)
    subprocess.run(['dotnet',config['labelsBridge'],str(tape),str(targets),str(output)],check=True,capture_output=True,text=True,timeout=600)
    values=[];mask=[];kept=[];status_counts={side:{'closed':0,'unfilled':0,'censored':0} for side in ('long','short')}
    with io.TextIOWrapper(limited_read(output),encoding='utf-8',newline='') as f:
        for r in csv.DictReader(f):
            kept.append(int(r['index']));v=[];c=[]
            for prefix in ('long','short'):
                status=r[prefix+'Status']
                if status not in status_counts[prefix]:raise ValueError('Unknown execution label status')
                status_counts[prefix][status]+=1
                closed=status=='closed';c.append(closed);v.append(float(r[prefix+'Net'])/50 if closed else 0.)
            values.append(v);mask.append(c)
    day['labelStatusCounts']=status_counts
    day['labelIndices']=np.asarray(kept,np.int64);day['netTargets']=np.asarray(values,np.float32).reshape(-1,2);day['closedTargets']=np.asarray(mask,bool).reshape(-1,2)
    print('LABEL_DONE',day['id'],'samples',len(kept),'closed',np.sum(day['closedTargets'],0).tolist(),flush=True)
    # One day at a time: do not retain bulky CSVs or duplicate labels.
    safe_cleanup(root,[tape,targets,output])


def matrix(days):
    x=[];y=[];mask=[];weights=[]
    for d in days:
        if not len(d['labelIndices']):continue
        a=pooled_features(d['samples'],d['labelIndices']);x.append(a);y.append(d['netTargets']);mask.append(d['closedTargets']);weights.append(np.full(len(a),1/max(1,len(a))))
    if not x:raise ValueError('No labelled days')
    return np.concatenate(x),np.concatenate(y),np.concatenate(mask),np.concatenate(weights)


class NeuralWindows(Dataset):
    def __init__(self,days,norm):
        self.days=days;self.norm=norm;self.rows=[(d,j,int(i)) for d in days for j,i in enumerate(d['labelIndices']) if d['closedTargets'][j].any()]
    def __len__(self):return len(self.rows)
    def __getitem__(self,j):
        d,k,i=self.rows[j];x,a=d['samples'].window(i);n=self.norm
        x=np.clip((x-n['mean'])/n['scale'],-10,10).astype(np.float32);a=np.clip((a-n['auxMean'])/n['auxScale'],-10,10).astype(np.float32)
        return torch.from_numpy(x),torch.from_numpy(a),torch.from_numpy(d['netTargets'][k]),torch.from_numpy(d['closedTargets'][k])


def neural_fit(kind,seed,train,validation,norm,config):
    torch.manual_seed(seed);np.random.seed(seed);model=AlphaForecast(kind)
    optimizer=torch.optim.AdamW(model.parameters(),lr=.001,weight_decay=1e-4)
    training=DataLoader(NeuralWindows(train,norm),batch_size=32,shuffle=True,num_workers=0)
    validating=DataLoader(NeuralWindows(validation,norm),batch_size=32,shuffle=False,num_workers=0)
    best=float('inf');best_state=None;stale=0;history=[]
    for epoch in range(config['epochs']):
        model.train()
        for x,a,y,mask in training:
            if not mask.any():continue
            p,n=model(x,a);loss=masked_loss(p,n,y,mask);optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),1);optimizer.step()
        model.eval();losses=[]
        with torch.inference_mode():
            for x,a,y,mask in validating:
                if mask.any():p,n=model(x,a);losses.append(float(masked_loss(p,n,y,mask)))
        value=float(np.mean(losses)) if losses else float('inf');history.append(value)
        print('NEURAL',kind,seed,'epoch',epoch+1,'validationLoss',value,flush=True)
        if value<best-1e-5:best=value;best_state={k:v.detach().clone() for k,v in model.state_dict().items()};stale=0
        else:stale+=1
        if stale>=config['patience']:break
    if best_state is None:raise ValueError('No finite validation loss')
    model.load_state_dict(best_state);model.eval();return model,history


def tree_fit(kind,seed,train):
    x,y,closed,weights=matrix(train);heads=[];pheads=[]
    leaves=7 if kind=='tree7' else 15
    for side in (0,1):
        select=closed[:,side]
        if select.sum()<100:heads.append(-64.);pheads.append(0.);continue
        target=np.clip(y[select,side],-64,64);w=weights[select]*len(weights)
        reg=HistGradientBoostingRegressor(max_iter=80,max_leaf_nodes=leaves,min_samples_leaf=100,l2_regularization=10,learning_rate=.05,early_stopping=False,random_state=seed).fit(x[select],target,sample_weight=w)
        cls=(target>0).astype(int)
        clf=float(cls[0]) if len(np.unique(cls))<2 else HistGradientBoostingClassifier(max_iter=80,max_leaf_nodes=leaves,min_samples_leaf=100,l2_regularization=10,learning_rate=.05,early_stopping=False,random_state=seed).fit(x[select],cls,sample_weight=w)
        heads.append(reg);pheads.append(clf)
    return heads,pheads


def predict(model,kind,days,norm):
    results={}
    for d in days:
        s=d['samples'];indices=s.inference_indices
        if kind.startswith('tree'):
            x=pooled_features(s,indices);reg,cls=model
            n=np.column_stack([np.full(len(x),h) if isinstance(h,float) else h.predict(x) for h in reg])
            p=np.column_stack([np.full(len(x),h) if isinstance(h,float) else h.predict_proba(x)[:,1] for h in cls])
        else:
            ns=[];ps=[]
            for start in range(0,len(indices),32):
                rows=[s.window(int(i)) for i in indices[start:start+32]]
                if not rows:continue
                x=np.stack([r[0] for r in rows]);a=np.stack([r[1] for r in rows])
                x=torch.tensor(np.clip((x-norm['mean'])/norm['scale'],-10,10),dtype=torch.float32);a=torch.tensor(np.clip((a-norm['auxMean'])/norm['auxScale'],-10,10),dtype=torch.float32)
                with torch.inference_mode():pp,nn=model(x,a);ps.append(pp.sigmoid().numpy());ns.append(nn.numpy())
            p=np.concatenate(ps) if ps else np.empty((0,2));n=np.concatenate(ns) if ns else np.empty((0,2))
        trend60=s.mid[indices]-s.mid[np.maximum(0,indices-12)];trend300=s.mid[indices]-s.mid[np.maximum(0,indices-60)]
        signals,delta=project_signals(n,p,trend60,trend300)
        results[d['id']]=(indices,signals,delta)
    return results


def policy_grid(config):
    return [dict(threshold=t,minimumNetTicks=e,stopPoints=s,maximumHoldSeconds=h,exitThreshold=max(.25,t-.1),trendFilter=True,forecastUnits='base-cost-net-ticks-50JPY') for t,e,s,h in itertools.product(config['policyThresholds'],config['minimumNetTicks'],config['stopPoints'],config['maximumHoldSeconds'])]


def score_policy(root,config,days,prediction,policy,cost=0,full_risk=False):
    rows=[]
    for group in day_groups(days):
        source=None
        if full_risk:
            paths={d['source'] for d in group}
            if len(paths)!=1:raise ValueError('Whole-day raw risk source is ambiguous')
            source=Path(next(iter(paths)));before=source.stat()
            if any((before.st_size,before.st_mtime_ns)!=(d['sourceBytes'],d['sourceMtimeNs']) for d in group):raise ValueError('Raw risk source differs from aggregated source metadata')
        row=replay(root,config,group,prediction,policy['threshold'],True,cost,policy,source)
        if source is not None:
            if any(row.get('rawSourceSha256')!=d['sourceSha256'] for d in group):raise ValueError('Raw risk source SHA differs from aggregated source')
            after=source.stat()
            if (before.st_size,before.st_mtime_ns)!=(after.st_size,after.st_mtime_ns):raise ValueError('Raw risk source changed')
        rows.append(row)
    return dict(policy=policy,metrics=totals(rows),daily=rows,costTicks=cost)


def screen_eligible(score,minimum_days):
    m=score['metrics']
    return bool(len(score['daily'])>=minimum_days and m['passesNumericalThresholds'] and m['completeDayCoverage'] and not m['hasOpenPosition'])


def eligible(score,minimum_days):
    m=score['metrics']
    return bool(len(score['daily'])>=minimum_days and m['passesNumericalThresholds'] and m['dailyRiskVerified'] and not m['hasOpenPosition'])


def challenger_key(score):
    m=score['metrics'];n=m['minimumDailyCompletedTrades'] or 0
    # Used only to decide the next research direction. Never makes a failed model eligible.
    return (min(n,10),m['winRate'] or 0,m['netProfitFactor'] or 0,m['netPnl'])


def save_model(root,tag,kind,model,policy,norm,source_pins):
    directory=root/'models';directory.mkdir(exist_ok=True)
    if kind.startswith('tree'):
        reg,cls=model;payload=dict(schema='nikkei-net-alpha-v2',features=FEATURE_NAMES_V2,pooledWidth=271,netHeads=[export_head(h) for h in reg],winHeads=[export_head(h,'classification') for h in cls],policy=policy)
        blob=json.dumps(payload,separators=(',',':')).encode();limited_write(directory/(tag+'.tree.json'),blob,root)
    else:
        buffer=io.BytesIO();torch.save(model.state_dict(),buffer);limited_write(directory/(tag+'.pt'),buffer.getvalue(),root)
    from verify_alpha_export import verify
    parity=verify(root,tag,kind,model)
    manifest=dict(rawFeatureParity=json.loads((root/'raw-feature-source-parity.json').read_text(encoding='utf-8')),tensorParity=parity,model=tag,kind=kind,schema='nikkei-net-alpha-v2',sequenceShape=[1,120,45],backgroundShape=[1,16],features=FEATURE_NAMES_V2,lookbackSeconds=600,samplingSeconds=5,predictionSeconds=30,outputMeaning='Independent long/short win probabilities conditional on fill, and net JPY/50 after base fees',labels='Common CrossPricePaperExecutionService and PaperPortfolioLedger; unfilled/censored excluded',policy=policy,acceptanceCriteria=ACCEPTANCE_CRITERIA,trainingSourceSha256=source_pins,macroCalendarKnown=False,cmeHolidayCalendarVerified=False,formalFutureFullDaysRequired=5,deploymentEligible=False)
    (directory/(tag+'.manifest.json')).write_text(json.dumps(manifest,indent=2))


def fit(root,config,manifests):
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    execution_pins=json.loads((root/'execution-source-sha256.json').read_text(encoding='utf-8'))
    binary_pins={p:v['sha256'].lower() for p,v in execution_pins.items() if p.endswith('.dll')}
    if any(hashlib.sha256((root/p).read_bytes()).hexdigest()!=sha for p,sha in binary_pins.items()):raise RuntimeError('Execution binary changed before fitting')
    source_pins={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'source').glob('*.py')}
    (root/'training-source-sha256.json').write_text(json.dumps(source_pins,indent=2))
    days=load_days(manifests);train=[d for d in days if d['role']=='train']
    requested=[d for d in days if d['role']=='validation'];validation=[];excluded=[]
    for group in day_groups(requested):
        if len(group)==2 and all(d['fullSession'] for d in group):validation.extend(group)
        else:excluded.append(dict(day=group[0]['id'][:10],reason='Partial session or source ordering quarantine; cannot certify whole-day execution risk'))
    split=dict(training=[d['id'] for d in train],validation=[d['id'] for d in validation],excludedValidation=excluded,heldout=config['heldout'],historicalRetrospectiveOnly=True,formalFutureValidationComplete=False)
    (root/'split-manifest.json').write_text(json.dumps(split,indent=2))
    if len(day_groups(validation))<config['validationMinimumFullDays']:
        (root/'research-report.md').write_text('# 未完成驗收\n\n品質合格完整驗證日不足；不能降低資料品質或拿封存日期補調參。先修復錄製及取得更多完整日。')
        print('DATA_BLOCKED insufficient reliable validation dates',flush=True);return
    if max(float(d['samples'].times[-1]) for d in train)>=min(float(d['samples'].times[0]) for d in validation)-600:raise ValueError('Chronological train/validation embargo violated')
    from verify_raw_features import verify_raw_sources
    verify_raw_sources(root,validation)
    from diagnose_legacy import diagnose
    diagnose(root,validation)
    all_input=np.concatenate([d['samples'].x[d['samples'].valid] for d in train]);aux=np.stack([d['samples'].window(int(i))[1] for d in train for i in d['samples'].indices[::30]])
    norm=dict(mean=all_input.mean(0),scale=np.maximum(all_input.std(0),.01),auxMean=aux.mean(0),auxScale=np.maximum(aux.std(0),.01));del all_input,aux
    buffer=io.BytesIO();np.savez_compressed(buffer,**norm);limited_write(root/'normalizer.npz',buffer.getvalue(),root)
    for d in train+validation:prepare_labels(root,config,d);gc.collect()
    label_summary=[dict(session=d['id'],samples=len(d['labelIndices']),executionStatusCounts=d['labelStatusCounts'],closedCounts=d['closedTargets'].sum(0).tolist(),meanNetTicks=[float(np.mean(d['netTargets'][d['closedTargets'][:,side],side])) if d['closedTargets'][:,side].any() else None for side in (0,1)]) for d in train+validation]
    (root/'label-summary.json').write_text(json.dumps(label_summary,indent=2))
    candidates=[];best_model=None;best_kind=None;best_score=None;best_tag=None
    for kind in config['models']:
        for seed in config['seeds']:
            tag=f'{kind}-seed{seed}';print('FIT',tag,flush=True)
            if kind.startswith('tree'):model=tree_fit(kind,seed,train);history=[]
            else:model,history=neural_fit(kind,seed,train,validation,norm,config)
            output=predict(model,kind,validation,norm);scores=[]
            for policy in policy_grid(config):
                score=score_policy(root,config,validation,output,policy);scores.append(score)
            screened=sorted((s for s in scores if screen_eligible(s,config['validationMinimumFullDays'])),key=lambda s:s['metrics']['netPnl'],reverse=True)
            full_scores=[]
            for screened_score in screened[:3]:
                print('FULL_RISK_REPLAY',tag,json.dumps(screened_score['policy']),flush=True)
                try:full_scores.append(score_policy(root,config,validation,output,screened_score['policy'],full_risk=True))
                except subprocess.CalledProcessError as error:
                    print('FULL_RISK_REJECTED',tag,error.stderr,flush=True)
                    break
            feasible=[s for s in full_scores if eligible(s,config['validationMinimumFullDays'])]
            chosen=max(feasible,key=lambda s:s['metrics']['netPnl']) if feasible else max(scores,key=challenger_key)
            summary=dict(model=tag,kind=kind,seed=seed,validationRuleQualified=bool(feasible),selected=chosen,history=history,allValidationScores=scores,fullRiskValidationScores=full_scores,deploymentEligible=False)
            candidates.append(summary)
            if feasible and (best_score is None or chosen['metrics']['netPnl']>best_score['metrics']['netPnl']):best_model=model;best_kind=kind;best_score=chosen;best_tag=tag
            save_model(root,tag,kind,model,chosen['policy'],norm,source_pins)
            (root/'model-results.json').write_text(json.dumps(candidates,indent=2))
            print('VALIDATION_RESULT',tag,json.dumps(chosen['metrics']),flush=True)
            del output;gc.collect()
    if best_model is not None:
        # Once only: reserved data may be evaluated only after the candidate and rules are frozen.
        freeze=dict(model=best_tag,policy=best_score['policy'],frozenAtUtc=datetime.now().astimezone().isoformat(),heldout=config['heldout'],allTrainingSourceSha256=source_pins)
        (root/'candidate-freeze.json').write_text(json.dumps(freeze,indent=2))
        stress=[];quality_error=None
        try:
            m=aggregate_source(config['heldout']['path'],root/'cache',ordering_policy='quarantine',features_v2=True);m.update(role='heldout',riskReplayReliable=m['ordering']==0)
            (root/'heldout-source-manifest.json').write_text(json.dumps(m,indent=2),encoding='utf-8')
            heldout=load_days([m])
            if not m['riskReplayReliable'] or len(heldout)!=2 or not all(d['fullSession'] for d in heldout):raise ValueError('Heldout lacks one complete reliable day/night pair')
            output=predict(best_model,best_kind,heldout,norm)
            for cost in config['costTicks']:stress.append(score_policy(root,config,heldout,output,best_score['policy'],cost,full_risk=True))
        except (ValueError,subprocess.CalledProcessError) as error:
            quality_error=str(error)
            if isinstance(error,subprocess.CalledProcessError):quality_error+=' '+str(error.stderr)
        result=dict(model=best_tag,heldoutStress=stress,heldoutQualityError=quality_error,historicalHeldoutPass=quality_error is None and bool(stress) and eligible(stress[0],1),formalFutureValidationComplete=False,deploymentEligible=False)
        (root/'heldout-results.json').write_text(json.dumps(result,indent=2));print('HELDOUT_RESULT',json.dumps(result),flush=True)
    report=['# 多尺度淨收益模型第二輪','','尚未完成正式驗收；原始C#特徵及模型張量已核對；凍結後5個新完整策略日及事件日曆仍待驗證。','','|模型|驗證交易數|淨JPY|淨勝率%|淨PF|每日最少交易|最大日虧損|通過驗證條件|','|---|---:|---:|---:|---:|---:|---:|---|']
    for c in candidates:
        m=c['selected']['metrics'];report.append(f"|{c['model']}|{m['completedTrades']}|{m['netPnl']}|{m['winRate']}|{m['netProfitFactor']}|{m['minimumDailyCompletedTrades']}|{m['maximumDailyLoss']}|{c['validationRuleQualified']}|")
    (root/'research-report.md').write_text('\n'.join(report),encoding='utf-8')
    if any(hashlib.sha256((root/p).read_bytes()).hexdigest()!=sha for p,sha in {**source_pins,**binary_pins}.items()):raise RuntimeError('Training source or execution binary changed during fitting')