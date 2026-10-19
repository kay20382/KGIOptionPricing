"""Daily strict target gates and comparable-cohort candidate evidence."""
import argparse
import csv
import gzip
import json
import math
from pathlib import Path
from performance_metrics import episode_metrics, completed_trade_counts, aggregate_round_trips, daily_targets

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
parser.add_argument('--baseline', required=True)
args = parser.parse_args()
root = Path(args.root)
baseline = json.loads(Path(args.baseline).read_text())
cohort = ['20261006-day', '20261007-day']
seeds = [11,22,33]
daily = []
for row in baseline['perDate']:
    if row['seed'] in seeds and row['episode'] in cohort:
        daily.append(dict(variant='Baseline', seed=row['seed'], episode=row['episode'],
            **{key:row[key] for key in ['completedTrades','grossPnlJPY','netPnlJPY','costsJPY','winRatePercent',
                'averageWinningNetJPY','averageLosingNetJPY','winningTrades','losingTrades']}))
evidence = {}
audits = []
for variant in ['A','B','C']:
    directory = root / f'candidate-{variant}'
    report = json.loads((directory / 'backtest-report.json').read_text())
    if len(report['training']) != 3 or len(report['scenarios']) != 3 or report['validation'] != cohort:
        raise ValueError('Missing prespecified experiment/cohort results')
    evidence[variant] = dict(train=report['train'], validation=cohort,
        actualStepsBySeed=report['training'], actorExcludedFeatures=report.get('actorExcludedFeatures', []),
        rewardCostWeight=report.get('rewardCostWeight', 1), trainingLatencyMs=report['trainingActionLatencyMs'],
        versions=report['versions'], parity=[])
    for scenario in report['scenarios']:
        seed = scenario['seed']
        if scenario['latencyMs'] != 1 or scenario['extraCostTicksPerSide'] != 0:
            raise ValueError('Execution configuration changed')
        timing = json.loads((directory / f'latency-seed{seed}.json').read_text())
        if not timing['allActionsMatch'] or timing['count'] < 1200 or timing['maxProbabilityError'] > 1e-6:
            raise ValueError('ONNX/C# parity failed')
        evidence[variant]['parity'].append(dict(seed=seed, **timing))
        for episode in scenario['episodes']:
            metric = episode_metrics(episode, multiplier=10)
            metric.update(completed_trade_counts(metric))
            if metric['costsJPY'] != metric['completedTrades'] * 72:
                raise ValueError('Round-trip accounting changed')
            daily.append(dict(variant=variant, seed=seed, episode=episode['episode'], **metric))
        totals = {}
        with gzip.open(directory / f'feedback-seed{seed}-delay1-cost0.jsonl.gz', 'rt', encoding='utf-8') as reader:
            for line in reader:
                transition = json.loads(line)
                episode, end = transition['episode'], transition['next']
                row = totals.setdefault(episode, dict(delta=0, reward=0, transitions=0))
                row['delta'] += end['deltaEquity']
                row['reward'] += end['reward']
                row['transitions'] += 1
                row['last'] = end
        if sorted(totals) != cohort:
            raise ValueError('Missing feedback episode')
        for episode, row in totals.items():
            end = row.pop('last')
            if end['status'] != 'CompleteEpisode' or end['position'] != 0:
                raise ValueError('Incomplete experiment replay')
            if not math.isclose(row['delta'], end['netPnl'], abs_tol=1e-6) or not math.isclose(row['reward'], end['totalReward'], abs_tol=1e-6):
                raise ValueError('Feedback reward/ledger mismatch')
            audits.append(dict(variant=variant, seed=seed, episode=episode, **row))
for row in daily:
    row.update(daily_targets(row, partial=False))
summaries = []
for variant in ['Baseline','A','B','C']:
    for seed in seeds:
        records = [row for row in daily if row['variant'] == variant and row['seed'] == seed]
        if len(records) != 2:
            raise ValueError('Two complete daily results required')
        summaries.append(dict(variant=variant, seed=seed, **aggregate_round_trips(records),
            initialPassedDays=sum(row['initialTargetPassed'] for row in records),
            advancedPassedDays=sum(row['advancedTargetPassed'] for row in records)))
result = dict(targets=dict(dailyTradesExclusive=100, netWinRatePercentExclusive=33,
                          initialGrossExclusive=0, advancedNetExclusive=0),
              cohort=cohort, perDate=daily, summaries=summaries, experiment=evidence,
              deploymentEligible=False, usage='DevelopmentPreviouslyInspected')
(root / 'performance-comparison.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
(root / 'feedback-audit.json').write_text(json.dumps(audits, indent=2), encoding='utf-8')
with (root / 'performance-per-date.csv').open('w', newline='', encoding='utf-8-sig') as destination:
    fields = list(daily[0])
    writer = csv.DictWriter(destination, fieldnames=fields, extrasaction='ignore')
    writer.writeheader()
    writer.writerows(daily)
def fmt(value):
    return '—' if value is None else f'{value:,.2f}'
lines = ['# 225MC 強化學習模型改進與每日目標驗證', '',
    '目標：每個完整日盤交易筆數 >100、扣成本後勝率 >33%、初步毛損益 >0；進階淨損益 >0。固定交易動作延遲 1 ms。100 筆或 33% 恰好等於門檻均不算通過。', '',
    '原模型及三個候選在相同 10/6、10/7 完整日盤比較。新增模型訓練使用 9/28 後段、9/29、10/5 後段，每 seed 102,400 步，seeds 11／22／33。原模型僅使用 9/28 後段 20,480 步、0 ms 訓練；候選為 1 ms 訓練。這些日期均曾檢視，仍是開發驗證。', '',
    'A：跨日訓練及增加步數。B：A 加上移除累積淨損益／回撤的模型輸入（actor 與 critic），原始帳本／reward 留存；ONNX 內嵌輸入遮罩。C：A 改以毛權益差為訓練 reward 主體，仍保留淨權益回撤懲罰；帳本照扣每邊 36 JPY。', '',
    '## 每日績效與門檻', '',
    '| 候選／Seed／日期 | 交易筆數 | 勝率 | 毛損益 JPY | 淨損益 JPY | 初步達標 | 進階達標 |',
    '|---|---:|---:|---:|---:|---|---|']
for row in daily:
    lines.append(f"| {row['variant']}／{row['seed']}／{row['episode'][:8]} | {row['completedTrades']} | {fmt(row['winRatePercent'])}% | {row['grossPnlJPY']:,.0f} | {row['netPnlJPY']:,.0f} | {'是' if row['initialTargetPassed'] else '否'} | {'是' if row['advancedTargetPassed'] else '否'} |")
lines += ['', '## 相同兩日合計', '',
    '| 候選／Seed | 交易筆數 | 勝率 | 毛損益 JPY | 淨損益 JPY | 初步達標日 | 進階達標日 |',
    '|---|---:|---:|---:|---:|---:|---:|']
for row in summaries:
    lines.append(f"| {row['variant']}／{row['seed']} | {row['completedTrades']} | {fmt(row['winRatePercent'])}% | {row['grossPnlJPY']:,.0f} | {row['netPnlJPY']:,.0f} | {row['initialPassedDays']}/2 | {row['advancedPassedDays']}/2 |")
lines += ['', '交易筆數為完整進出一趟；勝率按淨獲利 >0。多日勝率以筆數加權，但達標判斷只用日別結果。平均獲利／損失與成本亦保留在 JSON／CSV，不以減少交易造成的虧損縮小冒充成功。', '',
    '## 固定參數與限制', '',
    '225MC 1 口，225M／225 作參考；每點 10 JPY、最小跳動 5 點、佣金每邊 36 JPY、額外成本 0 跳。每 5 秒決策、停損 6 跳、最長持有 60 秒、冷卻 30 秒、收盤緩衝 310 秒。模型 [64,64]、Tanh；learning rate 0.0003、n_steps 2048、batch size 64、epochs 10、gamma 0.99、GAE 0.95、clip 0.2、entropy 0.01、target KL 0.02，回撤懲罰 0.25。', '',
    '每趟成本 72 JPY，101 趟至少 7,272 JPY 成本。勝率 >33% 只是其中一個條件；平均獲利／平均損失與成本共同決定淨損益。未用交易筆數獎勵強迫過度交易，亦未降低佣金或修改穿價模型。', '',
    f'工程核對：18 個候選模型／日期組合完整平倉，{sum(row["transitions"] for row in audits):,} 筆回饋的淨權益差及 reward 合計核對通過。9 個 ONNX 模型各 1,200 筆 Python／C# 動作一致。推論時間仍不含特徵／網路，不代表端到端 1 ms 保證。', '',
    '資料量仍不足完整新樣本外驗證，所有候選 deploymentEligible=false。未達門檻時保留拒絕原因，不挑掉虧損日或用部分日盤代替完整日盤。']
if not any(row['initialPassedDays'] == 2 for row in summaries if row['variant'] != 'Baseline'):
    lines += ['', '本輪沒有候選在兩個驗證日都達成初步目標。目標尚未達成，這些模型不予採用。下一輪需檢查可成交入場訊號的預測優勢與停損後果，並增加未使用的完整交易日；單純增加 PPO 步數不足以創造市場優勢。']
(root / 'research-report.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')
print(json.dumps(dict(dailyRows=len(daily), auditedCandidateEpisodes=len(audits),
                     initialPassed=sum(row['initialTargetPassed'] for row in daily if row['variant']!='Baseline'),
                     advancedPassed=sum(row['advancedTargetPassed'] for row in daily if row['variant']!='Baseline'))))
