"""Frozen historical predictive screen. Does not train RL or simulate trades."""
import argparse,json,sys,math,hashlib
from pathlib import Path
import numpy as np
from factor_significance import fit_univariate,predict_univariate,block_evidence,by_adjust,deduplicate,spearman
from collect_factor_panel import collect,calibrate,CALCULABLE
from observation_schema import feature_names
from prediction_fill_benchmark import label_samples,file_sha,verify_pin,write_csv

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',required=True);parser.add_argument('--bridge',required=True);parser.add_argument('--base',required=True)
    args=parser.parse_args();base=Path(args.base);out=Path(args.output)
    if out.exists():raise ValueError('Use new immutable root')
    out.mkdir(parents=True);read=lambda p:json.loads(Path(p).read_text(encoding='utf-8-sig'))
    previous=base/'nikkei-prediction-fill-benchmark-v2-20261011';verify_pin(read(previous/'frozen-inputs.json'))
    dataset=read(previous/'dataset.json');parameters=read(previous/'parameters.json');training=parameters['trainingDates'];evaluations=parameters['validationDates']+parameters['developmentDates']
    library=Path('KGIOptionPricing/Strategies/NikkeiRlDirection/factor-library/research-library.json').resolve();catalog=read(library)
    candidates={r['candidateId']:r for r in catalog['candidates']};sources={};pin={};source_pins={}
    def freeze(p):pin[str(Path(p).resolve())]=file_sha(p)
    for name in ('dataset.json','parameters.json','frozen-inputs.json'):freeze(previous/name)
    freeze(library)
    for p in Path(__file__).parent.glob('*.py'):freeze(p)
    for p in Path(args.bridge).parent.iterdir():
        if p.suffix in ('.dll','.json'):freeze(p)
    for p in (Path(__file__).parent/'bridge-source').glob('*'):freeze(p)
    for episode,path in dataset.items():
        cache=Path(path);manifest_path=Path(path+'.manifest.json');m=read(manifest_path);freeze(cache);freeze(manifest_path);freeze(cache.parent/'segment-index.json')
        if not read(cache.parent/'segment-index.json')['fullDayCoverage']:raise ValueError('Partial date in screen')
        src=Path(m['inputPath'])
        if src.exists():sha=m['inputSha256']
        else:
            provenance=base/'nikkei-rl-pruned-evaluation-cache-20261009'/episode[:8]/'extraction-summary.json';freeze(provenance);proof=read(provenance)
            if proof['sha256']!=m['inputSha256']:raise ValueError('Extraction provenance differs')
            src=Path(proof['source']);sha=proof['sourceSha256']
        if not src.exists():raise ValueError('Missing raw source '+str(src))
        sources[episode[:8]]=dict(cache=path,source=str(src),sha256=sha);source_pins[str(src)]=sha
    config=dict(trainingDates=training,evaluationDates=evaluations,allPreviouslyInspected=True,horizonsSeconds=[15,30,60,120],models=['linear','bins'],
        quantileBins=5,blockSeconds=600,fdrMethod='Benjamini-Yekutieli',qThreshold=.05,minPositiveDates=4,minDailySamples=1000,absTrainSpearmanDedup=.8,
        maximumSelected=10,minimumRequested=5,calculatedCandidateIds=CALCULABLE,midReturnTickJPY=50,baseline='Same-factor complete-case training mean',blockWeighting='Daily observation-count weighted block means; days equal weight; cluster sandwich variance',
        scope='PredictiveResearchNotTradingProfit',deploymentEligible=False,uncertainty='Approximate within-day time-block t; only5 inspected evaluation dates. Day-level sensitivity reported. Not finite-sample guarantee.',
        engineering='5-level books; receipt-time direction estimate;N200 trades;120s price moments;minute55/56/57/59/60=5closed,58=20closed,61=120closed with100valid;53=adjacent3s snapshots;39=lag3s past mini15s ticks withtrain-only beta;4=10state6step3s price-change Markov')
    def save(name,value):(out/name).write_text(json.dumps(value,ensure_ascii=False,indent=2,allow_nan=False),encoding='utf-8')
    save('parameters.json',config);freeze(out/'parameters.json');save('frozen-inputs.json',pin);save('source-inputs.json',source_pins);verify_pin(pin)
    inventory=[]
    for r in catalog['candidates']+catalog['conditionalCandidates']:
        cid=r['candidateId'];status='CalculatedResearchVersion' if cid in CALCULABLE else 'UnavailableRequiredFields' if cid in [13,14,18,19,20,26,27,28,37,38,49,50,'條件1','條件2'] else 'RequiresSeparateCausalEstimator'
        inventory.append(dict(candidateId=cid,name=r['name'],status=status,priorStatus=r['implementationStatus'],
            sourceFormula=r['sourceDefinition'].get('定義／公式（文字）',r['sourceDefinition'].get('審核後研究定義／公式','')),
            reason='' if cid in CALCULABLE else '缺逐筆委託/撤單/可靠主動事件或現貨/跨期/成交print口徑；不補零' if status=='UnavailableRequiredFields' else '未有相符已校準的因果自激/佇列/存活/季節性/衝擊估計器；直接用未來結果會洩漏'))
    write_csv(out/'factor-feasibility.csv',inventory);save('factor-feasibility.json',inventory)
    data={}
    for d in training+evaluations:
        s=sources[d];data[d]=collect(args.bridge,s['cache'],s['source'],s['sha256']);save('progress.json',dict(stage='SourceExtraction',finishedDates=list(data),totalDates=13))
        save('source-verification.json',{date:p['summary'] for date,p in data.items()});print('SOURCE',d,'frames',len(data[d]['rows']),'BBO',data[d]['summary']['maximumBookError'],'SHA PASS',flush=True)
    save('calibration.json',calibrate(data,training))
    names=feature_names(60,'mini-micro-research');market_columns=list(range(30))+list(range(40,48))+list(range(54,60))
    metadata=[dict(key='factor'+str(cid),family='library'+str(cid),candidateId=cid,name=candidates[cid]['name'],origin='CatalogResearchVersion') for cid in CALCULABLE]
    family_map={40:2,41:4,42:5,43:9,44:10,45:11,46:12,47:7,54:29,55:30,56:32,57:33,58:31,59:35,4:1,5:3}
    for i in market_columns:
        family='library'+str(family_map[i]) if i in family_map else 'library29' if i<4 else 'market'+str(i)
        metadata.append(dict(key='market'+str(i),family=family,candidateId=None,name=names[i],origin='ExistingMarketField'))
    metadata.append(dict(key='hoursToClose',family='clock',candidateId=None,name='hoursToSessionClose',origin='KnownSessionClock'))
    for p in data.values():
        matrix=np.column_stack([p['values'],p['base'][:,:30],p['extras'][:,:8],p['extras'][:,14:20],[(14.75*3600-(r['t']+8*3600)%86400)/3600 for r in p['rows']]])
        if matrix.shape[1]!=len(metadata):raise ValueError('Factor ordering mismatch')
        p['matrix']=matrix
    save('tested-universe.json',metadata)
    training_values={m['key']:np.concatenate([data[d]['matrix'][:,i] for d in training]) for i,m in enumerate(metadata)}
    ranking=[];daily=[];saved_models={}
    for h in config['horizonsSeconds']:
        labels={d:label_samples(p['rows'],h)[1:] for d,p in data.items()}
        train_y=np.concatenate([labels[d][0] for d in training]);train_ix={d:labels[d][1] for d in training}
        for i,m in enumerate(metadata):
            x=np.concatenate([data[d]['matrix'][train_ix[d],i] for d in training]);valid=np.isfinite(x);xx=x[valid];yy=train_y[valid]
            for kind in config['models']:
                model=fit_univariate(xx,yy,kind);model_key=m['key']+'-'+str(h)+'-'+kind;rows=[];evidence_rows=[]
                for d in evaluations:
                    y,ix=labels[d];column=data[d]['matrix'][ix,i];keep=np.isfinite(column);actual=y[keep];times=np.array([data[d]['rows'][j]['t'] for j in ix[keep]])
                    if model is None or not len(actual):
                        daily.append(dict(**m,horizonSeconds=h,model=kind,date=d,samples=int(keep.sum()),coveragePercent=100*keep.mean() if len(keep) else 0,maeTicks=None,mseTicksSquared=None,constantMseTicksSquared=None,mseImprovementTicksSquared=None,rankIC=None));continue
                    pred=predict_univariate(model,column[keep]);baseline=float(yy.mean());loss=(actual-baseline)**2;delta=loss-(actual-pred)**2
                    row=dict(**m,horizonSeconds=h,model=kind,date=d,samples=len(actual),coveragePercent=100*keep.mean(),maeTicks=float(abs(actual-pred).mean()),mseTicksSquared=float(np.mean((actual-pred)**2)),constantMseTicksSquared=float(loss.mean()),mseImprovementTicksSquared=float(delta.mean()),rankIC=spearman(column[keep],actual))
                    rows.append(row);daily.append(row);evidence_rows.append(dict(times=times,delta=delta))
                evidence=block_evidence(evidence_rows) if len(rows)==5 else dict(pValue=1.,meanImprovement=None,lower95=None,blocksPerDay=[],dayPValue=None)
                if evidence['meanImprovement'] is not None and not np.isclose(evidence['meanImprovement'],np.mean([r['mseImprovementTicksSquared'] for r in rows]),rtol=1e-10,atol=1e-10):raise ValueError('Block statistic differs from daily equal-weight MSE')
                mean_zero=float(np.mean([r['constantMseTicksSquared'] for r in rows])) if rows else None
                result=dict(**m,horizonSeconds=h,model=kind,trainingSamples=len(yy),**evidence,
                    positiveDates=sum(r['mseImprovementTicksSquared']>0 for r in rows),minDailySamples=min([r['samples'] for r in rows],default=0),
                    minDailyCoveragePercent=min([r['coveragePercent'] for r in rows],default=0),
                    mseImprovementPercent=100*evidence['meanImprovement']/mean_zero if mean_zero and evidence['meanImprovement'] is not None else None,
                    modelValid=model is not None,selected=False,modelKey=model_key)
                ranking.append(result)
                if model is not None:saved_models[model_key]=model
        print('SCREEN',h,'hypotheses',len(ranking),flush=True)
    q=by_adjust([r['pValue'] for r in ranking])
    for r,adjusted in zip(ranking,q):
        r['qValue']=float(adjusted);reasons=[]
        if not r['modelValid']:reasons.append('ConstantOrMissingTrain')
        if adjusted>.05:reasons.append('BYQAbove0.05')
        if r['lower95'] is None or r['lower95']<=0:reasons.append('NoPositiveBlockLowerBound')
        if r['positiveDates']<4:reasons.append('PositiveDatesBelow4of5')
        if r['minDailySamples']<1000:reasons.append('DailySamplesBelow1000')
        r['significantStable']=not reasons;r['reasons']=';'.join(reasons)
    eligible=sorted([r for r in ranking if r['significantStable']],key=lambda r:(-r['mseImprovementPercent'],r['qValue'],r['key'],r['horizonSeconds'],r['model']))
    selected=deduplicate(eligible,training_values,10)
    for r in selected:r['selected']=True
    ranking.sort(key=lambda r:(not r['significantStable'],-(r['mseImprovementPercent'] if r['mseImprovementPercent'] is not None else -1e12),r['key'],r['horizonSeconds'],r['model']))
    write_csv(out/'factor-ranking.csv',ranking);save('factor-ranking.json',ranking);write_csv(out/'daily-prediction-performance.csv',daily);save('selected-factors.json',selected)
    save('selected-models.json',{r['modelKey']:saved_models[r['modelKey']] for r in selected})
    verify_pin(pin);verify_pin(source_pins)
    save('verification.json',dict(done=True,dates=13,calculatedFamilies=len(CALCULABLE),universeVersions=len(metadata),hypotheses=len(ranking),dailyRows=len(daily),
        selected=len(selected),minimumFiveReached=len(selected)>=5,fingerprintsUnchanged=True,rawSourceAndNormalizedIdentityVerified=True,temporaryDetailedFilesCreated=False,
        tradingBacktest=False,allPreviouslyInspected=True,deploymentEligible=False))
    render(out,config,ranking,selected,inventory)
    print('DONE',json.dumps(dict(hypotheses=len(ranking),selected=len(selected),names=[r['name'] for r in selected]),ensure_ascii=False),flush=True)

def render(out,config,ranking,selected,inventory):
    lines=['# 日經微型全部因子預測篩選','',f'逐項審查61項及2條件候選；實測{len(CALCULABLE)}項可算工程版本及既有市場欄位，共{len(ranking)}個因子版本×期限×模型假說。選出{len(selected)}項；'+('不足5項，不以未顯著者補齊。' if len(selected)<5 else '達成5–10項歷史研究篩選數量。'),
        '', '8日訓練：'+','.join(config['trainingDates'])+'。5日評分：'+','.join(config['evaluationDates'])+'。全部已檢視歷史資料，沒有新樣本外或交易獲利驗證。',
        '', '## 觀察事實', '', '|因子|期限秒|模型|MSE改善%|BY q|改善日數|最低每日樣本|最低涵蓋%|日級p敏感度|', '|---|---:|---|---:|---:|---:|---:|---:|---:|']
    for r in selected:
        lines.append(f"|{r['name']}|{r['horizonSeconds']}|{r['model']}|{r['mseImprovementPercent']:.4f}|{r['qValue']:.6g}|{r['positiveDates']}/5|{r['minDailySamples']}|{r['minDailyCoveragePercent']:.2f}|{r['dayPValue']:.6g}|")
    lines+=['', '因子只用決策前資料。15/30/60/120秒為未來中間價跳數；單因子線性與訓練五分位條件均值，對照同因子完整案例的訓練常數均值。所有假說共同BY修正，門檻q≤.05、95%單側區塊下界>0、至少4/5日改善、每日至少1000完整標籤；訓練abs Spearman≥.80去重，最多每家族一項。',
        '', '## 分析推論與限制', '', '600秒實際時間區塊避免壓縮缺失或缺口，長於最大120秒重疊標籤；日內區塊按有效案例數加權，5日等權，與逐日MSE改善的估計目標一致；使用cluster sandwich變異與近似t檢定。區塊間仍可能有依賴，且僅5個評分日；BY只有在p值有效條件下才控制FDR。本輪不是有限樣本顯著性保證，日級p敏感度另列，後續必須凍結並以新完整日期重驗。',
        '已停用因子僅在研究副本中重新評分，沒有恢復actor輸入。來源缺逐筆委託/撤單/可靠aggressor時不可精確計算相關原式；16/17/23/24方向為因果報價/tick-rule估計，N200成交為feed print口徑。4為3秒取樣的六次價格變動Markov工程移植，39是mini past15s ticks lag3s train-only係數；不是原論文的同步時鐘證明。',
        '61為3秒固定網格已接收末成交價的局部simple return，以區間真成交量加權成完整分鐘VWAR，再於120個已完成分鐘至少100有效分鐘取type7 5%分位數以下均值；零量分鐘剔除，不保證因子值負，不是已校準VaR。53是3秒相鄰五檔快照的共同價位淨減量；不是撤單。',
        '', '## 邏輯與參數建議', '', '保留已通過因子作下一輪少量特徵模型，維持資料品質與固定交易契約，再由同一C#帳本驗證成交後與扣費結果。不可把預測MSE改善當每筆毛期望，或為湊5–10項放寬顯著性門檻。',
        '', '## 無法評分項目', '', '|ID|因子|狀態|原因|', '|---|---|---|---|']
    for r in inventory:
        if r['status']!='CalculatedResearchVersion':lines.append(f"|{r['candidateId']}|{r['name']}|{r['status']}|{r['reason']}|")
    lines+=['', '方法依據：[Statsmodels BY-FDR](https://www.statsmodels.org/stable/generated/statsmodels.stats.multitest.fdrcorrection.html)；統計程式在專案內手算測試，沒有安裝新套件。既有SciPy1.9.1對NumPy1.26.4有版本範圍警告，保留揭露。',
        '', '[全部排名CSV](factor-ranking.csv) · [逐日預測CSV](daily-prediction-performance.csv) · [可算性CSV](factor-feasibility.csv) · [參數](parameters.json) · [驗證](verification.json)']
    (out/'analysis-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    (out/'analysis-report.json').write_text(json.dumps(dict(parameters=config,ranking=ranking,selected=selected,inventory=inventory),ensure_ascii=False,indent=2,allow_nan=False),encoding='utf-8')

if __name__=='__main__':main()
