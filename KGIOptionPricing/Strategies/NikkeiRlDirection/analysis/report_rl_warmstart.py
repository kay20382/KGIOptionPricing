"""Compare prespecified warm-start candidates on the same complete daily cohort."""
import argparse
import csv
import gzip
import hashlib
import json
import math
from pathlib import Path
from performance_metrics import episode_metrics, completed_trade_counts, aggregate_round_trips, daily_targets
from feedback import verify_manifest
from fill_diagnostics import reconcile_episode, validate_daily_keys

parser=argparse.ArgumentParser()
parser.add_argument('--root',required=True)
parser.add_argument('--baseline',required=True)
args=parser.parse_args()
root=Path(args.root)
cohort=['20261006-day','20261007-day'];seeds=[11,22,33]
baseline=json.loads(Path(args.baseline).read_text())
daily=[dict(row) for row in baseline['perDate'] if row['variant']=='C']
if len(daily)!=6 or sorted(set(r['episode'] for r in daily))!=cohort: raise ValueError('Baseline cohort mismatch')
validate_daily_keys(daily,['C'],seeds,cohort)
audits=[];evidence={};exit_diagnostics=[]
for variant,cooldown in [('D',30),('E',5)]:
    directory=root/f'candidate-{variant}'
    report=json.loads((directory/'backtest-report.json').read_text())
    labels=json.loads((directory/'direction-training.json').read_text())
    if report['validation']!=cohort or len(report['training'])!=3 or len(report['scenarios'])!=3:
        raise ValueError('Missing experiment/cohort results')
    if report['environmentConfig']!=dict(cooldownSeconds=cooldown,decisionLatencyMs=1,rewardCostWeight=0):
        raise ValueError('Prespecified environment changed')
    if report['actorExcludedFeatures']!=[] or report['directionWarmstartEpochs']!=20:
        raise ValueError('Warmstart experiment changed')
    if [r['seed'] for r in report['training']]!=seeds or any(r['actualSteps']!=102400 for r in report['training']):
        raise ValueError('Missing prespecified seeds/steps')
    if [r['episode'] for r in labels['episodes']]!=report['train'] or set(report['train']) & set(cohort):
        raise ValueError('Direction-label split contaminated')
    if hashlib.sha256((directory/'direction-training.npz').read_bytes()).hexdigest().upper()!=labels['npzSha256']:
        raise ValueError('Direction training data changed')
    evidence[variant]=dict(training=report['training'],warmstart=report['warmstart'],labels=labels,
        environmentConfig=report['environmentConfig'],parity=[],fingerprints={})
    for scenario in report['scenarios']:
        seed=scenario['seed']
        if scenario['latencyMs']!=1 or scenario['extraCostTicksPerSide']!=0: raise ValueError('Execution configuration changed')
        modelpath=directory/f'policy-seed{seed}.onnx'
        manifest=json.loads(modelpath.with_suffix('.manifest.json').read_text())
        verify_manifest(modelpath,manifest)
        if manifest['environmentConfig']!=report['environmentConfig']: raise ValueError('Model/environment mismatch')
        paths=[modelpath,directory/f'ppo-seed{seed}.zip',directory/f'normalizer-seed{seed}.pkl']
        evidence[variant]['fingerprints'][seed]={p.name:hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in paths}
        timing=json.loads((directory/f'latency-seed{seed}.json').read_text())
        if not timing['allActionsMatch'] or timing['count']<1200 or timing['maxProbabilityError']>1e-6:
            raise ValueError('ONNX/C# parity failed')
        evidence[variant]['parity'].append(dict(seed=seed,**timing))
        if [e['episode'] for e in scenario['episodes']]!=cohort: raise ValueError('Daily cohort changed')
        for episode in scenario['episodes']:
            metric=episode_metrics(episode,multiplier=10)
            metric.update(completed_trade_counts(metric))
            if metric['costsJPY']!=metric['completedTrades']*72: raise ValueError('Round-trip accounting changed')
            row=dict(variant=variant,seed=seed,episode=episode['episode'],**metric)
            row.update(daily_targets(row,partial=False));daily.append(row)
        totals={}
        with gzip.open(directory/f'feedback-seed{seed}-delay1-cost0.jsonl.gz','rt',encoding='utf-8') as reader:
            for line in reader:
                transition=json.loads(line);end=transition['next'];episode=transition['episode']
                row=totals.setdefault(episode,dict(delta=0,reward=0,transitions=0,fills=[]))
                row['delta']+=end['deltaEquity'];row['reward']+=end['reward'];row['transitions']+=1;row['last']=end
                row['fills'].extend(end['fills'])
        if sorted(totals)!=cohort: raise ValueError('Missing feedback episode')
        for episode,row in totals.items():
            end=row.pop('last')
            if end['status']!='CompleteEpisode' or end['position']!=0: raise ValueError('Incomplete experiment replay')
            if not math.isclose(row['delta'],end['netPnl'],abs_tol=1e-6) or not math.isclose(row['reward'],end['totalReward'],abs_tol=1e-6):
                raise ValueError('Feedback reward/ledger mismatch')
            summary=next(e for e in scenario['episodes'] if e['episode']==episode)
            outcomes=reconcile_episode(summary,end,row.pop('fills'))
            for role in ['Flatten','StopLoss','TakeProfit']:
                group=[r for r in outcomes if r['exitRole']==role]
                if group:
                    exit_diagnostics.append(dict(variant=variant,seed=seed,episode=episode,exitRole=role,
                        trades=len(group),grossPnlJPY=sum(r['grossPnlJPY'] for r in group),
                        netPnlJPY=sum(r['netPnlJPY'] for r in group),
                        winRatePercent=100*sum(r['netPnlJPY']>0 for r in group)/len(group),
                        meanHoldingSeconds=sum(r['holdingSeconds'] for r in group)/len(group)))
            audits.append(dict(variant=variant,seed=seed,episode=episode,**row))
validate_daily_keys(daily,['C','D','E'],seeds,cohort)
summaries=[]
for variant in ['C','D','E']:
    for seed in seeds:
        records=[r for r in daily if r['variant']==variant and r['seed']==seed]
        summaries.append(dict(variant=variant,seed=seed,**aggregate_round_trips(records),
            initialPassedDays=sum(r['initialTargetPassed'] for r in records),
            advancedPassedDays=sum(r['advancedTargetPassed'] for r in records)))
result=dict(perDate=daily,summaries=summaries,experiment=evidence,cohort=cohort,exitDiagnostics=exit_diagnostics,
    usage='DevelopmentPreviouslyInspected',deploymentEligible=False,
    targets=dict(dailyTradesExclusive=100,netWinRatePercentExclusive=33,initialGrossExclusive=0,advancedNetExclusive=0))
(root/'performance-comparison.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
(root/'feedback-audit.json').write_text(json.dumps(audits,indent=2),encoding='utf-8')
with (root/'performance-per-date.csv').open('w',newline='',encoding='utf-8-sig') as destination:
    writer=csv.DictWriter(destination,fieldnames=list(daily[0]),extrasaction='ignore')
    writer.writeheader();writer.writerows(daily)
def fmt(value): return '—' if value is None else f'{value:,.2f}'
lines=['# 225MC 方向預訓練與 PPO 優化回測','',
    '每日目標：完整進出 >100 筆、按淨獲利計算勝率 >33%、初步毛損益 >0；進階另須淨損益 >0。交易動作延遲固定 1 ms。','',
    'C 為上一輪毛權益回饋候選。D 在 C 上增加方向預訓練；E 在 D 上將冷卻 30 秒改為 5 秒。每個候選 seeds 11／22／33、102400 PPO 步。未改佣金、停損或成交模型。','',
    '預訓練只用 9/28 後段、9/29、10/5 後段，30 秒後成交價變化 >2 跳／<-2 跳分別標為多／空，其餘等待。起點輸入仍是當時已知 40 維資料；方向標籤只代表價格變化，不代表限價單能成交或實際獲利。採類別加權交叉熵訓練 actor 20 epochs，normalizer 只擬合訓練輸入，再由 PPO 學習成交及出場。','',
    '## 日別績效','',
    '| 候選／Seed／日期 | 交易筆數 | 勝率 | 毛損益 JPY | 淨損益 JPY | 初步達標 | 進階達標 |',
    '|---|---:|---:|---:|---:|---|---|']
for row in daily:
    lines.append(f"| {row['variant']}／{row['seed']}／{row['episode'][:8]} | {row['completedTrades']} | {fmt(row['winRatePercent'])}% | {row['grossPnlJPY']:,.0f} | {row['netPnlJPY']:,.0f} | {'是' if row['initialTargetPassed'] else '否'} | {'是' if row['advancedTargetPassed'] else '否'} |")
lines+=['','## 同兩日合計（不作每日達標判斷）','',
    '| 候選／Seed | 交易筆數 | 勝率 | 毛損益 JPY | 淨損益 JPY | 初步達標日 | 進階達標日 |',
    '|---|---:|---:|---:|---:|---:|---:|']
for row in summaries:
    lines.append(f"| {row['variant']}／{row['seed']} | {row['completedTrades']} | {fmt(row['winRatePercent'])}% | {row['grossPnlJPY']:,.0f} | {row['netPnlJPY']:,.0f} | {row['initialPassedDays']}/2 | {row['advancedPassedDays']}/2 |")
passed=sum(r['initialTargetPassed'] for r in daily if r['variant']!='C')
advanced=sum(r['advancedTargetPassed'] for r in daily if r['variant']!='C')
timings=[p for e in evidence.values() for p in e['parity']]
lines+=['','## 成交後虧損診斷（三個種子、兩個日期合計）','',
    '| 候選 | 出場原因 | 交易筆數 | 毛損益 JPY | 淨損益 JPY |',
    '|---|---|---:|---:|---:|']
for variant in ['D','E']:
    for role in ['Flatten','StopLoss','TakeProfit']:
        group=[r for r in exit_diagnostics if r['variant']==variant and r['exitRole']==role]
        if group:
            lines.append(f"| {variant} | {role} | {sum(r['trades'] for r in group)} | {sum(r['grossPnlJPY'] for r in group):,.0f} | {sum(r['netPnlJPY'] for r in group):,.0f} |")
lines+=['','## 設定與驗證','',
    '225MC 1 口；每點 10 JPY、每跳 5 點、每邊佣金 36 JPY、每趟 72 JPY。決策間隔 5 秒、停損 6 跳、最長持有 60 秒、收盤緩衝 310 秒；D 冷卻 30 秒，E 冷卻 5 秒。reward 成本權重 0，淨權益回撤懲罰 0.25；實際帳本與淨績效仍扣全部佣金。','',
    'PPO：[64,64] Tanh、learning rate 0.0003、n_steps 2048、batch 64、epochs 10、gamma 0.99、GAE 0.95、clip 0.2、entropy 0.01、target KL 0.02。預訓練 Adam learning rate 0.001、batch 256、gradient clip 0.5。','',
    f"預訓練每個候選 {sum(r['samples'] for r in evidence['D']['labels']['episodes']):,} 筆。6 個模型各 1200 raw observation 的 Python／C# 動作一致，機率誤差 <1e-6。p99 {min(t['p99Ms'] for t in timings):.4f}–{max(t['p99Ms'] for t in timings):.4f} ms，最大 {max(t['maxMs'] for t in timings):.4f} ms。此量測僅含 actor 推論，1 ms 是回測假設，不是端到端保證。",'',
    f"12 個模型／日期回饋完整平倉，共 {sum(r['transitions'] for r in audits):,} 個 transitions；淨權益差與 reward 合計核對通過。每日費用及完整交易筆數一致。平均獲利／損失及參數保留於 JSON／CSV。",'',
    f'候選初步通過 {passed}/12 日別結果，進階 {advanced}/12。驗證日 10/6、10/7 已於先前研究使用，資料仍不足全新樣本外驗證；所有模型 deploymentEligible=false。']
if passed==0:
    lines+=['','本輪仍未達目標，候選不予採用。方向預訓練或縮短冷卻不保證建立可成交優勢；下一步應增加全新完整交易日，並檢查限價成交的逆向選擇與入場後價格路徑。']
(root/'research-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(json.dumps(dict(dailyRows=len(daily),auditedCandidateEpisodes=len(audits),initialPassed=passed,advancedPassed=advanced)))
