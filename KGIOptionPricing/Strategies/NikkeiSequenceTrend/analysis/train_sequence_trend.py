"""Sequential CPU experiments, training-only normalization, ledger-based selection."""
import json,time,hashlib,csv,subprocess,math,sys,io
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent.parent/'deps'))
import numpy as np
import torch
from torch.utils.data import Dataset,DataLoader
import sequence_dataset,importlib
# Pin final implementation before constructing training datasets; aggregation
# source and training source hashes are recorded separately.
importlib.reload(sequence_dataset)
from sequence_dataset import build_samples,FEATURE_NAMES
from sequence_model import SequenceModel,qualify,ACCEPTANCE_CRITERIA
import research_resource_guard
importlib.reload(research_resource_guard)
from research_resource_guard import Rate,Budget,limited_read,limited_write

class Windows(Dataset):
    def __init__(self,days,norm,inference=False):
        self.days=days;self.norm=norm
        self.lookup=[(d,int(i),0.,1) for d in days for i in d['samples'].inference_indices] if inference else [(d,int(i),float(y),int(c)) for d in days for i,y,c in zip(d['samples'].indices,d['samples'].targets,d['samples'].classes)]
    def __len__(self):return len(self.lookup)
    def __getitem__(self,k):
        d,i,y,c=self.lookup[k];x,a=d['samples'].window(i)
        x=np.clip((x-self.norm['mean'])/self.norm['scale'],-10,10)
        a=np.clip((a-self.norm['auxMean'])/self.norm['auxScale'],-10,10)
        return torch.from_numpy(x.astype(np.float32)),torch.from_numpy(a.astype(np.float32)),torch.tensor(y,dtype=torch.float32),torch.tensor(c)

def loaders(days,norm,shuffle=False,inference=False):
    return DataLoader(Windows(days,norm,inference),batch_size=32,shuffle=shuffle,num_workers=0,pin_memory=False)

def evaluate(model,days,norm,inference=False):
    model.eval();outputs={};squared=absolute=correct=count=0
    with torch.inference_mode():
        for day in days:
            ps=[];rs=[]
            for x,a,y,c in loaders([day],norm,inference=inference):
                logits,r=model(x,a);p=torch.softmax(logits,1)
                squared+=float(((r-y)**2).sum());absolute+=float((r-y).abs().sum());correct+=int((p.argmax(1)==c).sum());count+=len(y)
                ps.append(p.numpy());rs.append(r.numpy())
            indices=day['samples'].inference_indices if inference else day['samples'].indices
            outputs[day['id']]=(indices,np.concatenate(ps) if ps else np.empty((0,3)),np.concatenate(rs) if rs else np.empty(0))
    return dict(mse=squared/count if count else None,mae=absolute/count if count else None,accuracy=correct/count if count else None,samples=count),outputs

def write_csv(path,rows,header,root):
    rate=Rate(5*1024**2);budget=Budget(root);size=0
    with Path(path).open('w',encoding='utf-8',newline='') as f:
        line=','.join(header)+'\n';f.write(line);size+=len(line)
        for row in rows:
            line=','.join(f'{v:.7f}' if isinstance(v,(float,np.floating)) else str(v) for v in row)+'\n'
            rate.consume(len(line));size+=len(line)
            if size>256*1024**2:raise ValueError('Single replay file limit')
            f.write(line)
    budget.check()

def replay(root,config,day,output,threshold,trend,cost,policy=None,raw_source=None):
    temp=root/'replay-temp';temp.mkdir(exist_ok=True)
    days=day if isinstance(day,list) else [day]
    days=sorted(days,key=lambda d:d['samples'].times[0]);key=days[0]['id'][:10]+'-combined'
    quotes=temp/(key+'-quotes.csv');trades=temp/(key+'-trades.csv')
    def rows():
        for day in days:
            indices,p,r=output[day['id']];s=day['samples'];index={int(i):j for j,i in enumerate(indices)}
            for i,q in enumerate(day['quotes']):
                if i in index:
                    j=index[i];probs=p[j];delta=r[j]
                else:probs=[0.,1.,0.];delta=0.
                trend60=s.mid[i]-s.mid[max(0,i-12)];trend300=s.mid[i]-s.mid[max(0,i-60)]
                yield [*q[:3],int(q[3] and i in index),*probs,delta,trend60,trend300]
    write_csv(quotes,rows(),['time','bid','ask','valid','down','neutral','up','deltaTicks','trend60','trend300'],root)
    if not trades.exists():write_csv(trades,(row for d in days for row in d['trades']),['time','price','sequence'],root)
    cmd=['dotnet',config['bridge'],str(quotes),str(trades),str(cost),str(threshold),str(trend)]
    if policy is not None:cmd += [str(policy['stopPoints']),str(True),str(policy['minimumNetTicks']),str(policy['exitThreshold']),str(policy['maximumHoldSeconds'])]
    if raw_source is not None:
        if policy is None:raise ValueError('Full risk replay requires explicit policy')
        cmd.append(str(raw_source))
    completed=subprocess.run(cmd,capture_output=True,text=True,encoding='utf-8',timeout=600 if raw_source is not None else 120,check=True)
    value=json.loads(completed.stdout);value.update(session=key,memberSessions=[d['id'] for d in days],fullSession=all(d['fullSession'] for d in days) and any(d['id'].endswith('day') for d in days) and any(d['id'].endswith('night') for d in days),costTicks=cost,threshold=threshold,trendFilter=trend)
    value['fullRiskReplay']=raw_source is not None
    value['riskCoverage']='all-recorded-MC-books-and-trades' if raw_source is not None else '5-second-books-and-all-trades'
    return value

def day_groups(days):
    grouped={}
    for day in days:grouped.setdefault(day['id'][:10],[]).append(day)
    return list(grouped.values())

def totals(rows):
    n=sum(r['completedTrades'] for r in rows);wins=sum(r['winningTrades'] for r in rows);net=sum(r['netPnl'] for r in rows)
    positive=sum(r['winningNet'] for r in rows);negative=sum(r['losingNet'] for r in rows)
    losses=[r['maximumIntradayLoss'] for r in rows]
    win=100*wins/n if n else None;pf=positive/-negative if negative<0 else None
    return dict(acceptanceCriteria=ACCEPTANCE_CRITERIA,minimumDailyCompletedTrades=min((r['completedTrades'] for r in rows),default=None),dailyTradeCounts=[dict(day=r.get('session'),completedTrades=r['completedTrades']) for r in rows],completedTrades=n,netPnl=net,winRate=win,netProfitFactor=pf,maximumDailyLoss=max(losses,default=0),passesNumericalThresholds=qualify(net,win,pf,losses,[r['completedTrades'] for r in rows]),hasOpenPosition=any(r['openQuantity']!=0 for r in rows),completeDayCoverage=bool(rows) and all(r['fullSession'] for r in rows),dailyRiskVerified=bool(rows) and all(r['fullSession'] and r.get('fullRiskReplay',False) for r in rows))

def train_and_evaluate(root,config,manifests):
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    source_pins={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'source').glob('*.py')}
    (root/'training-source-sha256.json').write_text(json.dumps(source_pins,indent=2),encoding='utf-8')
    days=[]
    for source in manifests:
        for item in source['sessions']:
            with np.load(limited_read(item['cache']),allow_pickle=False) as a:
                x=a['x'].copy();mid=a['mid'].copy();times=a['times'].copy();valid=a['valid'].copy()
                d=dict(id=item['id'],role=source['role'],samples=build_samples(x,mid,times,valid),quotes=a['quotes'].copy(),trades=a['trades'].copy(),fullSession=item['fullSession'])
            if len(d['samples'].indices):days.append(d)
    train=[d for d in days if d['role']=='train'];validation=[d for d in days if d['role']=='validation'];development=[d for d in days if d['role']=='development']
    split_ruling=None
    if train and not validation and development:
        # Availability-only fallback fixed before any normalization/model fitting.
        # Both old validation sources were rejected for ordering, not performance.
        first=min(d['id'][:10] for d in development)
        validation=[d for d in development if d['id'].startswith(first)]
        development=[d for d in development if not d['id'].startswith(first)]
        for d in validation:d['role']='validation'
        split_ruling='Both originally specified validation sources rejected; first available subsequent development date moved to validation before fitting: '+first
    if not all((train,validation,development)):raise ValueError('Not enough independent date splits')
    data=np.concatenate([d['samples'].x[d['samples'].valid] for d in train])
    aux=np.stack([d['samples'].window(int(i))[1] for d in train for i in d['samples'].indices[::10]])
    norm=dict(mean=data.mean(0),scale=np.maximum(data.std(0),.01),auxMean=aux.mean(0),auxScale=np.maximum(aux.std(0),.01))
    normbuf=io.BytesIO();np.savez_compressed(normbuf,**norm);limited_write(root/'normalizer.npz',normbuf.getvalue(),root)
    dates={role:[d['id'] for d in days if d['role']==role] for role in ['train','validation','development']}
    (root/'split-manifest.json').write_text(json.dumps(dict(dates=dates,samples={role:sum(len(d['samples'].indices) for d in days if d['role']==role) for role in dates},availabilityRuling=split_ruling,historicalDatesArePreviouslyViewed=True,futureGate=config['futureGate'],macroCalendarKnown=False,cmeHolidayCalendarVerified=False),indent=2),encoding='utf-8')
    results=[];daily=[];modeldir=root/'models';modeldir.mkdir(exist_ok=True)
    for kind in config['models']:
        for seed in config['seeds']:
            torch.manual_seed(seed);np.random.seed(seed)
            model=SequenceModel(kind);optimizer=torch.optim.AdamW(model.parameters(),lr=config['learningRate'],weight_decay=1e-4)
            best=float('inf');best_state=None;stale=0;history=[];started=time.monotonic()
            for epoch in range(config['epochs']):
                model.train();loss_total=0.;batches=0
                for x,a,y,c in loaders(train,norm,True):
                    logits,pred=model(x,a);loss=torch.nn.functional.cross_entropy(logits,c)+.25*torch.nn.functional.smooth_l1_loss(pred,y)
                    optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),1);optimizer.step();loss_total+=float(loss.detach());batches+=1
                v,outputs=evaluate(model,validation,norm);value=v['mse'];history.append(dict(epoch=epoch+1,trainLoss=loss_total/max(batches,1),validation=v))
                print(f'TRAIN {kind} seed{seed} epoch{epoch+1} valMSE={value:.5f}',flush=True)
                if value<best-1e-5:best=value;best_state={k:v.detach().clone() for k,v in model.state_dict().items()};stale=0
                else:stale+=1
                if stale>=config['patience']:break
            model.load_state_dict(best_state);model.eval();v,_=evaluate(model,validation,norm);_,voutputs=evaluate(model,validation,norm,inference=True)
            choices=[]
            for threshold in config['thresholds']:
                for trend in config['trendFilters']:
                    rr=[replay(root,config,group,voutputs,threshold,trend,0) for group in day_groups(validation)]
                    aggregate=totals(rr);choices.append(dict(threshold=threshold,trend=trend,score=aggregate['netPnl'],metrics=aggregate))
            # Explicitly select by validation net P&L, never test-date outcomes.
            feasible=[q for q in choices if q['metrics']['passesNumericalThresholds'] and q['metrics']['dailyRiskVerified'] and not q['metrics']['hasOpenPosition']]
            choice=max(feasible or choices,key=lambda q:q['score']);prediction,_=evaluate(model,development,norm);_,outputs=evaluate(model,development,norm,inference=True)
            tag=f'{kind}-seed{seed}';modelbuf=io.BytesIO();torch.save(model.state_dict(),modelbuf);limited_write(modeldir/(tag+'.pt'),modelbuf.getvalue(),root)
            onnx_status=dict(status='deferred-to-existing-compatible-export-environment')
            per_model=[]
            for cost in config['costTicks']:
                rr=[replay(root,config,group,outputs,choice['threshold'],choice['trend'],cost) for group in day_groups(development)]
                aggregate=totals(rr);per_model.append(dict(costTicks=cost,**aggregate))
                for row in rr:daily.append(dict(model=tag,**row))
            result=dict(model=tag,architecture=kind,seed=seed,trainingSeconds=time.monotonic()-started,history=history,validationChoices=choices,selectedValidationRule=choice,validationRuleQualified=bool(feasible),developmentPrediction=prediction,developmentTrading=per_model,onnx=onnx_status,deploymentEligible=False,futureValidationComplete=False)
            manifest=dict(acceptanceCriteria=ACCEPTANCE_CRITERIA,schema='nikkei-sequence-multiscale-research-v1',model=tag,architecture=kind,sequenceShape=[1,120,39],backgroundShape=[1,16],features=FEATURE_NAMES,samplingSeconds=5,predictionSeconds=30,lookbackSeconds=600,backgroundWindowsSeconds=[1800,7200],sourceClock='WallReceiveTime;Python fractions truncated to microseconds;sequence retained',trainingDates=dates['train'],validationDates=dates['validation'],developmentDates=dates['development'],modelSha256=hashlib.sha256((modeldir/(tag+'.pt')).read_bytes()).hexdigest(),normalizerSha256=hashlib.sha256((root/'normalizer.npz').read_bytes()).hexdigest(),trainingSourceSha256=source_pins,threshold=choice['threshold'],trendFilter=choice['trend'],validationRuleQualified=bool(feasible),macroCalendarKnown=False,cmeHolidayCalendarVerified=False,onnx=onnx_status,deploymentEligible=False)
            (modeldir/(tag+'.manifest.json')).write_text(json.dumps(manifest,indent=2),encoding='utf-8')
            results.append(result)
            (root/'model-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
            print('MODEL_DONE',tag,json.dumps(per_model[0]),flush=True)
    # Export in the pre-existing RL environment, under this C: working directory
    # and inherited Job Object. Libraries on E: are read-only; no installation.
    exporter=config.get('exportPython')
    if exporter:
        subprocess.run([exporter,'-B',str(root/'source/export_sequence_onnx.py'),str(root)],cwd=root,check=True,timeout=180)
        export_reports=json.loads((root/'onnx-export-report.json').read_text(encoding='utf-8'))
        import onnxruntime as ort
        for result in results:
            tag=result['model'];graph=modeldir/(tag+'.onnx');status=export_reports.get(tag,{})
            if graph.exists():
                options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1
                session=ort.InferenceSession(str(graph),sess_options=options,providers=['CPUExecutionProvider'])
                model=SequenceModel(result['architecture']);model.load_state_dict(torch.load(modeldir/(tag+'.pt'),weights_only=True));model.eval();error=0.
                x,a,_,_=next(iter(loaders(development,norm)))
                with torch.inference_mode():
                    for j in range(4):
                        xx=x[j:j+1];aa=a[j:j+1];expected=model(xx,aa);got=session.run(None,{'sequence':xx.numpy(),'background':aa.numpy()})
                        error=max(error,max(float(np.max(np.abs(expected[k].numpy()-got[k]))) for k in range(2)))
                status=dict(status='passed' if error<=1e-4 else 'rejected',maxOutputError=error,scope='Four normalized development windows; not full C# feature parity',exportEnvironment=status)
            result['onnx']=status
            manifest_path=modeldir/(tag+'.manifest.json');manifest=json.loads(manifest_path.read_text(encoding='utf-8'));manifest['onnx']=status
            if graph.exists():manifest['onnxSha256']=hashlib.sha256(graph.read_bytes()).hexdigest()
            manifest_path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
        (root/'model-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
    # Serialized aggregate output only, no decision or execution detail retained.
    with (root/'daily-performance.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(daily[0]));writer.writeheader();writer.writerows(daily)
    report=['# 多尺度序列模型初次研究結果','','歷史日期均已檢視，屬開發／回溯評估；沒有正式合格模型。未完成五個新完整日、事件日程、完整CME日曆或C#序列逐值驗證。','', '|模型|完成交易|淨損益JPY|淨勝率%|淨獲利因子|最大盤別虧損JPY|','|---|---:|---:|---:|---:|---:|']
    for r in results:
        q=r['developmentTrading'][0];report.append(f"|{r['model']}|{q['completedTrades']}|{q['netPnl']}|{q['winRate']}|{q['netProfitFactor']}|{q['maximumDailyLoss']}|")
    report+=['','輸入：120×39短尺度＋16欄30分鐘／2小時背景統計。所有成本由共用C#帳本計算，同策略日盤及當晚夜盤連續使用同帳本與日損狀態；此為策略風控日定義，不等同JPX交易所結算日。缺少完整夜盤的日期不可認證整日風控。','', '原模型未更換，未啟動實盤。']
    (root/'research-report.md').write_text('\n'.join(report),encoding='utf-8')
    if any(hashlib.sha256((root/p).read_bytes()).hexdigest()!=sha for p,sha in source_pins.items()):raise ValueError('Training code changed during fitting')
