"""Build an evidence report and feedback diagnosis from completed offline runs."""
import argparse
import hashlib
import json
import csv
from pathlib import Path
from performance_metrics import episode_metrics


def sha(path):
    with path.open('rb') as source:
        digest = hashlib.sha256()
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
        return digest.hexdigest().upper()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run', required=True)
    parser.add_argument('--baselines', required=True)
    parser.add_argument('--dataset', required=True)
    args = parser.parse_args()
    run = Path(args.run)
    report = json.loads((run / 'backtest-report.json').read_text(encoding='utf-8'))
    baselines = json.loads(Path(args.baselines).read_text(encoding='utf-8'))
    measured = json.loads((run / 'measured-latency-backtest.json').read_text())
    training = report['training']
    if len(training) != 5 or len(report['scenarios']) != 45:
        raise ValueError('Five complete seeds and 45 scenarios required')
    if any(e['status'] != 'CompleteEpisode' for row in report['scenarios'] + measured + baselines for e in row['episodes']):
        raise ValueError('Execution incomplete; do not publish a completed report')
    dataset = json.loads(Path(args.dataset).read_text(encoding='utf-8-sig'))
    market_names = ['ret5Ticks', 'ret15Ticks', 'ret30Ticks', 'ret60Ticks', 'spreadTicks',
                    'obi1', 'obi5', 'bookAgeSecondsCapped60', 'log1p5SecondVolume',
                    'volumeWeightedUptickSign5Seconds']
    feature_names = [f'{product}.{name}' for product in ['225MC', '225M', '225'] for name in market_names]
    feature_names += ['positionContracts', 'holdingSecondsDiv60', 'unrealizedPnlDiv50JPY',
                      'netPnlDiv50JPY', 'currentDrawdownDiv50JPY', 'cooldownSecondsDiv30',
                      'pendingEntry', 'pendingExit', 'validMarketState', 'hoursToAuctionClose']
    for row in training:
        path = run / f"policy-seed{row['seed']}.manifest.json"
        manifest = json.loads(path.read_text(encoding='utf-8'))
        manifest['featureNames'] = feature_names
        manifest['trainingActionLatencyMs'] = report.get('trainingActionLatencyMs', 0)
        manifest['evaluationDecisionLatencyMs'] = 1
        manifest['environment'] = dict(decisionCadenceSeconds=5, stopTicks=6,
                                       maxHoldingSeconds=60, cooldownSeconds=30,
                                       closeBufferSeconds=310, timeZone='UTC+08:00',
                                       fills='Shared paper-strict-cross; no queue/market-impact model')
        path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    cache_manifests = {key: json.loads(Path(value + '.manifest.json').read_text(encoding='utf-8-sig'))
                       for key, value in dataset.items()}
    binaries = ['KGIOptionPricing/bin/Release/net10.0-windows/KGIOptionPricing.dll',
                'tools/NikkeiRlReplayBridge/bin/Release/net10.0-windows/NikkeiRlReplayBridge.dll',
                'tools/NikkeiRlModel/bin/Release/net10.0/NikkeiRlModel.dll',
                'tools/NikkeiRlReplayBridge/native/onnxruntime.dll']
    evidence = dict(schema='nikkei-rl-evidence-v1', cacheManifests=cache_manifests,
                    binaries={path: sha(Path(path)) for path in binaries},
                    runUsage='DevelopmentPreviouslyInspected', deploymentEligible=False)
    (run / 'execution-provenance.json').write_text(json.dumps(evidence, indent=2), encoding='utf-8')
    zero = [row for row in report['scenarios'] if row['latencyMs'] == 0 and row['extraCostTicksPerSide'] == 0]
    metrics = []
    for row in measured:
        if row['latencyMs'] != 1 or row['extraCostTicksPerSide'] != 0 or len(row['episodes']) != 1:
            raise ValueError('Primary performance report requires fixed 1 ms, no extra cost, one validation episode')
        metrics.append(dict(seed=row['seed'], latencyMs=1,
                            **episode_metrics(row['episodes'][0], multiplier=10)))
    parameters = dict(decisionLatencyMs=1, extraCostTicksPerSide=0, commissionJPYPerSide=36,
        quantityContracts=1, tickSizePoints=5, multiplierJPYPerPoint=10, decisionCadenceSeconds=5,
        stopTicks=6, maxHoldingSeconds=60, cooldownSeconds=30, closeBufferSeconds=310,
        drawdownRewardPenalty=0.25, trainingActionLatencyMs=report.get('trainingActionLatencyMs', 0), learningRate=0.0003,
        nSteps=2048, batchSize=64, epochs=10, gamma=0.99, gaeLambda=0.95, clipRange=0.2,
        entropyCoefficient=0.01, targetKL=0.02, actorCriticLayers=[64,64], activation='Tanh',
        stepsPerSeed=20480, seeds=[11,22,33,44,55], training=report['train'], validation=report['validation'])
    (run / 'performance-1ms.json').write_text(json.dumps(dict(parameters=parameters, performance=metrics), indent=2), encoding='utf-8')
    with (run / 'performance-1ms.csv').open('w', newline='', encoding='utf-8-sig') as destination:
        writer = csv.DictWriter(destination, fieldnames=list(metrics[0]))
        writer.writeheader()
        writer.writerows(metrics)
    reasons = ['Only one development validation day; minimum 20 fresh validation days not met',
               'Training and validation data were previously inspected; not pristine holdout']
    if any(row['summary']['netPnl'] <= 0 for row in zero):
        reasons.append('At least one seed does not outperform the zero-PnL Wait baseline')
    diagnostic = dict(schema='nikkei-rl-feedback-diagnosis-v1', deploymentEligible=False,
                      candidateAccepted=False, reasons=reasons,
                      nextOfflineResearch=['Collect unseen audited sessions before further tuning',
                                           'Analyze cost, missed-fill and drawdown transitions',
                                           'Train new candidates in batches and validate frozen models'],
                      onlineWeightUpdates=False, automaticModelSwitch=False)
    (run / 'feedback-diagnosis.json').write_text(json.dumps(diagnostic, indent=2), encoding='utf-8')
    primary = ['## 固定 1 ms 交易延遲績效', '',
        '| Seed | 勝率 | 毛損益 JPY | 成本 JPY | 淨損益 JPY | 交易次數 | 平均獲利 JPY | 平均損失 JPY |',
        '|---|---:|---:|---:|---:|---:|---:|---:|']
    def display(value):
        return '—' if value is None else f'{value:,.2f}'
    for m in metrics:
        primary.append(f"| {m['seed']} | {display(m['winRatePercent'])}% | {m['grossPnlJPY']:,.0f} | {m['costsJPY']:,.0f} | {m['netPnlJPY']:,.0f} | {m['completedTrades']} | {display(m['averageWinningNetJPY'])} | {display(m['averageLosingNetJPY'])} |")
    primary += ['', '交易次數為完整進出一趟；勝率以扣除成本後淨獲利大於零計算。平均獲利／損失亦為每趟扣成本後金額，損失保留負號；沒有相應交易則顯示 —。毛損益為扣成本前價差損益，淨損益＝毛損益－成本。', '',
        '1 ms 固定延遲套用模型進場、平倉與撤單動作；停損及到期／收盤風控由環境持續執行。既有模型是在 0 ms 環境訓練，本次凍結模型以 1 ms 評估，未重新訓練。', '',
        '## 參數設定', '', '| 項目 | 設定 |', '|---|---|',
        '| 商品／參考商品 | 225MC／225M、225 |', '| 交易延遲／決策週期 | 1 ms／5 秒 |',
        '| 數量／每點價值／最小跳動 | 1 口／10 JPY／5 點 |', '| 每邊佣金／額外成本 | 36 JPY／0 跳 |',
        '| 停損／最長持有／冷卻 | 6 跳／60 秒／30 秒 |', '| 收盤緩衝 | 撮合收盤前 310 秒 |',
        '| 回撤 reward 懲罰係數 | 0.25 |', '| PPO 架構 | actor／critic [64,64]，Tanh |',
        '| learning rate／n_steps／batch size／epochs | 0.0003／2048／64／10 |',
        '| gamma／GAE lambda／clip／entropy／target KL | 0.99／0.95／0.2／0.01／0.02 |',
        '| 訓練步數／seeds | 每個 20,480／11、22、33、44、55 |',
        '| 訓練／驗證資料 | 9/28 10:01:45 後段／9/29 日盤 |', '']
    lines = ['# 日經微型 225MC 強化學習回測結果', '',
             '工程回測已完成；研究驗證仍不足，候選模型不予採用。這些資料已於先前研究使用，不能宣稱全新樣本外績效。', '',
             '使用 9/28 10:01:45 後段訓練、9/29 日盤驗證；9/28 10:00:40／45 原始事件中斷區段及其後 60 秒暖機區段已排除。10/1 有 109 筆時間倒序，整日排除。', '',
             'MaskablePPO、actor／critic 各兩層 64 單元，每 seed 20,480 步，共五個 seed。每 5 秒決策，動作為等待、做多、做空、平倉、撤進場單。', '',
             '反饋為共用帳本淨權益變化／每跳價值，扣除 0.25 倍新增回撤跳值。逐步 gzip JSONL 保存狀態、合法動作遮罩、動作、成交、成本及獎勵；PPO 依目前策略軌跡更新，驗證階段凍結權重。', '',
             '每邊佣金 36 JPY、5 指數點一跳、每點 10 JPY、固定停損 6 跳、最長持有 60 秒、冷卻 30 秒。進場使用 Bid／Ask 限價與共用嚴格穿價成交。', '',
             *primary,
             '## 延遲敏感度比較（無額外成本）', '',
             '| Seed | 完整交易（0 ms） | 淨損益 0 ms JPY | 100 ms JPY | 250 ms JPY | 最大回撤（0 ms）JPY |',
             '|---|---:|---:|---:|---:|---:|']
    for row in zero:
        seed = row['seed']
        peers = {r['latencyMs']: r['summary'] for r in report['scenarios']
                 if r['seed'] == seed and r['extraCostTicksPerSide'] == 0}
        summary = row['summary']
        lines.append(f"| {seed} | {summary['completedTrades']} | {summary['netPnl']:.0f} | {peers[100]['netPnl']:.0f} | {peers[250]['netPnl']:.0f} | {summary['maximumEpisodeDrawdown']:.0f} |")
    lines += ['', '## 基準策略', '', '| 策略（0 ms／無額外成本） | 完整交易 | 淨損益 JPY | 最大回撤 JPY |', '|---|---:|---:|---:|']
    for row in baselines:
        if row['latencyMs'] == 0 and row['extraCostTicksPerSide'] == 0:
            s = row['summary']
            lines.append(f"| {row['baseline']} | {s['completedTrades']} | {s['netPnl']:.0f} | {s['maximumEpisodeDrawdown']:.0f} |")
    lines += ['', '趨勢基準只比較三個商品過去 60 秒報酬同向，使用相同固定風控與成交帳本；未用驗證結果調參。', '',
              '## C# 推論與延遲', '', '| Seed | 快照數 | 暖機 p50 ms | p95 ms | p99 ms | 最大 ms | 冷載入 ms | 固定 1 ms 回測淨損益 JPY |',
              '|---|---:|---:|---:|---:|---:|---:|---:|']
    for row in measured:
        seed = row['seed']
        timing = json.loads((run / f'latency-seed{seed}.json').read_text())
        if not timing['allActionsMatch'] or timing['maxProbabilityError'] > 1e-6:
            raise ValueError('Parity failed')
        lines.append(f"| {seed} | {timing['count']} | {timing['p50Ms']:.4f} | {timing['p95Ms']:.4f} | {timing['p99Ms']:.4f} | {timing['maxMs']:.4f} | {timing['coldLoadMs']:.2f} | {row['summary']['netPnl']:.0f}（{row['latencyMs']} ms） |")
    lines += ['', '每個模型 1,200 筆快照的 Python／C# 動作全部一致，機率最大誤差低於 1e-6。ONNX 內嵌正規化；其他 C# 程式可引用 NikkeiRlModel 函式庫。', '',
              '推論計時僅包含正規化、ONNX、遮罩與 softmax；不含特徵更新、排程、行情接收、券商與網路。衍生 cache 特徵時間尚未量測，不能把 actor p99 當端到端延遲。冷載入應在開始交易前完成。', '',
              '100／250 ms 是壓力情境。延遲會移動整個動作送出時間並重播其間行情，可能改變成交與後續決策；相同 tape 下結果不必單調變差。每邊額外 1／2 跳是成本敏感度，不代表已模擬排隊、市場衝擊或完整價格滑價。', '',
              '## 成本敏感度（0 ms）', '', '| Seed | 額外 0 跳 JPY | 額外 1 跳／邊 JPY | 額外 2 跳／邊 JPY |', '|---|---:|---:|---:|']
    for row in zero:
        peers = {r['extraCostTicksPerSide']: r['summary']['netPnl'] for r in report['scenarios']
                 if r['seed'] == row['seed'] and r['latencyMs'] == 0}
        lines.append(f"| {row['seed']} | {peers[0]:.0f} | {peers[1]:.0f} | {peers[2]:.0f} |")
    lines += ['', '成本也在狀態中，因此不同成本情境可能使模型採取不同動作；不是固定成交清單直接扣費。', '',
              '## 收盤與資料限制', '',
              '依 [JPX 交易時間](https://www.jpx.co.jp/english/derivatives/rules/trading-hours/)：日盤連續交易到日本時間 15:40，15:45 收盤撮合；夜盤連續交易到 05:55，06:00 收盤撮合。工具在連續交易停止前 10 秒（台北 14:39:50／04:54:50）禁止進場並平倉。檔案提前結束或尾端未平倉會標記不完整，不虛構成交。特殊交易日仍須正式交易日曆。', '',
              '本次所有驗證情境都已完整結束，但只有一個開發驗證日，未達 20 日／200 筆全新樣本外門檻。模型未接入主程式策略 UI、實盤或自動換版；診斷檔僅提出離線研究下一步。', '',
              '完整數據：backtest-report.json（45 情境）、baselines.json（18 情境）、measured-latency-backtest.json（5 情境）、latency-seed*.json、execution-provenance.json、feedback-diagnosis.json。']
    (run / 'research-report.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
