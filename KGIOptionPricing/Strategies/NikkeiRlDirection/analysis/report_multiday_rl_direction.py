"""Per-date and trade-weighted multi-date performance from frozen-model replay."""
import argparse
import csv
import gzip
import hashlib
import json
import math
from pathlib import Path
from performance_metrics import episode_metrics, completed_trade_counts, aggregate_round_trips, daily_targets

parser = argparse.ArgumentParser()
parser.add_argument('--run', required=True)
parser.add_argument('--original', required=True)
parser.add_argument('--dataset', required=True)
args = parser.parse_args()
run, original = Path(args.run), Path(args.original)
added = json.loads((run / 'backtest-multiday.json').read_text())
existing = json.loads((original / 'measured-latency-backtest.json').read_text())
if len(added['scenarios']) != 5:
    raise ValueError('All five frozen seeds required')
rows = []
for scenario in existing + added['scenarios']:
    if scenario['latencyMs'] != 1 or scenario['extraCostTicksPerSide'] != 0:
        raise ValueError('Fixed 1 ms and no additional cost required')
    for episode in scenario['episodes']:
        metric = episode_metrics(episode, multiplier=10)
        metric.update(completed_trade_counts(metric))
        metric.update(daily_targets(metric, partial='postgap' in episode['episode']))
        rows.append(dict(seed=scenario['seed'], episode=episode['episode'],
                         partialSession='postgap' in episode['episode'], **metric))
keys = [(row['seed'], row['episode']) for row in rows]
if len(set(keys)) != len(keys):
    raise ValueError('Duplicate seed/date data')
aggregate = [dict(seed=seed, **aggregate_round_trips([row for row in rows if row['seed'] == seed]))
             for seed in [11,22,33,44,55]]
parameters = json.loads((original / 'performance-1ms.json').read_text())['parameters']
parameters['validation'] = sorted({row['episode'] for row in rows})
result = dict(parameters=parameters, perDate=rows, aggregateBySeed=aggregate,
              dateCount=len(parameters['validation']), partialDateCount=1,
              modelsFrozen=True, deploymentEligible=False, usage='DevelopmentPreviouslyInspected')
(run / 'performance-multiday.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
for filename, records in [('performance-per-date.csv', rows), ('performance-summary.csv', aggregate)]:
    with (run / filename).open('w', encoding='utf-8-sig', newline='') as destination:
        writer = csv.DictWriter(destination, fieldnames=list(records[0]))
        writer.writeheader()
        writer.writerows(records)
audit = []
for directory, expected_episodes in [(run, len(added['addedEpisodes'])), (original, 1)]:
    for seed in [11,22,33,44,55]:
        path = directory / f'feedback-seed{seed}-delay1-cost0.jsonl.gz'
        totals = {}
        with gzip.open(path, 'rt', encoding='utf-8') as reader:
            for line in reader:
                transition = json.loads(line)
                episode, end = transition['episode'], transition['next']
                value = totals.setdefault(episode, dict(delta=0, reward=0, transitions=0))
                value['delta'] += end['deltaEquity']
                value['reward'] += end['reward']
                value['transitions'] += 1
                value['last'] = end
        if len(totals) != expected_episodes:
            raise ValueError('Missing recorded feedback episode')
        for episode, value in totals.items():
            end = value.pop('last')
            if end['status'] != 'CompleteEpisode' or end['position'] != 0:
                raise ValueError('Incomplete replay feedback')
            if not math.isclose(value['delta'], end['netPnl'], abs_tol=1e-6) or not math.isclose(value['reward'], end['totalReward'], abs_tol=1e-6):
                raise ValueError('Reward/equity feedback mismatch')
            audit.append(dict(seed=seed, episode=episode, **value))
(run / 'feedback-audit.json').write_text(json.dumps(audit, indent=2), encoding='utf-8')
dataset = json.loads(Path(args.dataset).read_text(encoding='utf-8-sig'))
manifests = {key: json.loads(Path(value + '.manifest.json').read_text(encoding='utf-8-sig')) for key,value in dataset.items()}
for key, path in dataset.items():
    if hashlib.sha256(Path(path).read_bytes()).hexdigest().upper() != manifests[key]['cacheSha256']:
        raise ValueError('Cache changed after replay')
(run / 'cache-provenance.json').write_text(json.dumps(manifests, indent=2), encoding='utf-8')
def display(value):
    return '—' if value is None else f'{value:,.2f}'
def metric_line(row, prefix):
    return f"| {prefix} | {display(row['winRatePercent'])}% | {row['grossPnlJPY']:,.0f} | {row['costsJPY']:,.0f} | {row['netPnlJPY']:,.0f} | {row['completedTrades']} | {display(row['averageWinningNetJPY'])} | {display(row['averageLosingNetJPY'])} |"
lines = ['# 225MC 強化學習多日期回測（固定 1 ms）', '',
    '新增 10/5、10/6、10/7 日盤，合併既有 9/29 結果。五個 seed 為不同候選模型，各自跨日期加總，不能把五個 seed 視為同一投資組合。模型及正規化均凍結，未重訓或依新增日期調參。', '',
    '10/5 08:58:50／55 有原始事件中斷，只保留 65 秒暖機後的 09:00 起區段，標為部分日盤；10/6、10/7 通過時間順序、缺口及遺失事件檢查。共四個日期，其中三個完整日盤、一個部分日盤。', '',
    '## 各模型多日期合計', '',
    '| Seed | 勝率 | 毛損益 JPY | 成本 JPY | 淨損益 JPY | 交易次數 | 平均獲利 JPY | 平均損失 JPY |',
    '|---|---:|---:|---:|---:|---:|---:|---:|']
for row in aggregate:
    lines.append(metric_line(row, row['seed']))
lines += ['', '交易次數為完整進出一趟；勝率及平均獲利／損失按每趟扣成本後淨損益。多日勝率及平均金額按相應交易筆數加權，沒有直接平均每日百分比。毛損益－成本＝淨損益；平手交易不當作虧損交易。每天重新設定狀態，不跨日持倉。', '',
    '每日目標：完整日盤交易筆數 >100、勝率 >33%、初步毛損益 >0；進階再要求淨損益 >0。部分日盤不作每日達標證據。JSON／每日 CSV 已附各門檻與達標狀態。', '',
    '## 各日期明細', '', '| 日期／Seed | 勝率 | 毛損益 JPY | 成本 JPY | 淨損益 JPY | 交易次數 | 平均獲利 JPY | 平均損失 JPY |',
    '|---|---:|---:|---:|---:|---:|---:|---:|']
for row in sorted(rows, key=lambda item: (item['episode'], item['seed'])):
    lines.append(metric_line(row, f"{row['episode']}／{row['seed']}"))
lines += ['', '## 參數設定', '', '| 項目 | 設定 |', '|---|---|',
    '| 商品／參考商品／口數 | 225MC／225M、225／1 口 |',
    '| 交易動作延遲／決策週期 | 固定 1 ms／5 秒 |',
    '| 每點價值／最小跳動 | 10 JPY／5 點 |',
    '| 每邊佣金／額外成本 | 36 JPY／0 跳 |',
    '| 停損／最長持有／冷卻 | 6 跳／60 秒／30 秒 |',
    '| 收盤緩衝／reward 回撤係數 | 撮合收盤前 310 秒／0.25 |',
    '| PPO／網路 | MaskablePPO／actor、critic [64,64]，Tanh |',
    '| learning rate／n_steps／batch size／epochs | 0.0003／2048／64／10 |',
    '| gamma／GAE lambda／clip／entropy／target KL | 0.99／0.95／0.2／0.01／0.02 |',
    '| 原訓練資料／步數／訓練延遲 | 9/28 10:01:45 後段／每 seed 20,480／0 ms |', '',
    '## 驗證與限制', '',
    '15 個新增模型／日期組合均完整平倉。加上原有 5 個組合，逐步反饋的權益差合計與共用帳本淨損益、reward 合計與 totalReward 已核對。ONNX、PPO checkpoint、normalizer 的 SHA 於回測前後一致。', '',
    '採共用嚴格穿價成交與帳本；1 ms 延遲重播期間行情，固定停損及風控持續執行。未包含排隊、市場衝擊與完整價格滑價；固定 1 ms 不是端到端實測延遲。', '',
    '日期曾用於其他研究，仍屬開發驗證；未達完整新資料／20 日等採用門檻。保留模型拒絕採用與 deploymentEligible=false。', '',
    '檔案：performance-per-date.csv、performance-summary.csv、performance-multiday.json、backtest-multiday.json、feedback-audit.json、cache-provenance.json。']
(run / 'research-report.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')
print(f'Multi-date report PASS: {len(rows)} seed/date rows, {sum(row["transitions"] for row in audit)} audited transitions')
