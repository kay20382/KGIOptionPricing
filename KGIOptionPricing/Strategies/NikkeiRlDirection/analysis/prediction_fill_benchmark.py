"""Causal offline benchmark; shared C# execution and accounting remain authoritative."""
import numpy as np
import hashlib
from pathlib import Path

def market_columns():return list(range(30))+list(range(39,48))+list(range(54,60))

def label_samples(rows,horizon):
    if horizon not in (15,30,60,120):raise ValueError('Unsupported horizon')
    columns=market_columns();times=np.array([r['t'] for r in rows]);steps=horizon//3
    if any(len(r['x'])!=60 for r in rows):raise ValueError('Expected60 observations')
    if len(times)>1 and np.any(np.diff(times)<=0):raise ValueError('Unordered frames')
    bad=np.array([not r['valid'] or r['gap'] or r['bid']<=0 or r['ask']<=r['bid']
                  or not np.isfinite(r['x']).all() for r in rows],dtype=int)
    prefix=np.r_[0,np.cumsum(bad)];x=[];y=[];indices=[]
    for i in range(max(0,len(rows)-steps)):
        j=i+steps
        if not rows[i]['entryAllowed'] or prefix[j+1]!=prefix[i]:continue
        if not np.all(np.abs(np.diff(times[i:j+1])-3)<1e-5):continue
        x.append([rows[i]['x'][c] for c in columns])
        y.append(((rows[j]['bid']+rows[j]['ask'])-(rows[i]['bid']+rows[i]['ask']))/10)
        indices.append(i)
    return np.asarray(x,dtype=float).reshape(-1,len(columns)),np.asarray(y),np.asarray(indices,dtype=int)

def quote_markout(rows,at,price,side,horizon):
    if side not in (-1,1):raise ValueError('Invalid side')
    import bisect
    times=[r['t'] for r in rows];i=bisect.bisect_left(times,at+horizon)
    if i==len(rows):return None
    r=rows[i];delay=r['t']-at-horizon
    if delay>3.00001 or not r['valid'] or r['gap'] or r['bid']<=0 or r['ask']<=r['bid']:return None
    # This is a sampled price markout, not a completed trade or another ledger.
    return dict(valueJPY=side*((r['bid']+r['ask'])/2-price)*10,delaySeconds=delay)

def daily_metrics(rows):
    from completed_daily import aggregate_daily
    value=aggregate_daily(rows)
    value['expectedGrossPnlPerTradeJPY']=value['grossPnlJPY']/value['trades'] if value['trades'] else None
    return value

def file_sha(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
    return h.hexdigest().upper()

def verify_pin(pin):
    for path,expected in pin.items():
        if file_sha(path)!=expected.upper():raise ValueError('Frozen input changed: '+path)

def prediction_metrics(y,prediction):
    if len(y)==0:return dict(samples=0,maeTicks=None,zeroMaeTicks=None,maeImprovementTicks=None,
        mseTicksSquared=None,zeroMseTicksSquared=None,mseImprovementTicksSquared=None,rSquared=None,
        correlation=None,signalCount=0,signalNonzeroActualCount=0,signalDirectionAccuracyPercent=None,signalMeanSignedMoveJPY=None)
    chosen=np.abs(prediction)>=2;nonzero=chosen&(y!=0)
    mae=float(np.mean(np.abs(y-prediction)));zero=float(np.mean(np.abs(y)))
    mse=float(np.mean((y-prediction)**2));zero_mse=float(np.mean(y**2));variance=float(np.var(y))
    return dict(samples=len(y),maeTicks=mae,zeroMaeTicks=zero,maeImprovementTicks=zero-mae,
        mseTicksSquared=mse,zeroMseTicksSquared=zero_mse,mseImprovementTicksSquared=zero_mse-mse,
        rSquared=1-mse/variance if variance>0 else None,
        correlation=float(np.corrcoef(y,prediction)[0,1]) if np.std(y)>1e-9 and np.std(prediction)>1e-9 else None,
        signalCount=int(chosen.sum()),signalNonzeroActualCount=int(nonzero.sum()),signalDirectionAccuracyPercent=float(100*np.mean(np.sign(y[nonzero])==np.sign(prediction[nonzero]))) if nonzero.any() else None,
        signalMeanSignedMoveJPY=float(np.mean(np.sign(prediction[chosen])*y[chosen])*50) if chosen.any() else None)

class BridgeClient:
    def __init__(self,bridge,dataset):
        import subprocess
        self.process=subprocess.Popen(['dotnet',str(bridge),'serve',str(dataset)],stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,encoding='utf-8',bufsize=1)
    def request(self,**value):
        import json
        self.process.stdin.write(json.dumps(value,separators=(',',':'))+'\n');self.process.stdin.flush()
        raw=self.process.stdout.readline()
        if not raw:raise RuntimeError('C# diagnostic bridge stopped')
        result=json.loads(raw)
        if 'error' in result:raise RuntimeError(result['error'])
        return result
    def close(self):
        if self.process.poll() is None:
            try:self.request_close()
            finally:
                try:self.process.wait(timeout=10)
                except Exception:self.process.terminate();self.process.wait(timeout=10)
    def request_close(self):
        self.process.stdin.write('{"op":"close"}\n');self.process.stdin.flush()

def collect_episode(client,episode,config):
    from direction_warmstart import parse_at
    packet=client.request(op='reset',episode=episode,config=config,mode='wait');rows=[]
    while True:
        state=packet['state'];quote=packet['quote']
        rows.append(dict(t=parse_at(quote['at']).timestamp(),bid=quote['bid'],ask=quote['ask'],
            valid=quote['valid'],gap=quote['dataGap'],x=state['observation'],
            entryAllowed=state['mask'][1] and state['mask'][2]))
        if state['terminated'] or state['truncated']:break
        packet=client.request(op='step')
    return rows,packet['summary']

def distribution(values):
    if not values:return dict(count=0,mean=None,p50=None,p90=None,p99=None,max=None)
    a=np.asarray(values)
    return dict(count=len(a),mean=float(a.mean()),p50=float(np.percentile(a,50)),
        p90=float(np.percentile(a,90)),p99=float(np.percentile(a,99)),max=float(a.max()))

def prediction_cohorts(attempts):
    result={}
    for cohort in ('filled','unfilled'):
        selected=[a for a in attempts if a['filled']==(cohort=='filled')]
        values=[a['predictionTicks'] for a in selected if a['predictionTicks'] is not None and np.isfinite(a['predictionTicks'])]
        result[cohort]=dict(distribution(values),missing=len(selected)-len(values),absolute=distribution([abs(v) for v in values]))
    return result

def replay_episode(client,episode,config,quotes,mode,model=None):
    from direction_warmstart import parse_at
    packet=client.request(op='reset',episode=episode,config=config,mode=mode,**({'model':str(model)} if model else {}))
    attempts={};completed=[];entry=None;timings=[];reasons={};index=0
    while not (packet['state']['terminated'] or packet['state']['truncated']):
        before=packet['state'];packet=client.request(op='step');state=packet['state']
        if packet['inferenceMs'] is not None and index>0:timings.append(packet['inferenceMs'])
        reason=state['overrideReason']
        if reason:reasons[reason]=reasons.get(reason,0)+1
        if state['effectiveAction'] in (1,2) and before['position']==0 and not before['pendingEntry']:
            attempts[index]=dict(index=index,at=quotes[index]['t'],side=1 if state['effectiveAction']==1 else -1,
                price=(quotes[index]['bid']+quotes[index]['ask'])/2,predictionTicks=packet['prediction'],filled=False)
        contexts=iter(packet['contexts'])
        for fill in state['fills']:
            if fill['role'] in (0,1):
                context=next(contexts)['pendingContext'];signal_index=context['signalIndex']
                if entry is not None or signal_index not in attempts:raise ValueError('Unpaired diagnostic entry')
                attempts[signal_index]['filled']=True
                entry=dict(at=parse_at(fill['timestamp']).timestamp(),price=fill['price'],side=1 if fill['role']==0 else -1,
                    signalIndex=signal_index,signalAt=quotes[signal_index]['t'])
            else:
                if entry is None:raise ValueError('Unpaired diagnostic exit')
                completed.append(dict(entry,exitRole=fill['role'],holdingSeconds=parse_at(fill['timestamp']).timestamp()-entry['at'],
                    grossPnlJPY=state['completedGrossPnl']-before['completedGrossPnl'],
                    netPnlJPY=state['completedNetPnl']-before['completedNetPnl']))
                entry=None
        index+=1
    summary=packet['summary']
    if len(completed)!=summary['trades'] or (entry is not None)!=bool(summary['excludedOpenPositions']):raise ValueError('Diagnostic/shared ledger count mismatch')
    diagnostics=dict(entryIntents=len(attempts),entryFills=sum(a['filled'] for a in attempts.values()),
        unfilledIntents=sum(not a['filled'] for a in attempts.values()),completedTrades=len(completed),
        excludedOpenPositions=summary['excludedOpenPositions'],entryFillRatePercent=100*sum(a['filled'] for a in attempts.values())/len(attempts) if attempts else None,
        signalToFillSeconds=distribution([r['at']-r['signalAt'] for r in completed]),
        holdingSeconds=distribution([r['holdingSeconds'] for r in completed]),reasons=reasons,
        inferenceMs=distribution(timings),inferenceAbove1ms=sum(t>1 for t in timings),markouts={},signalOutcomes={},exitRoles={},
        predictionScoresTicks=prediction_cohorts(list(attempts.values())))
    for h in (3,15,30):
        values=[quote_markout(quotes,e['at'],e['price'],e['side'],h) for e in completed]
        found=[v for v in values if v is not None]
        diagnostics['markouts'][str(h)]=dict(relativeTo='ActualEntryFillPrice',sampledMidMoveJPY=distribution([v['valueJPY'] for v in found]),
            sampleDelaySeconds=distribution([v['delaySeconds'] for v in found]),missing=len(values)-len(found),
            adversePercent=100*sum(v['valueJPY']<0 for v in found)/len(found) if found else None)
    for cohort in ('filled','unfilled'):
        selected=[a for a in attempts.values() if a['filled']==(cohort=='filled')]
        for h in (15,30,60,120):
            values=[quote_markout(quotes,a['at'],a['price'],a['side'],h) for a in selected]
            diagnostics['signalOutcomes'][cohort+str(h)]=dict(signalCount=len(selected),missing=sum(v is None for v in values),
                signedSignalMidMoveJPY=distribution([v['valueJPY'] for v in values if v is not None]))
    for role,name in ((2,'TakeProfit'),(3,'StopLoss'),(4,'Flatten')):
        group=[r for r in completed if r['exitRole']==role]
        diagnostics['exitRoles'][name]=dict(count=len(group),grossPnlJPY=sum(r['grossPnlJPY'] for r in group),
            netPnlJPY=sum(r['netPnlJPY'] for r in group),holdingSeconds=distribution([r['holdingSeconds'] for r in group]))
    return summary,diagnostics

def export_model(kind,model,means,scales,path,horizon,training_dates):
    import json
    result=dict(schema='nikkei-prediction-baseline-v1',kind=kind,observationSize=60,columns=market_columns(),means=means.tolist(),scales=scales.tolist(),
        normalizedClip=8,horizonSeconds=horizon,trainingDates=training_dates,signalThresholdTicks=2,
        usage='OfflineResearchPreviouslyInspected',deploymentEligible=False)
    if kind=='ridge':result.update(coefficients=model[0].tolist(),intercept=float(model[1]))
    else:
        result.update(initial=float(model.init_.constant_[0,0]),learningRate=float(model.learning_rate),trees=[])
        for estimator in model.estimators_[:,0]:
            t=estimator.tree_;result['trees'].append(dict(left=t.children_left.tolist(),right=t.children_right.tolist(),
                feature=t.feature.tolist(),threshold=t.threshold.tolist(),value=t.value[:,0,0].tolist()))
    Path(path).write_text(json.dumps(result,ensure_ascii=False,allow_nan=False),encoding='utf-8')

def write_csv(path,rows):
    import csv
    if not rows:return
    fields=list(dict.fromkeys(k for row in rows for k in row))
    with Path(path).open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader();writer.writerows(rows)

def main():
    import argparse,json,time,sys,warnings,platform
    from sklearn.ensemble import GradientBoostingRegressor
    import sklearn,scipy
    parser=argparse.ArgumentParser();parser.add_argument('--output',required=True);parser.add_argument('--bridge',required=True)
    parser.add_argument('--cache-root',required=True);parser.add_argument('--historical-root',required=True)
    args=parser.parse_args();output=Path(args.output)
    if output.exists():raise ValueError('Use a new immutable output root')
    output.mkdir(parents=True);(output/'models').mkdir()
    cache_root=Path(args.cache_root);historical=Path(args.historical_root)
    dataset=json.loads((cache_root/'evaluation-dataset.json').read_text(encoding='utf-8'));indexes={};pin={};complete={};excluded=[]
    def freeze(path):pin[str(Path(path).resolve())]=file_sha(path)
    freeze(cache_root/'evaluation-dataset.json')
    coverage_pin=json.loads((historical.parent/'nikkei-rl-pruned-earlier-coverage-verification-20261009/coverage-inputs.json').read_text(encoding='utf-8'))
    verify_pin(coverage_pin);pin.update(coverage_pin)
    for episode,path in dataset.items():
        manifest_path=Path(path+'.manifest.json');manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
        if manifest['schema']!='nikkei-rl-cache-pruned-flow-v1-evaluation' or manifest['researchReadinessProfile']!='exclude-flow' or manifest['decisionIntervalSeconds']!=3:raise ValueError('Wrong cache readiness/schema')
        if any(manifest[k]!=0 for k in ('badRows','outOfOrder','dropped','dataGapFrames')):raise ValueError('Invalid segment')
        if file_sha(path)!=manifest['cacheSha256']:raise ValueError('Cache identity mismatch')
        index_path=Path(path).parent/'segment-index.json'
        if str(index_path) not in coverage_pin:raise ValueError('Unpinned coverage index')
        index=json.loads(index_path.read_text(encoding='utf-8'));date=index['date']
        if date in indexes and indexes[date]!=index:raise ValueError('Same-date coverage differs')
        indexes[date]=index;freeze(path);freeze(manifest_path)
        matching=[s for s in index['segments'] if s['id']==episode and not s['excluded']]
        if len(matching)!=1 or matching[0]['cacheSha256']!=manifest['cacheSha256']:raise ValueError('Segment coverage identity mismatch')
        if index['fullDayCoverage']:complete[episode]=path
        else:excluded.append(dict(date=date,episode=episode,reason='PartialDayOrInternalGap'))
    dates=sorted({e[:8] for e in complete});training=[d for d in dates if d<='20260924'];validation=['20260929','20260930'];development=['20261006','20261007','20261008']
    if len(training)!=8 or set(dates)!=set(training+validation+development):raise ValueError('Expected13 complete historical dates')
    configuration=dict(allowIncompleteEvaluation=True,commission=36,extraCostTicksPerSide=0,decisionLatencyMs=1,
        decisionIntervalSeconds=3,featureProfile='mini-micro-research',researchReadinessProfile='exclude-flow',
        directionDrivenHolding=True,maxHoldingSeconds=0,cooldownSeconds=10,observationSize=60,stopMode='fixed',
        fixedStopPoints=150,maxStopPoints=150,tickSize=5,multiplier=10,entryPriceMode='same-side')
    params=dict(trainingDates=training,validationDates=validation,developmentDates=development,allPreviouslyInspected=True,
        horizonsSeconds=[15,30,60,120],marketColumns=market_columns(),ridgeAlpha=10,gbr=dict(n_estimators=60,max_depth=3,learning_rate=.05,min_samples_leaf=100,random_state=20261011),
        execution=configuration,extraCostTicksPerSide=[0,1,2],signalThresholdTicks=2,excludedDays=excluded,
        versions=dict(python=platform.python_version(),numpy=np.__version__,sklearn=sklearn.__version__,scipy=scipy.__version__),
        dependencyWarning='SciPy1.9.1 declares numpy<1.25; installed numpy1.26.4. GBR fit/predict smoke passed; no dependency changed.',
        targets=dict(minDailyTrades=50,minNetWinRatePercent=40,minGrossExpectancyJPY=50),deploymentEligible=False)
    for f in Path(args.bridge).parent.glob('*.dll'):freeze(f)
    for pattern in ('*.runtimeconfig.json','*.deps.json'):
        for f in Path(args.bridge).parent.glob(pattern):freeze(f)
    for name in ('prediction_fill_benchmark.py','completed_daily.py','fill_diagnostics.py','direction_warmstart.py','test_prediction_fill_benchmark.py'):freeze(Path(__file__).parent/name)
    bridge_source=Path(__file__).parent/'bridge-source'
    if not bridge_source.exists():bridge_source=Path('tools/NikkeiPredictionDiagnostics')
    for pattern in ('*.cs','*.csproj'):
        for f in bridge_source.glob(pattern):freeze(f)
    actor=historical/'models/pruned-earlier/policy-seed22.onnx';freeze(actor);freeze(actor.with_suffix('.manifest.json'))
    dataset_path=output/'dataset.json';dataset_path.write_text(json.dumps(complete,indent=2),encoding='utf-8');freeze(dataset_path)
    parameters_path=output/'parameters.json';parameters_path.write_text(json.dumps(params,ensure_ascii=False,indent=2),encoding='utf-8');freeze(parameters_path)
    def save(name,value):
        (output/name).write_text(json.dumps(value,ensure_ascii=False,indent=2,allow_nan=False),encoding='utf-8')
    save('frozen-inputs.json',pin);verify_pin(pin)
    client=BridgeClient(args.bridge,dataset_path);data={};wait={};prediction_rows=[];models=[];parity=[];performance=[];diagnostic_rows=[]
    try:
        for episode in sorted(complete):
            rows,summary=collect_episode(client,episode,configuration);data[episode]=rows;wait[episode]=summary
            print('DATA',episode,len(rows),'valid',sum(r['valid'] for r in rows),flush=True)
        for horizon in (15,30,60,120):
            labels={e:label_samples(rows,horizon) for e,rows in data.items()}
            x=np.concatenate([v[0] for e,v in labels.items() if e[:8] in training]);y=np.concatenate([v[1] for e,v in labels.items() if e[:8] in training])
            means=x.mean(0);scales=x.std(0);scales[scales<1e-9]=1;z=np.clip((x-means)/scales,-8,8)
            zm=z.mean(0);ym=y.mean();weights=np.linalg.solve((z-zm).T@(z-zm)+10*np.eye(z.shape[1]),(z-zm).T@(y-ym))
            fitted={'ridge':(weights,ym-zm@weights),'gbr':GradientBoostingRegressor(**params['gbr']).fit(z,y)}
            for kind in ('zero','ridge','gbr'):
                name=f'{kind}-{horizon}s';model_path=output/'models'/f'{name}.json'
                if kind!='zero':
                    export_model(kind,fitted[kind],means,scales,model_path,horizon,training);models.append((name,model_path))
                def predict(a):
                    transformed=np.clip((a-means)/scales,-8,8)
                    return np.zeros(len(a)) if kind=='zero' else transformed@weights+fitted[kind][1] if kind=='ridge' else fitted[kind].predict(transformed)
                for episode,(xx,yy,indices) in labels.items():
                    prediction_rows.append(dict(model=name,date=episode[:8],horizonSeconds=horizon,
                        usage='TrainingInSample' if episode[:8] in training else 'ValidationPreviouslyInspected' if episode[:8] in validation else 'DevelopmentPreviouslyInspected',
                        fullDayCoverage=True,**prediction_metrics(yy,predict(xx))))
                if kind!='zero':
                    selected=np.linspace(0,len(data['20261006-segment01'])-1,256,dtype=int)
                    raw=[data['20261006-segment01'][i]['x'] for i in selected]
                    actual=client.request(op='predict',model=str(model_path),observations=raw)['predictions']
                    expected=predict(np.asarray(raw)[:,market_columns()]);difference=float(np.max(np.abs(expected-actual)))
                    if difference>1e-7:raise ValueError('C#/Python model parity failed '+name+' '+str(difference))
                    parity.append(dict(model=name,samples=len(raw),maximumPredictionErrorTicks=difference,passed=True))
            print('TRAIN',horizon,'samples',len(y),'parity PASS',flush=True)
        for _,path in models:freeze(path)
        save('frozen-inputs.json',pin);verify_pin(pin)
        write_csv(output/'prediction-performance.csv',prediction_rows);save('prediction-performance.json',prediction_rows);save('model-parity.json',parity)
        modes=[(name,'baseline',path) for name,path in models]+[('Wait','wait',None),('pruned-earlier','actor',actor)]
        for name,mode,path in modes:
            for date in validation+development:
                if mode=='actor' and date in validation:continue
                episode=next(e for e in complete if e[:8]==date)
                for cost in (0,1,2):
                    config=dict(configuration,extraCostTicksPerSide=cost)
                    summary,diagnostics=replay_episode(client,episode,config,data[episode],mode,path)
                    metrics=daily_metrics([summary]);reasons=[]
                    if metrics['trades']<50:reasons.append('TradesBelow50')
                    if metrics['winRatePercent'] is None or metrics['winRatePercent']<40:reasons.append('NetWinRateBelow40Percent')
                    if metrics['expectedGrossPnlPerTradeJPY'] is None or metrics['expectedGrossPnlPerTradeJPY']<50:reasons.append('GrossExpectancyBelow50JPY')
                    row=dict(model=name,date=date,extraCostTicksPerSide=cost,fullDayCoverage=True,
                        usage='ValidationPreviouslyInspected' if date in validation else 'DevelopmentPreviouslyInspected',
                        hardTargetPass=not reasons,hardTargetReasons=';'.join(reasons),**metrics)
                    if mode=='actor':
                        old=json.loads((historical.parent/'nikkei-rl-daily-backfill-final-20261009/daily-performance.json').read_text(encoding='utf-8'))['daily']
                        match=next(r for r in old if r['group']=='pruned-earlier' and r['date']==date and r['extraCostTicksPerSide']==cost)
                        for field in ('trades','wins','losses','grossPnlJPY','netPnlJPY','costsJPY','excludedOpenPositions'):
                            if abs(row[field]-match[field])>1e-6:raise ValueError('Historical actor ledger changed '+field)
                    performance.append(row);diagnostic_rows.append(dict(model=name,date=date,cost=cost,**diagnostics))
                    print('REPLAY',name,date,'cost',cost,'N',metrics['trades'],'gross',metrics['grossPnlJPY'],'net',metrics['netPnlJPY'],flush=True)
                    save('progress.json',dict(stage='Replay',finishedRows=len(performance),expectedRows=144,lastModel=name,lastDate=date))
                    write_csv(output/'daily-performance.csv',performance);save('daily-performance.json',performance);save('fill-diagnosis.json',diagnostic_rows)
    finally:client.close()
    verify_pin(pin)
    qualified=[]
    for name,_ in models:
        rows=[r for r in performance if r['model']==name and r['date'] in development and r['extraCostTicksPerSide']==0]
        if len(rows)==3 and all(r['hardTargetPass'] for r in rows):qualified.append(name)
    save('verification.json',dict(done=True,frozenInputsUnchanged=True,cacheAndCoveragePinned=True,cSharpSharedLedgerReconciled=True,
        actorHistoricalRowsVerified=9,predictionModels=len(models),paritySamples=sum(p['samples'] for p in parity),performanceRows=len(performance),
        qualifiedModels=qualified,deploymentEligible=False,allPreviouslyInspected=True))
    render_report(output,params,prediction_rows,performance,diagnostic_rows,qualified)
    print('DONE',str(output),flush=True)

def render_report(output,params,predictions,performance,diagnostics,qualified):
    def number(x):return '—' if x is None else f'{x:.2f}'
    lines=['# 預測基準與成交診斷','',
        '本輪為已檢視歷史資料研究：8個完整日訓練、2日校驗、3日開發評估，沒有全新樣本外或實盤驗證。',
        '3秒判斷；15/30/60/120秒僅為預測標籤；1ms固定動作延遲、10秒實際出場冷卻、無最長持倉、固定/最大150點、每邊36JPY。same-side strict-cross，碰價不成交。',
        'Ridge與GBR共8模型，可供C#直接使用的JSON已保存；保持流量48–53停用，排除部位/帳本欄位。零預測是價格預測對照，不是交易損益。',
        '訓練：'+','.join(params['trainingDates'])+'；校驗：'+','.join(params['validationDates'])+'；開發：'+','.join(params['developmentDates'])+'。',
        '排除部分日/缺口日：'+','.join(sorted({r['date'] for r in params['excludedDays']}))+'。本輪不混用10/1獨立monotonic版本，未新增10/9原始行情回測。',
        '', '## 觀察事實：開發日預測', '',
        '|模型|日期|樣本|MSE跳²|零預測MSE跳²|MSE改善|R²|MAE跳|相關|≥2跳訊號|非零方向分母|方向命中%|訊號方向中間價變動JPY|',
        '|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    for r in predictions:
        if r['date'] in params['developmentDates']:
            lines.append('|'+ '|'.join(str(r[k]) if k in ('model','date','samples','signalCount','signalNonzeroActualCount') else number(r[k]) for k in
                ('model','date','samples','mseTicksSquared','zeroMseTicksSquared','mseImprovementTicksSquared','rSquared','maeTicks','correlation','signalCount','signalNonzeroActualCount','signalDirectionAccuracyPercent','signalMeanSignedMoveJPY'))+'|')
    lines+=['','回歸訓練使用平方損失，先以MSE對零預測及R²（對當日實際平均值）評估，MAE及相關為補充；不能只憑MAE淘汰平方損失模型。方向命中排除未來零變動樣本；訊號方向中間價變動是重疊的預測診斷，未扣成本、未考慮能否成交，不能稱每筆交易毛期望。',
        '', '## 觀察事實：逐日成交績效（額外0跳）','','|模型|日期|筆數|淨勝率%|毛JPY|淨JPY|平均淨獲利|平均淨損失|每筆毛期望|通過|淘汰原因|',
        '|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|']
    for r in performance:
        if r['extraCostTicksPerSide']==0:
            lines.append('|'+ '|'.join(str(r[k]) if k in ('model','date','trades','hardTargetPass','hardTargetReasons') else number(r[k]) for k in
                ('model','date','trades','winRatePercent','grossPnlJPY','netPnlJPY','averageWinJPY','averageLossJPY','expectedGrossPnlPerTradeJPY','hardTargetPass','hardTargetReasons'))+'|')
    lines+=['','## 觀察事實：成交品質（額外0跳）','','|模型|日期|進場意圖|進場成交|成交率%|平均等候秒|15秒成交後變動JPY|15秒取樣延後秒|',
        '|---|---|---:|---:|---:|---:|---:|---:|']
    for r in diagnostics:
        if r['cost']==0:
            values=[r['model'],r['date'],r['entryIntents'],r['entryFills'],number(r['entryFillRatePercent']),number(r['signalToFillSeconds']['mean']),
                number(r['markouts']['15']['sampledMidMoveJPY']['mean']),number(r['markouts']['15']['sampleDelaySeconds']['mean'])]
            lines.append('|'+ '|'.join(map(str,values))+'|')
    cohort_rows=[]
    lines+=['','## 成交與未成交的預測強度（額外0跳）','','|模型|日期|群組|有限分數筆數|缺失|平均預測跳|平均絕對預測跳|絕對預測p50|絕對預測p90|',
        '|---|---|---|---:|---:|---:|---:|---:|---:|']
    for r in diagnostics:
        for cohort,scores in r['predictionScoresTicks'].items():
            cohort_rows.append(dict(model=r['model'],date=r['date'],cost=r['cost'],cohort=cohort,
                **{k:v for k,v in scores.items() if k!='absolute'},**{'absolute'+k.title():v for k,v in scores['absolute'].items()}))
            if r['cost']==0:
                lines.append('|'+ '|'.join(map(str,[r['model'],r['date'],cohort,scores['count'],scores['missing'],number(scores['mean']),
                    number(scores['absolute']['mean']),number(scores['absolute']['p50']),number(scores['absolute']['p90'])]))+'|')
    write_csv(output/'fill-prediction-cohorts.csv',cohort_rows)
    lines+=['','原RL為離散動作actor，沒有與回歸模型可比的預測跳數，分數以缺失列出，不能補零。成交/未成交強度是觀察分組，不是可識別的因果成交選擇估計。等待時間及成交後markout僅納入完成交易，排除期末尚未平倉交易。',
        '', '## 分析推論、邏輯修改與參數建議','',
        '本輪合格模型：'+(','.join(qualified) if qualified else '無')+'。只有同一模型在三個指定開發日全部滿足≥50筆、≥40%淨勝率、≥50JPY毛期望才列入。',
        '預測平方誤差改善與成交績效應分開判斷；若MSE相對零預測改善而成交後變動或淨期望不佳，優先研究成交選擇及持倉管理。若MSE/R²及相關性不穩定，先檢查資料表示及行情條件，不擴大RL調參。',
        '2跳訊號門檻事前固定，本輪未搜尋最佳門檻；不能用已檢視最高值宣稱穩健參數。進出場沿用同一帳本，neutral產生平倉訊號、同向續抱、反向先平倉。',
        '一口來回佣金72JPY；額外每邊0/1/2跳合計成本為72/172/272JPY。每筆毛期望50JPY本身不足以達成淨期望正值，進階研究必須越過對應成本門檻。',
        '', '## 仍需驗證','',
        '成交後markout為下一個3秒網格的有效中間價相對實際進場價，實際取樣比目標延後0–3秒；不是成交時刻委託簿、可執行平倉價、MFE/MAE、成交機率或獲利證明。另列成交/未成交意圖的訊號時刻中間價變動，兩群分母皆揭露，不能將未成交視為虧損或零收益。',
        '缺口/期末開放持倉的浮盈浮虧及進場費對稱排除，完成虧損保留。未完成進場不加入完成交易markout。',
        '模型推論耗時只計C#模型函式（首個呼叫除外），不含行情處理、特徵、IPC、排程或券商網路；1ms不是全流程保證。',
        params['dependencyWarning'],
        '', '[逐日績效三成本CSV](daily-performance.csv) · [逐日預測CSV](prediction-performance.csv) · [成交分組CSV](fill-prediction-cohorts.csv) · [成交診斷JSON](fill-diagnosis.json) · [參數](parameters.json) · [驗證](verification.json)']
    (output/'analysis-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    import json
    (output/'analysis-report.json').write_text(json.dumps(dict(parameters=params,prediction=predictions,performance=performance,
        diagnostics=diagnostics,qualifiedModels=qualified,deploymentEligible=False),ensure_ascii=False,indent=2,allow_nan=False),encoding='utf-8')

if __name__=='__main__':main()
