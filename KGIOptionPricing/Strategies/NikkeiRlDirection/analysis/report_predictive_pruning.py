"""Strict accounting/provenance gates for a train-only feature-pruning experiment."""
import argparse
import csv
import gzip
import hashlib
import json
import math
from pathlib import Path
import numpy as np
from feature_experiments import TRAIN,COHORT,SEEDS,combination_eligible
from feedback import verify_manifest
from fill_diagnostics import reconcile_episode,validate_daily_keys
from observation_schema import feature_names
from performance_metrics import episode_metrics,completed_trade_counts,aggregate_round_trips,daily_targets

def sha(path):
    digest=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):digest.update(block)
    return digest.hexdigest().upper()

def validate_benchmark_runtime(evidence,actual_sha):
    expected=[(v,s) for v in ['C0','R','N','N5'] for s in SEEDS]
    if evidence['runtimeSha256']!=actual_sha or [(r['variant'],r['seed']) for r in evidence['models']]!=expected or evidence['phase']!='After all training and frozen sensitivity completed':raise ValueError('Benchmark runtime/phase/model set differs')

def bootstrap_method_description(selection):
    return f"採{selection['blockSamples']}個保留樣本的circular block，{selection['bootstrapReplicates']}次bootstrap；篩後樣本有間隔，時間跨度不固定，不是30秒時間區塊。"

def validate_run_report(report,variant,selection):
    config=dict(cooldownSeconds=10,decisionLatencyMs=1,rewardCostWeight=0,observationSize=56)
    if report['environmentConfig']!=config or report['actorExcludedFeatures']!=selection['masks']['N' if variant=='N5' else variant]:raise ValueError('Changed configuration/mask')
    if variant=='N5' and report.get('directionLabel')!=dict(horizonSeconds=5,exclusiveThresholdTicks=0):raise ValueError('Wrong5s label contract')
    if report['train']!=TRAIN or report['validation']!=COHORT or report['requestedSteps']!=102400 or report['directionWarmstartEpochs']!=20 or report['featureNames']!=feature_names(56):raise ValueError('Changed cohort/training contract')
    if [(r['seed'],r['actualSteps']) for r in report['training']]!=[(s,102400) for s in SEEDS]:raise ValueError('Incomplete training')

def update_funnel(funnel,row):
    end=row['next']
    for key,amount in [('entrySignals',int(row['proposedAction'] in [1,2])),('effectiveEntries',int(end['effectiveAction'] in [1,2])),
        ('entryFills',sum(f['role'] in [0,1] for f in end['fills'])),('pendingEntrySteps',int(end['pendingEntry']))]:
        funnel[key]=funnel.get(key,0)+amount
    reasons=funnel.setdefault('overrideReasons',{});reason=end['overrideReason']
    if reason:reasons[reason]=reasons.get(reason,0)+1

def audit_scenario(directory,scenario,variant,phase='PrimaryOrCost'):
    seed=scenario['seed'];delay=scenario['latencyMs'];cost=scenario['extraCostTicksPerSide'];totals={}
    if [e['episode'] for e in scenario['episodes']]!=COHORT:raise ValueError('Wrong daily cohort')
    with gzip.open(directory/f'feedback-seed{seed}-delay{delay}-cost{cost}.jsonl.gz','rt',encoding='utf-8') as stream:
        for line in stream:
            row=json.loads(line);end=row['next'];t=totals.setdefault(row['episode'],dict(delta=0,reward=0,transitions=0,fills=[],last=None,funnel={}))
            update_funnel(t['funnel'],row)
            if len(row['observation'])!=56 or len(row['mask'])!=5:raise ValueError('Feedback observation/mask contract differs')
            if row['stepId']!=t['transitions']:raise ValueError('Feedback step mismatch')
            t['delta']+=end['deltaEquity'];t['reward']+=end['reward'];t['transitions']+=1;t['fills'].extend(end['fills']);t['last']=end
    if sorted(totals)!=COHORT:raise ValueError('Missing feedback dates')
    rows=[];audits=[];exits=[]
    for e in scenario['episodes']:
        t=totals[e['episode']];end=t.pop('last');fills=t.pop('fills')
        if not math.isclose(t['delta'],end['netPnl'],abs_tol=1e-6) or not math.isclose(t['reward'],end['totalReward'],abs_tol=1e-6):raise ValueError('Feedback equity/reward mismatch')
        outcomes=reconcile_episode(e,end,fills);metric=episode_metrics(e,10);metric.update(completed_trade_counts(metric))
        if metric['costsJPY']!=metric['completedTrades']*(72+100*cost):raise ValueError('Cost sensitivity fee mismatch')
        row=dict(variant=variant,seed=seed,episode=e['episode'],latencyMs=delay,extraCostTicksPerSide=cost,**metric)
        row.update(daily_targets(row));rows.append(row)
        audits.append(dict(phase=phase,variant=variant,seed=seed,episode=e['episode'],latencyMs=delay,extraCostTicksPerSide=cost,fillTrades=len(outcomes),**t))
        for role in ['Flatten','StopLoss','TakeProfit']:
            group=[x for x in outcomes if x['exitRole']==role]
            if group:exits.append(dict(variant=variant,seed=seed,episode=e['episode'],latencyMs=delay,extraCostTicksPerSide=cost,role=role,trades=len(group),grossPnlJPY=sum(x['grossPnlJPY'] for x in group),netPnlJPY=sum(x['netPnlJPY'] for x in group)))
    return rows,audits,exits

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);args=parser.parse_args();root=Path(args.root)
    selection=json.loads((root/'screening/selection.json').read_text())
    validate_benchmark_runtime(json.loads((root/'benchmark-provenance.json').read_text()),sha(root/'bridge/NikkeiRlReplayBridge.dll'))
    if selection['train']!=TRAIN or selection['validationExcludedFromScreening']!=COHORT or selection['featureNames']!=feature_names(56):raise ValueError('Selection leakage/schema')
    kept=selection['retainedMarketIndices'];market=list(range(30))+list(range(40,56))
    expected={'C0':list(range(40,56)),'R':sorted(set(market)-set(kept)|set(range(48,56))),'N':sorted(set(market)-set(kept))}
    if selection['masks']!=expected or selection['controlIndicesProtected']!=list(range(30,40)):raise ValueError('Selection masks altered')
    for path,digest in json.loads((root/'provenance.json').read_text(encoding='utf-8-sig')).items():
        if sha(path)!=digest:raise ValueError('Source/runtime/selection changed: '+path)
    caches={};raw_checked={}
    for episode,path in json.loads((root/'dataset.json').read_text()).items():
        manifest=json.loads(Path(path+'.manifest.json').read_text())
        if manifest['schema']!='nikkei-rl-cache-v4' or not manifest['legacyParityVerified'] or sha(path)!=manifest['cacheSha256'] or any(manifest[k] for k in ['badRows','outOfOrder','dropped','dataGapFrames']):raise ValueError('Cache provenance failed')
        raw=manifest['inputPath']
        if raw not in raw_checked:raw_checked[raw]=sha(raw)
        if raw_checked[raw]!=manifest['inputSha256']:raise ValueError('Raw changed')
        if episode in TRAIN and selection['provenance']['cacheSha256'][episode]!=manifest['cacheSha256']:raise ValueError('Selection used different cache')
        caches[episode]=manifest
    expected['N5']=expected['N']
    variants=['C0','R','N','N5']
    daily=[];pressure=[];audits=[];exits=[];models=[];raw_inputs=None
    for variant in variants:
        directory=root/'models'/variant;report=json.loads((directory/'backtest-report.json').read_text());validate_run_report(report,variant,selection)
        sample=np.load(directory/'direction-training.npz')
        values=(sample['observation'],sample['action'])
        if variant!='N5':
            if raw_inputs is None:raw_inputs=values
            elif any(not np.array_equal(a,b) for a,b in zip(raw_inputs,values)):raise ValueError('Training inputs/labels differ')
        else:
            from predictive_pruning import build_samples
            xs=[];ys=[]
            for episode in TRAIN:
                with gzip.open(root/'screening'/f'{episode}-states.json.gz','rt',encoding='utf-8') as stream:states=json.load(stream)
                x,y,_=build_samples(states,5,0);xs.append(x);ys.append(y)
            if not np.array_equal(values[0],np.concatenate(xs)) or not np.array_equal(values[1],np.concatenate(ys)):raise ValueError('5s training differs from diagnostic samples')
        if sorted((s['seed'],s['latencyMs'],s['extraCostTicksPerSide']) for s in report['scenarios'])!=[(seed,1,cost) for seed in SEEDS for cost in [0,1,2]]:raise ValueError('Missing pressure scenarios')
        for seed in SEEDS:
            model=directory/f'policy-seed{seed}.onnx';manifest=json.loads(model.with_suffix('.manifest.json').read_text());verify_manifest(model,manifest)
            if manifest['featureNames']!=feature_names(56) or manifest['environmentConfig']!=report['environmentConfig'] or manifest['actorExcludedFeatures']!=expected[variant] or manifest['trainingEpisodes']!=TRAIN or manifest['validationEpisodes']!=COHORT:raise ValueError('Model contract differs')
            if variant=='N5' and manifest.get('directionLabel')!=dict(horizonSeconds=5,exclusiveThresholdTicks=0):raise ValueError('Manifest5s label mismatch')
            timing=json.loads((directory/f'latency-seed{seed}.json').read_text())
            for key,path in [('modelSha256',model),('manifestSha256',model.with_suffix('.manifest.json')),('paritySha256',directory/f'parity-seed{seed}.json')]:
                if timing[key]!=sha(path):raise ValueError('Timing belongs to another model')
            if timing['observationSize']!=56 or timing['count']!=1200 or not timing['allActionsMatch'] or timing['maxProbabilityError']>1e-6:raise ValueError('C# parity failed')
            models.append(dict(variant=variant,seed=seed,timing=timing,bundle={p.name:sha(p) for p in [model,model.with_suffix('.manifest.json'),directory/f'ppo-seed{seed}.zip',directory/f'normalizer-seed{seed}.pkl']}))
        for scenario in report['scenarios']:
            rows,audit,diagnostic=audit_scenario(directory,scenario,variant)
            (daily if scenario['extraCostTicksPerSide']==0 else pressure).extend(rows);audits.extend(audit);exits.extend(diagnostic)
    validate_daily_keys(daily,variants,SEEDS,COHORT)
    for cost in [1,2]:validate_daily_keys([r for r in pressure if r['extraCostTicksPerSide']==cost],variants,SEEDS,COHORT)
    sensitivity=[];deltas=[]
    for frozen_variant,delay in [(v,d) for v in ['N','N5'] for d in [1,10]]:
        primary=json.loads((root/'models'/frozen_variant/'backtest-report.json').read_text())
        directory=root/f'frozen-{frozen_variant}-{delay}ms';frozen=json.loads((directory/'backtest-multiday.json').read_text())
        if frozen['addedEpisodes']!=COHORT or frozen['latencyMs']!=delay or not frozen['modelsFrozen'] or [s['seed'] for s in frozen['scenarios']]!=SEEDS:raise ValueError('Wrong frozen experiment')
        for scenario in frozen['scenarios']:
            seed=scenario['seed'];bundle=next(m['bundle'] for m in models if m['variant']==frozen_variant and m['seed']==seed)
            if frozen['fingerprints'][str(seed)]!={k:v for k,v in bundle.items() if not k.endswith('.manifest.json')}:raise ValueError('Frozen bundle changed')
            if scenario['latencyMs']!=delay or scenario['extraCostTicksPerSide']!=0:raise ValueError('Frozen scenario altered')
            if delay==1 and scenario['episodes']!=next(s['episodes'] for s in primary['scenarios'] if s['seed']==seed and s['extraCostTicksPerSide']==0):raise ValueError('Frozen1ms does not reproduce primary')
            rows,audit,_=audit_scenario(directory,scenario,frozen_variant,'Frozen');sensitivity.extend(rows);audits.extend(audit)
    for row in [r for r in sensitivity if r['latencyMs']==10]:
        base=next(r for r in sensitivity if r['variant']==row['variant'] and r['latencyMs']==1 and r['seed']==row['seed'] and r['episode']==row['episode'])
        deltas.append(dict(variant=row['variant'],seed=row['seed'],episode=row['episode'],tradesDelta=row['completedTrades']-base['completedTrades'],grossDeltaJPY=row['grossPnlJPY']-base['grossPnlJPY'],netDeltaJPY=row['netPnlJPY']-base['netPnlJPY']))
    summary=[dict(variant=v,**aggregate_round_trips([r for r in daily if r['variant']==v]),initialPassedDays=sum(r['initialTargetPassed'] for r in daily if r['variant']==v),advancedPassedDays=sum(r['advancedTargetPassed'] for r in daily if r['variant']==v)) for v in variants]
    eligible=[v for v in ['R','N','N5'] if combination_eligible(daily,v)]
    result=dict(perDate=daily,summary=summary,pressure=pressure,sensitivity=sensitivity,latencyDeltas=deltas,models=models,cache=caches,selection=selection,
        methodDisclosure=bootstrap_method_description(selection),eligibleGroups=eligible,exitDiagnostics=exits,usage='DevelopmentPreviouslyInspected',deploymentEligible=False)
    (root/'performance-comparison.json').write_text(json.dumps(result,indent=2),encoding='utf-8');(root/'feedback-audit.json').write_text(json.dumps(audits,indent=2),encoding='utf-8')
    for name,rows in [('performance-per-date.csv',daily),('cost-pressure-per-date.csv',pressure),('latency-sensitivity-per-date.csv',sensitivity)]:
        with (root/name).open('w',newline='',encoding='utf-8-sig') as stream:
            writer=csv.DictWriter(stream,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
    def num(v):return '—' if v is None else f'{v:,.2f}'
    lines=['# 225MC 預測能力篩選與特徵疊代','',
        '冷卻10秒、動作延遲1ms；每日完整交易>100、淨勝率>33%、毛損益>0；進階另須淨損益>0。',
        '訓練9/28後段、9/29、10/5後段；10/6及10/7為已檢視開發驗證，不是全新樣本外。',
        'C0同56維原40維控制；R刪除未通過市場特徵；N再加入通過篩選的新特徵。N5與N同特徵，但方向預訓練改5秒/0跳；額外探索組源於訓練診斷時距落差，前三組不變、全部並列。篩選只用訓練日期內walk-forward。','',
        '## 日別績效','', '| 組別／seed／日期 | 交易筆數 | 勝率% | 毛損益JPY | 淨損益JPY | 平均獲利JPY | 平均損失JPY | 初步／進階 |',
        '|---|---:|---:|---:|---:|---:|---:|---|']
    for r in daily:lines.append(f"| {r['variant']}/{r['seed']}/{r['episode'][:8]} | {r['completedTrades']} | {num(r['winRatePercent'])} | {num(r['grossPnlJPY'])} | {num(r['netPnlJPY'])} | {num(r['averageWinningNetJPY'])} | {num(r['averageLosingNetJPY'])} | {'是' if r['initialTargetPassed'] else '否'}/{'是' if r['advancedTargetPassed'] else '否'} |")
    lines+=['','## 三種子兩日合計（比較用，不能取代每日門檻）','', '| 組別 | 交易筆數 | 勝率% | 毛損益JPY | 淨損益JPY | 初步達標日 |','|---|---:|---:|---:|---:|---:|']
    for r in summary:lines.append(f"| {r['variant']} | {r['completedTrades']} | {num(r['winRatePercent'])} | {num(r['grossPnlJPY'])} | {num(r['netPnlJPY'])} | {r['initialPassedDays']}/6 |")
    lines+=['','## 訊號與實際成交漏斗（主回測三種子兩日合計）','',
        '| 組別 | 提議進場 | 有效進場動作 | 實際進場成交 | 等待進場步數 |','|---|---:|---:|---:|---:|']
    for variant in variants:
        group=[a['funnel'] for a in audits if a['phase']=='PrimaryOrCost' and a['extraCostTicksPerSide']==0 and a['variant']==variant]
        values=[sum(a[k] for a in group) for k in ['entrySignals','effectiveEntries','entryFills','pendingEntrySteps']]
        lines.append(f"| {variant} | {' | '.join(str(v) for v in values)} |")
    lines+=['','提議／有效動作不等於成交；進場採被動限價、後續嚴格穿越才成交。方向proxy可預測不代表被動進場後仍有可成交優勢。原因碼分布保存在feedback-audit.json。']
    lines+=['','## 特徵篩選','',f"46個市場候選中保留{len(kept)}個，移除{46-len(kept)}個；十個部位／風控狀態保留。以正規化前遮罩0實作，actor／critic／ONNX一致，歷史原始資料與舊模型保留。",'保留：'+('、'.join(feature_names(56)[i] for i in kept) or '無'),
        '淘汰：'+('、'.join(feature_names(56)[i] for i in selection['removedMarketIndices'])),'',
        '標準：任一5s／30s時距，兩折類別平衡log-loss改善均>0、平均>=0.005、六樣本區塊bootstrap90%下界>0。三次多項式單變量探針；此為探索性操作門檻，未校正多重檢定，不能證明普遍無預測力，也可能漏掉只在多變量組合有效的特徵。',
        bootstrap_method_description(selection),
        '來源：[Queue Imbalance](https://arxiv.org/abs/1512.03492)支持委買賣失衡作短期價格預測研究；[Stoikov Micro-Price](https://www.ma.imperial.ac.uk/~ajacquie/Gatheral60/Slides/Gatheral60%20-%20Stoikov.pdf)區分量加權中價與完整micro-price。本輪量加權中價偏移只是spread×OBI1/2，沒有實作完整Markov估計。其餘交互項為本輪研究假設。',
        '原feature9雖名uptickVolumeRatio5s，實際為sum(qty*tickSign)/sum(qty)，故交互項直接使用[-1,1]signed ratio。標籤是未來last-trade方向proxy，並非可成交損益。','',
        '## 參數、C#與回饋稽核','',
        '1口，每點10JPY、5點/跳；佣金36JPY/邊，原成本72JPY/完整交易。5秒決策、停損6跳、最長持有60秒、收盤緩衝310秒。共享paper-strict-cross與帳本，沒有實盤委託。勝率及平均獲利／損失按完整交易扣費後計。',
        'PPO每seed102400步，seeds11/22/33，網路[64,64]Tanh，learning_rate0.0003、n_steps2048、batch64、epochs10、gamma0.99、GAE0.95、clip0.2、entropy0.01、target_KL0.02。C0/R/N方向預訓練20epochs、30秒閾值正負2跳；N5則為20epochs、5秒閾值0跳、Adam0.001、batch256、gradient clip0.5；reward成本權重0、淨權益回撤懲罰0.25，實際績效仍扣全部費用。',
        f"五份cache/raw SHA核對；前三組預訓練原始56維與30秒標籤完全一致，N5與訓練內5秒樣本逐值一致。12模型各1200快照C#/Python action parity一致。主回測24、成本壓力48、凍結延遲24日別，共{sum(a['transitions'] for a in audits):,} transitions逐筆成交／帳本／回饋互核。"]
    timings=[m['timing'] for m in models]
    lines+=[f"Actor p99 {min(t['p99Ms'] for t in timings):.4f}–{max(t['p99Ms'] for t in timings):.4f}ms，最大{max(t['maxMs'] for t in timings):.4f}ms，>1ms共{sum(t['countAbove1Ms'] for t in timings)}/14400。包含normalization/ORT/mask，不含特徵、broker/network；1ms為回測設定，不能保證全流程1ms。",'',
        '## 延遲與成本壓力','', 'N／N5凍結1ms共12日別與主回測完全一致；10ms重播差額如下，是整個策略路徑的敏感度，不是同一委託的純滑價。','',
        '| 組別／seed／日期 | 交易差 | 毛差JPY | 淨差JPY |','|---|---:|---:|---:|']
    for r in deltas:lines.append(f"| {r['variant']}/{r['seed']}/{r['episode'][:8]} | {r['tradesDelta']} | {num(r['grossDeltaJPY'])} | {num(r['netDeltaJPY'])} |")
    lines+=['','額外每邊1／2跳透過費用壓力實作，每完整交易總成本172／272JPY；價格未被平移，不能稱為真實滑價。日別明細見cost-pressure-per-date.csv。','',
        f"主回測初步達標{sum(r['initialTargetPassed'] for r in daily)}/24，進階達標{sum(r['advancedTargetPassed'] for r in daily)}/24。至少2/3種子兩日皆初步達標之組別：{','.join(eligible) or '無'}。尚未符合每日目標者不採用。"]
    (root/'research-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(dict(models=len(models),primaryDaily=len(daily),pressureDaily=len(pressure),frozenDaily=len(sensitivity),transitions=sum(a['transitions'] for a in audits),eligible=eligible)),flush=True)

if __name__=='__main__':main()
