"""Single guarded run: cached causal inputs, 30s forecast and 180s action value."""
import csv,gc,hashlib,io,json,subprocess,sys
from pathlib import Path
from datetime import datetime
import numpy as np
import torch
from torch.utils.data import Dataset,DataLoader
from sklearn.ensemble import HistGradientBoostingRegressor,HistGradientBoostingClassifier
from research_resource_guard import guarded_run,limited_read,limited_write,safe_cleanup
from fit_execution_alpha import load_days,score_policy,eligible,screen_eligible,challenger_key,policy_grid
from train_sequence_trend import write_csv,day_groups
from execution_alpha import pooled_features,export_head
from dual_horizon_model import DualForecast,dual_loss,dual_signals,value_future_mask
from sequence_model import ACCEPTANCE_CRITERIA
from sequence_dataset import FEATURE_NAMES_V2,aggregate_source
from verify_dual_export import verify
from holdout_guard import claim_holdout

def labels(root,config,day):
    temp=root/'replay-temp';temp.mkdir(exist_ok=True);s=day['samples'];indices=s.indices[::3];good=value_future_mask(s,indices)
    day['labelIndices']=indices;day['priceTargets']=(s.mid[indices+6]-s.mid[indices])/5
    day['netTargets']=np.zeros((len(indices),2),np.float32);day['closedTargets']=np.zeros((len(indices),2),bool)
    positions={int(i):j for j,i in enumerate(indices)};tape=temp/'tape.csv';targets=temp/'targets.csv';output=temp/'labels.csv'
    write_csv(tape,day['trades'],['time','price','sequence'],root)
    write_csv(targets,([int(i),s.times[i],day['quotes'][i,1],day['quotes'][i,2]] for i in indices[good]),['index','time','bid','ask'],root)
    subprocess.run(['dotnet',config['labelsBridge'],str(tape),str(targets),str(output)],capture_output=True,text=True,check=True,timeout=600)
    counts={side:{'closed':0,'unfilled':0,'censored':0} for side in ('long','short')}
    with io.TextIOWrapper(limited_read(output),encoding='utf-8',newline='') as f:
        for row in csv.DictReader(f):
            j=positions[int(row['index'])]
            for side,prefix in enumerate(('long','short')):
                status=row[prefix+'Status']
                if status not in counts[prefix]:raise ValueError('Unknown label status')
                counts[prefix][status]+=1
                if status=='closed':day['closedTargets'][j,side]=True;day['netTargets'][j,side]=float(row[prefix+'Net'])/50
    day['labelSummary']=dict(session=day['id'],forecast30Labels=len(indices),value180Eligible=int(good.sum()),futureValueQualityExcluded=int((~good).sum()),executionStatuses=counts,closedCounts=day['closedTargets'].sum(0).tolist(),meanNetTicks=[float(day['netTargets'][day['closedTargets'][:,side],side].mean()) if day['closedTargets'][:,side].any() else None for side in (0,1)])
    safe_cleanup(root,[tape,targets,output]);print('LABEL180_DONE',json.dumps(day['labelSummary']),flush=True)

def matrix(days):
    total={key:sum(len(d['labelIndices']) for d in group) for key,group in ((group[0]['id'][:10],group) for group in day_groups(days))}
    x=[];f=[];y=[];m=[];w=[]
    for d in days:
        rows=pooled_features(d['samples'],d['labelIndices']);x.append(rows);f.append(d['priceTargets']);y.append(d['netTargets']);m.append(d['closedTargets']);w.append(np.full(len(rows),1/max(1,total[d['id'][:10]])))
    return np.concatenate(x),np.concatenate(f),np.concatenate(y),np.concatenate(m),np.concatenate(w)

class Windows(Dataset):
    def __init__(self,days,norm):self.rows=[(d,j,int(i)) for d in days for j,i in enumerate(d['labelIndices'])];self.norm=norm
    def __len__(self):return len(self.rows)
    def __getitem__(self,j):
        d,k,i=self.rows[j];x,a=d['samples'].window(i);n=self.norm
        return torch.tensor(np.clip((x-n['mean'])/n['scale'],-10,10),dtype=torch.float32),torch.tensor(np.clip((a-n['auxMean'])/n['auxScale'],-10,10),dtype=torch.float32),torch.tensor(d['priceTargets'][k],dtype=torch.float32),torch.tensor(d['netTargets'][k]),torch.tensor(d['closedTargets'][k])

def neural(kind,seed,train,validation,norm,config):
    torch.manual_seed(seed);np.random.seed(seed);model=DualForecast(kind);opt=torch.optim.AdamW(model.parameters(),lr=.001,weight_decay=1e-4)
    training=DataLoader(Windows(train,norm),batch_size=32,shuffle=True,num_workers=0);validating=DataLoader(Windows(validation,norm),batch_size=32,shuffle=False,num_workers=0)
    best=float('inf');state=None;stale=0;history=[]
    for epoch in range(config['epochs']):
        model.train()
        for x,a,price,y,m in training:
            f,p,n=model(x,a);loss=dual_loss(f,p,n,price,y,m);opt.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),1);opt.step()
        model.eval();losses=[]
        with torch.inference_mode():
            for x,a,price,y,m in validating:f,p,n=model(x,a);losses.append(float(dual_loss(f,p,n,price,y,m)))
        value=float(np.mean(losses));history.append(value);print('NEURAL180',kind,seed,epoch+1,value,flush=True)
        if np.isfinite(value) and value<best-1e-5:best=value;state={k:v.detach().clone() for k,v in model.state_dict().items()};stale=0
        else:stale+=1
        if stale>=2:break
    if state is None:raise ValueError('No finite validation loss')
    model.load_state_dict(state);model.eval();return model,history

def tree(kind,seed,train):
    x,f,y,m,w=matrix(train);leaves=15 if kind=='tree15' else 31
    def reg(target,select):return HistGradientBoostingRegressor(max_iter=120,max_leaf_nodes=leaves,min_samples_leaf=64,l2_regularization=5,learning_rate=.05,early_stopping=False,random_state=seed).fit(x[select],target[select],sample_weight=w[select]*len(w))
    forecast=reg(np.clip(f,-64,64),np.ones(len(f),bool));nets=[];wins=[]
    for side in (0,1):
        select=m[:,side];target=np.clip(y[:,side],-128,128);nets.append(reg(target,select));cls=(target>0).astype(int)
        wins.append(float(cls[select][0]) if len(np.unique(cls[select]))<2 else HistGradientBoostingClassifier(max_iter=120,max_leaf_nodes=leaves,min_samples_leaf=64,l2_regularization=5,learning_rate=.05,early_stopping=False,random_state=seed).fit(x[select],cls[select],sample_weight=w[select]*len(w)))
    return forecast,nets,wins

def predict(model,kind,days,norm):
    output={};diagnostics=[]
    for d in days:
        s=d['samples'];indices=s.inference_indices
        if kind.startswith('tree'):
            x=pooled_features(s,indices);fore,nets,wins=model;f=fore.predict(x);n=np.column_stack([h.predict(x) for h in nets]);p=np.column_stack([np.full(len(x),h) if isinstance(h,float) else h.predict_proba(x)[:,1] for h in wins])
        else:
            fs=[];ns=[];ps=[]
            for start in range(0,len(indices),32):
                rows=[s.window(int(i)) for i in indices[start:start+32]];x=np.stack([r[0] for r in rows]);a=np.stack([r[1] for r in rows])
                with torch.inference_mode():f,p,n=model(torch.tensor(np.clip((x-norm['mean'])/norm['scale'],-10,10),dtype=torch.float32),torch.tensor(np.clip((a-norm['auxMean'])/norm['auxScale'],-10,10),dtype=torch.float32));fs.append(f.numpy());ps.append(p.sigmoid().numpy());ns.append(n.numpy())
            f=np.concatenate(fs);n=np.concatenate(ns);p=np.concatenate(ps)
        trend60=s.mid[indices]-s.mid[np.maximum(indices-12,0)];trend300=s.mid[indices]-s.mid[np.maximum(indices-60,0)];probs,delta=dual_signals(f,n,p,trend60,trend300);output[d['id']]=(indices,probs,delta)
        diagnostics.append(dict(session=d['id'],inferenceWindows=len(indices),forecast30Range=[float(f.min()),float(f.max())],net180Range=[float(n.min()),float(n.max())],positiveValueHeads=(n>0).sum(0).tolist(),directionAndValuePass=int((delta!=0).sum()),confidence40AndEdge25Pass=int(((np.where(delta>0,probs[:,2],probs[:,0])>=.4)&(np.abs(delta)>.25)).sum())))
    return output,diagnostics

def save(root,tag,kind,model,policy,pins):
    directory=root/'models';directory.mkdir(exist_ok=True)
    if kind.startswith('tree'):
        f,n,p=model;payload=dict(schema='nikkei-action-alpha-v3',features=FEATURE_NAMES_V2,pooledWidth=271,forecast30Head=export_head(f),netHeads=[export_head(h) for h in n],winHeads=[export_head(h,'classification') for h in p],policy=policy);limited_write(directory/(tag+'.tree.json'),json.dumps(payload,separators=(',',':')).encode(),root)
    else:
        buffer=io.BytesIO();torch.save(model.state_dict(),buffer);limited_write(directory/(tag+'.pt'),buffer.getvalue(),root)
    parity=verify(root,tag,kind,model)
    manifest=dict(model=tag,kind=kind,schema='nikkei-action-alpha-v3',sequenceShape=[1,120,45],backgroundShape=[1,16],samplingSeconds=5,lookbackSeconds=600,marketPredictionSeconds=30,actionHoldingSecondsFromEntry=180,stopPoints=50,outputs=['forecast30_ticks','win_logits','net_ticks'],outputMeaning='30s midpoint drift; independent fill-and-close conditional 180s win probabilities and net JPY/50 after72JPY fees',targetClips=dict(forecast30Ticks=64,net180Ticks=128),valueLoss='mean squared error, scaling8 ticks; predicts clipped conditional mean',trainingSourceSha256=pins,tensorParity=parity,rawFeatureParity=json.loads((root/'raw-feature-source-parity.json').read_text()),macroCalendarKnown=False,cmeHolidayCalendarVerified=False,acceptanceCriteria=ACCEPTANCE_CRITERIA,formalFutureFullDaysRequired=5,deploymentEligible=False)
    (directory/(tag+'.manifest.json')).write_text(json.dumps(manifest,indent=2))

def worker(root):
    config=json.loads((root/'experiment-config.json').read_text(encoding='utf-8'));torch.set_num_threads(2);torch.set_num_interop_threads(1)
    provenance=json.loads((root/'cache-provenance.json').read_text());manifest=json.loads((root/'source-manifest.json').read_text())
    for name,sha in provenance['aggregationSourcePins'].items():
        if hashlib.sha256((root/'source'/name).read_bytes()).hexdigest()!=sha:raise ValueError('Cached aggregation schema/source pin mismatch')
    for path,sha in provenance['cacheSha256'].items():
        if hashlib.sha256(limited_read(root/path).getvalue()).hexdigest()!=sha:raise ValueError('Cached arrays SHA mismatch')
    pins={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'source').glob('*.py')}
    executions=json.loads((root/'execution-source-sha256.json').read_text());binary_pins={p:v['sha256'].lower() for p,v in executions.items() if p.endswith('.dll')}
    if any(hashlib.sha256((root/p).read_bytes()).hexdigest()!=sha for p,sha in binary_pins.items()):raise ValueError('Execution binary pin mismatch')
    (root/'training-source-sha256.json').write_text(json.dumps(pins,indent=2));days=load_days(manifest['sources']);train=[d for d in days if d['role']=='train'];validation=[d for d in days if d['role']=='validation']
    if len(day_groups(validation))<2 or not all(d['fullSession'] for d in validation):raise ValueError('Insufficient full validation days')
    if max(d['samples'].times[-1] for d in train)>=min(d['samples'].times[0] for d in validation)-600:raise ValueError('Chronological embargo violated')
    with np.load(limited_read(root/'normalizer.npz')) as z:norm={key:z[key] for key in z.files}
    for d in train+validation:labels(root,config,d);gc.collect()
    (root/'label-summary.json').write_text(json.dumps([d['labelSummary'] for d in train+validation],indent=2))
    counts=lambda items:sum((d['closedTargets'].sum(0) for d in items),np.zeros(2,np.int64))
    if np.min(counts(train))<100 or np.min(counts(validation))<20:raise ValueError('Insufficient closed 180s outcomes; cannot train value heads')
    candidates=[];best=None
    for kind,seed in config['modelSpecs']:
        tag=f'{kind}-seed{seed}';print('FIT180',tag,flush=True)
        if kind.startswith('tree'):model=tree(kind,seed,train);history=[]
        else:model,history=neural(kind,seed,train,validation,norm,config)
        output,diagnostics=predict(model,kind,validation,norm);scores=[score_policy(root,config,validation,output,p) for p in policy_grid(config)]
        screened=sorted((s for s in scores if screen_eligible(s,2)),key=lambda s:s['metrics']['netPnl'],reverse=True);full=[]
        for score in screened[:3]:
            print('FULL_RISK180',tag,json.dumps(score['policy']),flush=True)
            try:full.append(score_policy(root,config,validation,output,score['policy'],full_risk=True))
            except subprocess.CalledProcessError as error:print('FULL_RISK_REJECTED',tag,error.stderr,flush=True);break
        feasible=[s for s in full if eligible(s,2)];chosen=max(feasible,key=lambda s:s['metrics']['netPnl']) if feasible else max(scores,key=challenger_key)
        item=dict(model=tag,kind=kind,seed=seed,validationRuleQualified=bool(feasible),selected=chosen,history=history,diagnostics=diagnostics,allValidationScores=scores,fullRiskValidationScores=full,deploymentEligible=False);candidates.append(item);save(root,tag,kind,model,chosen['policy'],pins)
        (root/'model-results.json').write_text(json.dumps(candidates,indent=2));print('VALIDATION180_RESULT',tag,json.dumps(chosen['metrics']),flush=True)
        if feasible and (best is None or chosen['metrics']['netPnl']>best[3]['metrics']['netPnl']):best=(model,kind,tag,chosen)
        del output;gc.collect()
    if best is not None:
        model,kind,tag,chosen=best;freeze=dict(model=tag,policy=chosen['policy'],heldout=config['heldout'],frozenAtUtc=datetime.now().astimezone().isoformat());(root/'candidate-freeze.json').write_text(json.dumps(freeze,indent=2));stress=[];error=None
        try:
            if (Path(config['priorRun'])/'candidate-freeze.json').exists():raise ValueError('Prior run consumed reserved holdout; need a fresh future date')
            claim_holdout(root.parent,config['heldout']['date'].replace('-',''),'225MC',root,freeze)
            m=aggregate_source(config['heldout']['path'],root/'cache',ordering_policy='quarantine',features_v2=True);m.update(role='heldout',riskReplayReliable=m['ordering']==0);(root/'heldout-source-manifest.json').write_text(json.dumps(m,indent=2));heldout=load_days([m])
            if not m['riskReplayReliable'] or len(heldout)!=2 or not all(d['fullSession'] for d in heldout):raise ValueError('Heldout quality not sufficient')
            output,_=predict(model,kind,heldout,norm)
            for cost in config['costTicks']:stress.append(score_policy(root,config,heldout,output,chosen['policy'],cost,full_risk=True))
        except (ValueError,FileExistsError,subprocess.CalledProcessError) as e:error=str(e)+(' '+str(e.stderr) if isinstance(e,subprocess.CalledProcessError) else '')
        (root/'heldout-results.json').write_text(json.dumps(dict(model=tag,qualityError=error,stress=stress,historicalHeldoutPass=error is None and bool(stress) and eligible(stress[0],1),formalFutureValidationComplete=False,deploymentEligible=False),indent=2))
    report=['# 第四輪：30秒行情／180秒交易價值','','尚未達成正式驗收；凍結後5個新完整策略日、事件日曆仍待驗證。','','|模型|交易數|淨JPY|勝率%|淨PF|每日最少|最大日虧損|通過全量驗證|','|---|---:|---:|---:|---:|---:|---:|---|']
    for c in candidates:
        m=c['selected']['metrics'];report.append(f"|{c['model']}|{m['completedTrades']}|{m['netPnl']}|{m['winRate']}|{m['netProfitFactor']}|{m['minimumDailyCompletedTrades']}|{m['maximumDailyLoss']}|{c['validationRuleQualified']}|")
    (root/'research-report.md').write_text('\n'.join(report),encoding='utf-8')
    if any(hashlib.sha256((root/p).read_bytes()).hexdigest()!=sha for p,sha in {**pins,**binary_pins}.items()):raise ValueError('Frozen source/binary changed')

if __name__=='__main__':
    root=Path(sys.argv[1]).resolve()
    if '--worker' in sys.argv:worker(root)
    else:
        config=json.loads((root/'experiment-config.json').read_text(encoding='utf-8'));prior=Path(config['priorRun'])
        if not (prior/'resource-summary.json').exists() or json.loads((prior/'resource-summary.json').read_text())['outcome'] not in ('completed','failed','stopped'):raise ValueError('Prior research still active')
        print(json.dumps(guarded_run([sys.executable,str(root/'source/run_action_alpha.py'),str(root),'--worker'],root),indent=2))