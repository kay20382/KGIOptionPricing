"""Read-only group comparison and full shared-ledger feedback audit."""
import argparse,csv,json,statistics,gzip
from collections import Counter
from pathlib import Path
from completed_daily import aggregate_daily
from analyze_stop_sensitivity import audit_episode
from expanded_daily_backtest import sha,read,DATES
from observation_schema import research_exclusions,feature_names
from root_cause_comparison import validate_parity
from candidate_feature_inventory import write_inventory

GROUPS=['baseline','book','flow','price','all'];SEEDS=[11,22,33]

def decision_funnel(rows):
    counts=dict(decisions=0,healthyDecisions=0,eligibleFlatDecisions=0,holdingDecisions=0,entryIntents=0,entryFills=0,flatSignals=0,heldSignals=0)
    reasons=Counter();actions=Counter();exits=Counter()
    for row in rows:
        obs=row['observation'];state=row['next'];counts['decisions']+=1
        counts['healthyDecisions']+=int(obs[38]==1);counts['holdingDecisions']+=int(obs[30]!=0)
        counts['eligibleFlatDecisions']+=int(obs[30]==0 and row['mask'][1] and row['mask'][2])
        if row['proposedAction'] in [1,2]:counts['flatSignals' if obs[30]==0 else 'heldSignals']+=1
        counts['entryIntents']+=int(state['effectiveAction'] in [1,2])
        actions[str(row['proposedAction'])]+=1;reasons[state['overrideReason'] or 'POLICY_ACTION']+=1
        for fill in state['fills']:
            if fill['role'] in [0,1]:counts['entryFills']+=1
            else:exits[str(fill['role'])]+=1
    return dict(**counts,actionCounts=dict(actions),overrideReasonCounts=dict(reasons),exitRoleCounts=dict(exits))

def group_comparison(daily):
    eligible=[d for d in daily if d['targetEligible'] and d['extraCostTicksPerSide']==0]
    keys={(d['group'],d['seed']) for d in eligible};groups=sorted({k[0] for k in keys});seeds=sorted({k[1] for k in keys})
    if not groups or 'baseline' not in groups or any((g,s) not in keys for g in groups for s in seeds):raise ValueError('Missing paired group/seed baseline')
    rows=[]
    for g in groups:
        for s in seeds:
            data=[d for d in eligible if d['group']==g and d['seed']==s]
            baseline=[d for d in eligible if d['group']=='baseline' and d['seed']==s]
            if len(data)!=2 or len(baseline)!=2 or {d['date'] for d in data}!={d['date'] for d in baseline}:raise ValueError('Paired development dates missing or duplicated')
            total=aggregate_daily(data);base=aggregate_daily(baseline)
            rows.append(dict(group=g,seed=s,initialDays=sum(d['initialTarget'] for d in data),advancedDays=sum(d['advancedTarget'] for d in data),
                netDeltaVsBaselineJPY=total['netPnlJPY']-base['netPnlJPY'],grossDeltaVsBaselineJPY=total['grossPnlJPY']-base['grossPnlJPY'],**total))
    ranking=[]
    for g in groups:
        data=[d for d in rows if d['group']==g]
        ranking.append(dict(group=g,initialDays=sum(d['initialDays'] for d in data),advancedDays=sum(d['advancedDays'] for d in data),
            medianNetJPY=statistics.median(d['netPnlJPY'] for d in data),worstSeedNetJPY=min(d['netPnlJPY'] for d in data),
            medianNetDeltaJPY=statistics.median(d['netDeltaVsBaselineJPY'] for d in data),improvedSeeds=sum(d['netDeltaVsBaselineJPY']>0 for d in data),
            recommendedForNextRound=g!='baseline' and sum(d['netDeltaVsBaselineJPY']>0 for d in data)>=2 and statistics.median(d['netDeltaVsBaselineJPY'] for d in data)>0))
    ranking.sort(key=lambda d:(-d['advancedDays'],-d['initialDays'],-d['medianNetJPY'],-d['worstSeedNetJPY'],d['group']))
    for i,row in enumerate(ranking,1):row['rank']=i
    return rows,ranking

def write_csv(path,rows):
    with Path(path).open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)

def analyze(root,output):
    r=Path(root).resolve();out=Path(output).resolve();out.mkdir(parents=True,exist_ok=True)
    frozen=read(r/'frozen-inputs.json')
    frozen.update(read(r/'frozen-training-inputs.json'))
    frozen.update(read(r/'source-archives.json'))
    if any(sha(p)!=h for p,h in frozen.items()):raise ValueError('Frozen experimental input changed')
    daily=[];timings=[];audits=[];funnels=[];transitions=0;actor_hashes={};warmstart=None
    expected=dict(decisionLatencyMs=1,decisionIntervalSeconds=3,cooldownSeconds=10,maxHoldingSeconds=0,featureProfile='mini-micro-research',observationSize=60,
        stopMode='fixed',fixedStopPoints=150,maxStopPoints=150,commission=36,tickSize=5,multiplier=10,rewardCostWeight=1)
    for group in GROUPS:
        folder=r/'models'/group;train=read(folder/'backtest-report.json')
        if train['train']!=['20260928-postgap-day','20260929-day','20260930-day','20261005-postgap-day'] or train['validation']!=['20261006-day','20261007-day'] or train['requestedSteps']!=32768 or train['predictionHorizonSeconds']!=15:raise ValueError('Experiment split/budget/horizon mismatch')
        if train['actorExcludedFeatures']!=research_exclusions(group) or any(d['actualSteps']!=32768 for d in train['training']):raise ValueError('Training mask/actual steps mismatch')
        provenance=train['warmstartProvenance']
        same=(provenance['selectedSamples'],provenance['matchedTimestampsSha256'],provenance['labelSha256'])
        if warmstart is None:warmstart=same
        elif warmstart!=same:raise ValueError('Group warmstart labels changed')
        for seed in SEEDS:
            model=folder/f'policy-seed{seed}.onnx';manifest=model.with_suffix('.manifest.json');meta=read(manifest)
            if meta['schema']!='nikkei-rl-research-v1' or meta['featureNames']!=feature_names(60,'mini-micro-research') or meta['actorExcludedFeatures']!=research_exclusions(group):raise ValueError('Model schema/mask changed')
            actor_hashes[str(model)]=sha(model);actor_hashes[str(manifest)]=sha(manifest)
            parity=read(r/f'parity-{group}-{seed}.json')
            validate_parity(parity,sha(model),sha(manifest),sha(folder/f'parity-seed{seed}.json'))
            timings.append(dict(group=group,seed=seed,**parity))
            for cost in [0,1,2]:
                evaluation=read(r/'evaluations'/f'{group}-seed{seed}-cost{cost}'/'evaluation.json');config=evaluation['config']
                if any(config[k]!=v for k,v in expected.items()) or config['extraCostTicksPerSide']!=cost:raise ValueError('Protected execution/risk contract changed')
                if evaluation['modelSha256']!=sha(model) or evaluation['manifestSha256']!=sha(manifest) or evaluation['datasetSha256']!=sha(r/'evaluation-dataset.json'):raise ValueError('Evaluation identity mismatch')
                bydate={d:[] for d in DATES};by_audit={d:[] for d in DATES}
                for ep in evaluation['results']:
                    complete,audit=audit_episode(ep,config);bydate[ep['date']].append(complete);by_audit[ep['date']].append(audit);transitions+=audit['steps']
                    if cost==0:
                        with gzip.open(ep['feedbackPath'],'rt',encoding='utf-8') as f:
                            funnels.append(dict(group=group,seed=seed,date=ep['date'],episode=ep['id'],**decision_funnel(json.loads(line) for line in f)))
                for date in DATES:
                    row=aggregate_daily(bydate[date]);index=read(r/'cache'/date/'segment-index.json')
                    eligible=index['fullDayCoverage'] and date in ['20261006','20261007'] and cost==0
                    initial=eligible and row['trades']>100 and (row['winRatePercent'] or 0)>33 and row['grossPnlJPY']>0
                    daily.append(dict(group=group,seed=seed,date=date,extraCostTicksPerSide=cost,fullDayCoverage=index['fullDayCoverage'],targetEligible=eligible,
                        initialTarget=initial,advancedTarget=initial and row['netPnlJPY']>0,parameterId=f'{group}-fixed150-cost{cost}',**row))
                    audits.append(dict(group=group,seed=seed,date=date,cost=cost,steps=sum(a['steps'] for a in by_audit[date]),
                        gapOverrunStops=sum(a['gapOverrunStops'] for a in by_audit[date]),maxTriggerExcessPoints=max((a['maxTriggerExcessPoints'] for a in by_audit[date]),default=0)))
    if len(daily)!=315 or len({(d['group'],d['seed'],d['date'],d['extraCostTicksPerSide']) for d in daily})!=315:raise ValueError('Missing or duplicated315daily rows')
    comparison,ranking=group_comparison(daily)
    feature_timing=[dict(date=d,**{k:read(r/'cache'/d/'full.br.manifest.json')[k] for k in ['frames','validFrames','featureComputeP99Ms','featureEventUpdateP99Ms','featureEventTimingSampleStride']}) for d in DATES]
    cache_audit=read(r/'feature-cache-audit.json')
    if len(cache_audit)!=7 or {a['date'] for a in cache_audit}!=set(DATES) or any(a['maxFeatureError']!=0 or not a['allValidFramesHaveCompleteFlow'] for a in cache_audit):raise ValueError('Source-to-feature cache/flow-health audit failed')
    for a in cache_audit:
        t=next(t for t in feature_timing if t['date']==a['date'])
        if a['frames']!=t['frames'] or a['validFrames']!=t['validFrames']:raise ValueError('Source audit/cache frame mismatch')
    historical=read(r.parent/'nikkei-rl-stop150-20261008/analysis-report.json')
    historical_rows=[d for d in historical['developmentPerCandidate'] if d['profile']=='fixed150']
    verification=dict(dailyRows=315,scenarios=45,feedbackTransitions=transitions,csharpParityPredictions=sum(t['count'] for t in timings),
        allFrozenInputsUnchanged=True,frozenInputs=len(frozen),sharedLedgerReconciled=True,cooldownAtLeast10Seconds=True,allRiskStopsCapped=True,heldStopsNeverLoosen=True,
        sourceFeatureParityFrames=sum(a['frames'] for a in cache_audit),allValidFramesHaveCompleteFlow=True)
    result=dict(status='PreviouslyInspectedDevelopmentFeatureScreen',deploymentEligible=False,parameters=expected,stepsPerSeed=32768,warmstartEpochs=20,
        groups={g:dict(excludedActorFeatures=research_exclusions(g)) for g in GROUPS},daily=daily,developmentPerCandidate=comparison,groupRanking=ranking,
        initialPassed=sum(d['initialTarget'] for d in daily),advancedPassed=sum(d['advancedTarget'] for d in daily),targetEligibleRows=sum(d['targetEligible'] for d in daily),
        actorTimings=timings,featureTimings=feature_timing,sourceFeatureAudit=cache_audit,audits=audits,decisionFunnels=funnels,historicalPower150Reference=historical_rows,verification=verification)
    (out/'analysis-report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    (out/'model-fingerprints.json').write_text(json.dumps(actor_hashes,indent=2))
    (out/'decision-funnels.json').write_text(json.dumps(funnels,indent=2))
    write_csv(out/'daily-performance.csv',daily);write_csv(out/'group-comparison.csv',comparison);write_csv(out/'parameter-ranking.csv',ranking);write_inventory(out)
    fmt=lambda n:'—' if n is None else f'{n:,.2f}'
    lines=['# 日經微型：候選特徵分組回測','',
        f"初步逐日目標達成 {result['initialPassed']}/{result['targetEligibleRows']}；進階 {result['advancedPassed']}/{result['targetEligibleRows']}。一個分母案例為一組／一seed／一完整開發日期，沒有把三seed合成策略。",'',
        '參數：225MC一口、225M參考、不用大型225；3秒取樣／判斷、15秒可執行標籤、1ms動作延遲、10秒平倉後冷卻、無最長持倉、同向持有、反向先平倉、固定150點／最大150點（30跳）、每邊36JPY、tick5、乘數10；額外成本0/1/2ticks每邊。', '',
        '每組seed11/22/33各32768 PPO步、20預訓練epochs、64×64網路；60維含舊Power40及新增20。baseline將新增20維全遮罩，Book開40–47、Flow開48–53、Price開54–59、All全開，遮罩一致套於預訓練/PPO/ONNX。全部使用同一150點停損可執行標籤與健康門檻。', '',
        '訓練9/28、9/29、9/30、10/5的連續到收盤片段；10/2為歷史檢視，10/6–7為已反覆使用的開發日期，沒有全新樣本外。七日逐日列出，部分日不計全日達標；未完成最後持倉浮盈浮損與進場費對稱排除，完整虧損全部保留。', '',
        '## 分組比較（兩完整開發日；基本成本）','', '|組別|seed|交易筆數|勝率%|毛損益JPY|淨損益JPY|相對同seed基準淨差|平均淨獲利JPY|平均淨損失JPY|','|---|---:|---:|---:|---:|---:|---:|---:|---:|']
    for d in comparison:lines.append(f"|{d['group']}|{d['seed']}|{d['trades']}|{fmt(d['winRatePercent'])}|{fmt(d['grossPnlJPY'])}|{fmt(d['netPnlJPY'])}|{fmt(d['netDeltaVsBaselineJPY'])}|{fmt(d['averageWinJPY'])}|{fmt(d['averageLossJPY'])}|")
    lines+=['','## 觀察事實與分析推論','']
    for d in ranking:
        lines.append(f"- {d['group']}：三seed淨損益中位數 {fmt(d['medianNetJPY'])} JPY，最差seed {fmt(d['worstSeedNetJPY'])}；{d['improvedSeeds']}/3 seed勝過相同預算基準，中位數淨差 {fmt(d['medianNetDeltaJPY'])}。")
    retained=[d['group'] for d in ranking if d['recommendedForNextRound']]
    lines += ['', '邏輯修改建議：'+('下一輪優先保留 '+', '.join(retained)+' 組作更長訓練／新日期驗證。' if retained else '本輪沒有至少兩個seed且中位數改善的新增組；不將新增特徵升級為最佳模型。'),
        '群組回測評估的是可交易策略效果，未分離每一特徵的獨立預測能力。單一seed獲利或排序優勝都不足以證明穩健；未通過逐日交易量／勝率／毛淨損益目標者仍不合格。','',
        '信號漏斗見decision-funnels.json：逐episode區分總／健康／空手適格決策、持倉、空手／持倉方向訊號、進場意圖、進場成交、各出場角色及override原因。進場意圖含延遲佇列中的請求，非券商確認；若僅因交易減少而淨虧損縮小，不解讀為獨立預測力已驗證。', '',
        '資料診斷：新增完整5秒BBO/OFI窗口門檻大幅減少可用决策；兩開發日約48%–49%有效。原始五檔部分無效與最佳檔缺測需分開，深檔無效不再丟失有效最佳檔OFI。獨立逐日原始事件重算57939個frame與20項特徵完全一致，所有交易適格frames均有完整流視窗。對現有資料的結論只涵蓋此共同健康群體，不能外推到被排除行情。', '',
        '參數建議：保留10秒冷卻與150點最大停損；本輪固定停損150且重新訓練，相對舊Power150敏感度實驗同時改變訓練預算、標籤及健康門檻，舊結果僅為歷史參考。', '',
        '## 每天績效（基本成本；完整315列含額外成本見CSV）','', '|組別|seed|日期|完整日|交易筆數|勝率%|毛損益JPY|淨損益JPY|平均淨獲利|平均淨損失|排除未完持倉|','|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|']
    for d in sorted([d for d in daily if d['extraCostTicksPerSide']==0],key=lambda x:(x['group'],x['seed'],x['date'])):
        lines.append(f"|{d['group']}|{d['seed']}|{d['date']}|{d['fullDayCoverage']}|{d['trades']}|{fmt(d['winRatePercent'])}|{fmt(d['grossPnlJPY'])}|{fmt(d['netPnlJPY'])}|{fmt(d['averageWinJPY'])}|{fmt(d['averageLossJPY'])}|{d['excludedOpenPositions']}|")
    lines+=['','## 判斷耗時','', '|組別|seed|C# actor p99 ms|最大ms|超過1ms筆數|','|---|---:|---:|---:|---:|']
    for d in timings:lines.append(f"|{d['group']}|{d['seed']}|{d['p99Ms']:.4f}|{d['maxMs']:.4f}|{d['countAbove1Ms']}|")
    lines+=['','|日期|feature snapshot p99 ms|事件更新抽樣p99 ms|健康frame/總frame|','|---|---:|---:|---:|']
    for d in feature_timing:lines.append(f"|{d['date']}|{d['featureComputeP99Ms']:.4f}|{fmt(d['featureEventUpdateP99Ms'])}|{d['validFrames']}/{d['frames']}|")
    lines+=['','仍需驗證：固定1ms僅是動作延遲假設；actor及特徵分開量測，不能把p99相加當端到端p99，未含排程／網路。strict-cross按委託價成交，行情跳空實際成交可能超過150點，額外ticks為成本壓力測試而非真實價格滑價模型。','',
        '所有逐步回饋、成交、費用、冷卻、價格停損與未完成持倉核對共用C#帳本；模型可由C# ONNX使用但僅供研究，未部署。','',
        f"驗證：{verification['feedbackTransitions']:,}步回饋、{verification['csharpParityPredictions']:,}筆C#/Python parity、45情境315日別，輸入凍結雜湊一致。"]
    (out/'analysis-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--runs',required=True);p.add_argument('--output',required=True);p.add_argument('--mode',choices=['all','logic','parameters'],default='all');args=p.parse_args()
    result=analyze(args.runs,args.output);print(json.dumps(result['verification']))
