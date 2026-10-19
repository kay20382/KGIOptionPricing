"""Audit prespecified feature ablations against ledger fills and daily gates."""
import argparse
import csv
import gzip
import hashlib
import json
import math
from pathlib import Path
import numpy as np
from feature_experiments import COHORT, TRAIN, SEEDS, MASKS, combination_eligible
from performance_metrics import episode_metrics, completed_trade_counts, aggregate_round_trips, daily_targets
from feedback import verify_manifest
from fill_diagnostics import reconcile_episode, validate_daily_keys
from observation_schema import feature_names


def sha(path):
    digest=hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda:source.read(1024*1024),b''):
            digest.update(block)
    return digest.hexdigest().upper()


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',required=True)
    parser.add_argument('--suite',choices=['features','papers'],default='features')
    parser.add_argument('--baseline')
    args=parser.parse_args()
    root=Path(args.root)
    paper=args.suite=='papers'
    if paper and not (root/'latency-sensitivity.json').exists():
        raise ValueError('Paper suite requires frozen P1 sensitivity evidence')
    masks={'B0':[],'C0':list(range(40,48)),'P1':list(range(44,48)),'P2':list(range(40,44))} if paper else MASKS
    control_variant='C0' if paper else 'B1'
    candidates=['P1','P2'] if paper else ['F1','F2','F3','F4']
    baseline_directory=Path(args.baseline) if args.baseline else root/'B0-40'
    dataset_file='dataset.json' if paper else 'dataset-v3-verified.json'
    parents_file='parents.json' if paper else 'dataset-v1.json'
    daily=[];audits=[];evidence={};baseline_inputs=None;cache_evidence={};exit_diagnostics=[]
    dataset=json.loads((root/dataset_file).read_text())
    parents=json.loads((root/parents_file).read_text())
    for episode,path in dataset.items():
        path=Path(path);manifest=json.loads(Path(str(path)+'.manifest.json').read_text())
        expected_names=feature_names(48)[40:] if paper else feature_names(50)[40:48]
        if manifest['schema']!=('nikkei-rl-cache-v4' if paper else 'nikkei-rl-cache-v3') or manifest['marketExtraNames']!=expected_names:
            raise ValueError('Wrong cache feature schema')
        if manifest['cacheSha256']!=sha(path) or not manifest['legacyParityVerified']:
            raise ValueError('Unverified feature cache')
        if sha(Path(manifest['inputPath']))!=manifest['inputSha256']:
            raise ValueError('Raw data changed')
        if manifest['parentCacheSha256']!=sha(Path(parents[episode])) or any(manifest[k] for k in ['badRows','outOfOrder','dropped','dataGapFrames']):
            raise ValueError('Cache quality failed')
        if paper and not 0<=manifest['paperDerivativeAnchorValidFrames']<=manifest['paperHealthyValidFrames']<=manifest['validFrames']:
            raise ValueError('Invalid paper feature availability')
        cache_evidence[episode]=manifest
    for variant in masks:
        directory=baseline_directory if variant=='B0' else root/('models' if paper else 'verified')/variant
        size=40 if variant=='B0' else 48 if paper else 50
        report=json.loads((directory/'backtest-report.json').read_text())
        config=dict(cooldownSeconds=10,decisionLatencyMs=1,rewardCostWeight=0)
        if variant!='B0': config['observationSize']=size
        if report['environmentConfig']!=config or report['actorExcludedFeatures']!=masks[variant]:
            raise ValueError('Experiment configuration changed')
        if report['train']!=TRAIN or report['validation']!=COHORT or report['directionWarmstartEpochs']!=20:
            raise ValueError('Experiment split or warmstart changed')
        if report['requestedSteps']!=102400 or [r['seed'] for r in report['training']]!=SEEDS or any(r['actualSteps']!=102400 for r in report['training']):
            raise ValueError('Missing seed/steps')
        if len(report['scenarios'])!=3: raise ValueError('Missing scenarios')
        labels=json.loads((directory/'direction-training.json').read_text())
        if labels['npzSha256']!=sha(directory/'direction-training.npz') or [r['episode'] for r in labels['episodes']]!=TRAIN:
            raise ValueError('Warmstart provenance changed')
        samples=np.load(directory/'direction-training.npz')
        if paper and variant!='B0':
            probe=np.load(root/'training-probe-verified/direction-training.npz')
            if not np.array_equal(samples['observation'],probe['observation']) or not np.array_equal(samples['action'],probe['action']):
                raise ValueError('Paper training input differs from prespecified probe')
        if samples['observation'].shape[1]!=size: raise ValueError('Wrong warmstart shape')
        if baseline_inputs is None:
            baseline_inputs=(samples['observation'].copy(),samples['action'].copy())
        elif not np.array_equal(samples['observation'][:,:40],baseline_inputs[0]) or not np.array_equal(samples['action'],baseline_inputs[1]):
            raise ValueError('Original warmstart inputs/labels changed')
        sources=parents if variant=='B0' else dataset
        if any(labels['cacheSha256'][e]!=sha(Path(sources[e])) for e in TRAIN):
            raise ValueError('Training cache changed')
        evidence[variant]=dict(environmentConfig=config,actorExcludedFeatures=masks[variant],
            featureNames=feature_names(size),training=report['training'],labels=labels,models=[])
        for scenario in report['scenarios']:
            seed=scenario['seed']
            if scenario['latencyMs']!=1 or scenario['extraCostTicksPerSide']!=0 or [r['episode'] for r in scenario['episodes']]!=COHORT:
                raise ValueError('Execution scenario changed')
            model=directory/f'policy-seed{seed}.onnx'
            manifest=json.loads(model.with_suffix('.manifest.json').read_text())
            verify_manifest(model,manifest)
            if manifest['observationSize']!=size or manifest['environmentConfig']!=config or manifest['actorExcludedFeatures']!=masks[variant]:
                raise ValueError('Model configuration mismatch')
            if variant!='B0' and manifest['featureNames']!=feature_names(size): raise ValueError('Feature names mismatch')
            timing=json.loads((directory/f'latency-seed{seed}.json').read_text())
            if timing['modelSha256']!=sha(model) or timing['manifestSha256']!=sha(model.with_suffix('.manifest.json')) or timing['paritySha256']!=sha(directory/f'parity-seed{seed}.json'):
                raise ValueError('C# parity belongs to a different model bundle')
            if timing['count']!=1200 or timing['observationSize']!=size or not timing['allActionsMatch'] or timing['maxProbabilityError']>1e-6:
                raise ValueError('C# parity failed')
            evidence[variant]['models'].append(dict(seed=seed,parity=timing,
                hashes={p.name:sha(p) for p in [model,model.with_suffix('.manifest.json'),directory/f'ppo-seed{seed}.zip',directory/f'normalizer-seed{seed}.pkl',directory/f'parity-seed{seed}.json']}))
            totals={}
            with gzip.open(directory/f'feedback-seed{seed}-delay1-cost0.jsonl.gz','rt',encoding='utf-8') as source:
                for line in source:
                    transition=json.loads(line);end=transition['next'];episode=transition['episode']
                    total=totals.setdefault(episode,dict(delta=0,reward=0,transitions=0,fills=[]))
                    total['delta']+=end['deltaEquity'];total['reward']+=end['reward'];total['transitions']+=1
                    total['fills'].extend(end['fills']);total['last']=end
            if sorted(totals)!=COHORT: raise ValueError('Missing feedback dates')
            for episode in scenario['episodes']:
                metric=episode_metrics(episode,10);metric.update(completed_trade_counts(metric))
                if metric['costsJPY']!=metric['completedTrades']*72: raise ValueError('Fee mismatch')
                row=dict(variant=variant,seed=seed,episode=episode['episode'],**metric)
                row.update(daily_targets(row));daily.append(row)
                total=totals[episode['episode']];end=total.pop('last')
                if not math.isclose(total['delta'],end['netPnl'],abs_tol=1e-6) or not math.isclose(total['reward'],end['totalReward'],abs_tol=1e-6):
                    raise ValueError('Feedback ledger/reward mismatch')
                outcomes=reconcile_episode(episode,end,total.pop('fills'))
                for role in ['Flatten','StopLoss','TakeProfit']:
                    group=[r for r in outcomes if r['exitRole']==role]
                    if group:
                        exit_diagnostics.append(dict(variant=variant,seed=seed,episode=episode['episode'],exitRole=role,
                            trades=len(group),grossPnlJPY=sum(r['grossPnlJPY'] for r in group),
                            netPnlJPY=sum(r['netPnlJPY'] for r in group),holdingSeconds=sum(r['holdingSeconds'] for r in group)))
                audits.append(dict(variant=variant,seed=seed,episode=episode['episode'],**total,fillTrades=len(outcomes)))
    validate_daily_keys(daily,list(masks),SEEDS,COHORT)
    summaries=[];comparisons=[]
    for variant in masks:
        for seed in SEEDS:
            rows=[r for r in daily if r['variant']==variant and r['seed']==seed]
            summaries.append(dict(variant=variant,seed=seed,**aggregate_round_trips(rows),
                initialPassedDays=sum(r['initialTargetPassed'] for r in rows),advancedPassedDays=sum(r['advancedTargetPassed'] for r in rows)))
        if variant in candidates:
            for row in [r for r in daily if r['variant']==variant]:
                control=next(r for r in daily if r['variant']==control_variant and r['seed']==row['seed'] and r['episode']==row['episode'])
                comparisons.append(dict(variant=variant,seed=row['seed'],episode=row['episode'],
                    grossDeltaJPY=row['grossPnlJPY']-control['grossPnlJPY'],netDeltaJPY=row['netPnlJPY']-control['netPnlJPY'],
                    tradesDelta=row['completedTrades']-control['completedTrades'],
                    winRateDeltaPercent=None if row['winRatePercent'] is None or control['winRatePercent'] is None else row['winRatePercent']-control['winRatePercent']))
    eligible=[v for v in candidates if combination_eligible(daily,v)]
    result=dict(perDate=daily,summaries=summaries,versusB1=comparisons,experiment=evidence,cache=cache_evidence,exitDiagnostics=exit_diagnostics,
        combinationEligibleGroups=eligible,usage='DevelopmentPreviouslyInspected',deploymentEligible=False,
        targets=dict(dailyTradesExclusive=100,netWinRatePercentExclusive=33,initialGrossExclusive=0,advancedNetExclusive=0))
    if paper:
        result['versusC0']=result.pop('versusB1')
        result['removedActiveFeatures']=feature_names(50)[40:]
        result['trainingInputAudit']=json.loads((root/'training-input-audit.json').read_text())
        result['selectedFeatureGroups']=[]
        result['researchReferenceObservationSize']=40
        result['selectionStatus']='CombinationResearchRequired' if eligible else 'NoPaperCandidatePassed'
    (root/'performance-comparison.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    (root/'feedback-audit.json').write_text(json.dumps(audits,indent=2),encoding='utf-8')
    with (root/'performance-per-date.csv').open('w',encoding='utf-8-sig',newline='') as target:
        writer=csv.DictWriter(target,fieldnames=list(daily[0]));writer.writeheader();writer.writerows(daily)
    fmt=lambda v:'—' if v is None else f'{v:,.2f}'
    lines=['# 225MC 論文特徵淘汰與回測' if paper else '# 225MC 強化學習特徵消融回測','',
        '冷卻固定 10 秒、交易動作延遲 1 ms。每日初步目標：完整進出 >100 筆、淨獲利勝率 >33%、毛損益 >0；進階另須淨損益 >0。','',
        ('B0：原 40 維；C0：48 維新增輸入全遮罩；P1：第二至第五檔 MLOFI；P2：五檔平均深度與變化率。上一輪 10 個增量特徵已從新模型／cache 移除，歷史 50 維模型保留。各 seeds 11／22／33、102400 PPO 步、方向預訓練 20 epochs。' if paper else 'B0：原 40 維；B1：50 維但新增輸入全遮罩；F1：三商品 OFI；F2：大日經／迷你相對微型領先報酬；F3：報價分類成交量、波動、價差變化；F4：持倉至當下 MFE／MAE。各組 seeds 11／22／33、102400 PPO 步、20 epochs 方向預訓練。'),' ',
        ('訓練日期為 9/28 後段、9/29、10/5 後段，驗證 10/6／10/7 已檢視完整日盤。新 8 維僅按訓練資料檢查變異；未刪除未經個別消融的原 40 維。' if paper else '訓練日期為 9/28 中斷後、9/29、10/5 中斷後；驗證為 10/6、10/7 完整日盤，兩日已於先前研究檢視。未完成舊特徵健康條件的初版實驗已中止，根目錄 B1/F1/F2/F3/F4 為 superseded；本報告只使用 verified/ 的新版結果及 B0-40。'),' ',
        '## 日別績效','',
        '| 組別／Seed／日期 | 交易筆數 | 勝率 | 毛損益 JPY | 淨損益 JPY | 平均獲利 JPY | 平均損失 JPY | 初步／進階 |',
        '|---|---:|---:|---:|---:|---:|---:|---|']
    for r in daily:
        lines.append(f"| {r['variant']}／{r['seed']}／{r['episode'][:8]} | {r['completedTrades']} | {fmt(r['winRatePercent'])}% | {r['grossPnlJPY']:,.0f} | {r['netPnlJPY']:,.0f} | {fmt(r['averageWinningNetJPY'])} | {fmt(r['averageLosingNetJPY'])} | {'是' if r['initialTargetPassed'] else '否'}／{'是' if r['advancedTargetPassed'] else '否'} |")
    lines+=['','## 相同 48 維控制組比較' if paper else '## 相同 50 維控制組比較','',
        ('下表跨三個種子與兩日期累加，只用於比較；P1／P2 以相同 48 維 C0 控制組衡量，不取代逐日門檻。' if paper else '下表跨三個種子與兩日期累加，只用於消融比較，不取代逐日門檻。B0 與 B1 網路輸入形狀不同，因此新增特徵效果以 F1–F4 對 B1 衡量。'),' ',
        f'| 組別 | 完整交易 | 毛損益 JPY | 淨損益 JPY | 對 {control_variant} 毛差額 | 對 {control_variant} 淨差額 | 初步達標日 |',
        '|---|---:|---:|---:|---:|---:|---:|']
    control=aggregate_round_trips([r for r in daily if r['variant']==control_variant])
    for v in masks:
        rows=[r for r in daily if r['variant']==v];a=aggregate_round_trips(rows)
        lines.append(f"| {v} | {a['completedTrades']} | {a['grossPnlJPY']:,.0f} | {a['netPnlJPY']:,.0f} | {a['grossPnlJPY']-control['grossPnlJPY']:,.0f} | {a['netPnlJPY']-control['netPnlJPY']:,.0f} | {sum(r['initialTargetPassed'] for r in rows)}/6 |")
    lines+=['','## 真實成交出場診斷','',
        '| 組別 | 出場原因 | 筆數 | 毛損益 JPY | 淨損益 JPY | 平均持有秒 |','|---|---|---:|---:|---:|---:|']
    for v in masks:
        for role in ['Flatten','StopLoss','TakeProfit']:
            group=[r for r in exit_diagnostics if r['variant']==v and r['exitRole']==role]
            if group:
                count=sum(r['trades'] for r in group)
                lines.append(f"| {v} | {role} | {count} | {sum(r['grossPnlJPY'] for r in group):,.0f} | {sum(r['netPnlJPY'] for r in group):,.0f} | {sum(r['holdingSeconds'] for r in group)/count:.2f} |")
    timings=[m['parity'] for e in evidence.values() for m in e['models']]
    lines+=['','## 參數與驗證','',
        '225MC 1 口，每點 10 JPY、每跳 5 點、每邊佣金 36 JPY；決策間隔 5 秒、停損 6 跳、最長持有 60 秒、收盤緩衝 310 秒。成交採共用 paper-strict-cross 與帳本。毛損益＝淨損益＋佣金；勝率與平均獲利／損失均以扣佣金後的完整交易計算。','',
        'PPO：[64,64] Tanh、learning rate 0.0003、n_steps 2048、batch 64、epochs 10、gamma 0.99、GAE 0.95、clip 0.2、entropy 0.01、target KL 0.02。方向預訓練 horizon 30 秒、門檻正負 2 跳，Adam 0.001、batch 256、gradient clip 0.5。reward 成本權重 0、淨權益回撤懲罰 0.25；真實績效照扣全部費用。','',
        ('新 48 維只有原 40 維與 8 個論文特徵，沒有舊 MFE／MAE 等增量。特徵使用當下收到的事件，方向未來標籤只用於訓練，normalizer 不擬合驗證日期。' if paper else '預訓練使用等待軌跡，因此 MFE／MAE 在預訓練中皆為零；F4 的持倉路徑訊號只能在 PPO 實際持倉回饋階段學習。所有特徵只使用當下已收到的事件，方向未來標籤只用於訓練。'),'',
        f"五份原始資料與快取 SHA 核對通過；{'v4' if paper else 'v3'} 原 30 維、frame、trade 逐一與 parent 完全相同。各組預訓練前 40 維與標籤逐值一致。{len(masks)*3} 模型各 1200 快照，C#／Python 動作一致、機率誤差 <1e-6；{len(audits)} 組日別成交／帳本／回饋互核，共 {sum(r['transitions'] for r in audits):,} transitions。",'',
        f"訓練結束後逐模型量測 actor p99 {min(t['p99Ms'] for t in timings):.4f}–{max(t['p99Ms'] for t in timings):.4f} ms，最大 {max(t['maxMs'] for t in timings):.4f} ms；超過 1 ms 共 {sum(t['countAbove1Ms'] for t in timings)}/{sum(t['count'] for t in timings)} 次。包含 normalization／ORT／mask，不含特徵或 broker/network；1 ms 僅為指定回測延遲，實測尖峰可超過。",'',
        '| 日期 | 整體特徵 snapshot p99 ms | 新特徵事件更新 p99 ms（每 1000 件抽樣） |','|---|---:|---:|']
    for e,m in cache_evidence.items():
        lines.append(f"| {e} | {m['featureComputeP99Ms']:.4f} | {m['featureEventUpdateP99Ms']:.4f} |")
    if paper:
        lines+=['','| 日期 | 原有效 frame | 新深度報價有效 | 5 秒歷史 anchor 可用 |','|---|---:|---:|---:|']
        for e,m in cache_evidence.items():
            lines.append(f"| {e} | {m['validFrames']} | {m['paperHealthyValidFrames']} | {m['paperDerivativeAnchorValidFrames']} |")
        lines+=['','以上統計以原有效 frame 為母體，與非零比率不同。缺少新鮮歷史 anchor 時導數填零，零亦可能代表真實無變化；本版沒有另外輸入 missing indicator。']
    lines+=['','## 判斷與下一步','',
        f"初步通過 {sum(r['initialTargetPassed'] for r in daily)}/{len(daily)}，進階通過 {sum(r['advancedTargetPassed'] for r in daily)}/{len(daily)}。預先規定同組至少 2/3 種子各在兩日通過初步門檻才可組合；合格組別：{', '.join(eligible) or '無'}。",'',
        '未過門檻的特徵組不採用，也不依單一最好種子組合。全新完整日資料仍不足，所有模型 deploymentEligible=false；後續需補全新完整日期、量測整段決策流程，並持續檢查成交後逆向選擇。','',
        'C# 推論使用 [ONNX Runtime 官方 API](https://onnxruntime.ai/docs/get-started/with-csharp)。']
    flattened=[r for r in exit_diagnostics if r['variant']=='F1' and r['exitRole']=='Flatten']
    stopped=[r for r in exit_diagnostics if r['variant']=='F1' and r['exitRole']=='StopLoss']
    flat_count=sum(r['trades'] for r in flattened)
    if flat_count and not paper:
        flat_gross=sum(r['grossPnlJPY'] for r in flattened)
        lines+=['',f"本輪 OFI 組一般平倉 {flat_count} 筆，毛損益 {flat_gross:,.0f} JPY，平均每筆 {flat_gross/flat_count:.2f} JPY，低於每趟 72 JPY 費用；停損 {sum(r['trades'] for r in stopped)} 筆，毛損益 {sum(r['grossPnlJPY'] for r in stopped):,.0f} JPY。增加交易與部分勝率改善尚不足以建立可成交優勢。下一輪優先研究成交後不利價格變動與停損風險，不能只增加交易頻率。"]
    if paper:
        lines+=['','## 論文定義及移植限制','',
            'MLOFI 依 Xu／Gould／Howison equations9–12，採 225MC 第二至第五檔 5 秒 rank flow，除以同檔窗口平均深度。FI-2010 Table4 u4／u6 採五檔 bid／ask 平均量 log1p 與 5 秒有限差分；原論文十檔縮為五檔，未虛構真實撤單／下單事件。',
            '[MLOFI 原文](https://arxiv.org/abs/1907.06230)、[FI-2010 原文](https://arxiv.org/abs/1705.03233)。論文為股票資料與價格預測研究，本輪才檢驗 225MC 可成交交易績效；不能套用論文命中率作回測結果。']
        lines+=['','原 MLOFI 研究衡量同期間價格變化，不等於可交易的未來預測。本版以收到的五檔快照計算 flow 代理，不能重建快照之間未提供的交易所事件。訓練資料中第四、第五檔 flow 相關係數約 0.94；此統計僅供診斷，未事後改動預先設定的 mask。']
    sensitivity_path=root/'latency-sensitivity.json'
    if paper and not sensitivity_path.exists():
        raise ValueError('Paper suite requires frozen P1 sensitivity evidence')
    if sensitivity_path.exists():
        sensitivity=json.loads(sensitivity_path.read_text())
        if not sensitivity['frozenPrimaryExactlyEqual'] or len(sensitivity['perDate'])!=12:
            raise ValueError('Incomplete frozen sensitivity evidence')
        if paper and (sensitivity['variant']!='P1' or sensitivity['primaryLatencyMs']!=1 or sensitivity['sensitivityLatencyMs']!=10):
            raise ValueError('Wrong paper sensitivity experiment')
        result['latencySensitivity']=sensitivity
        (root/'performance-comparison.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        lines+=['','## 凍結模型與延遲敏感度','',
            f"{sensitivity['variant']} 三種子凍結重播的 1 ms 逐日結果與主回測完全一致，模型／normalizer SHA 保持不變。另以 10 ms 重播相同日期，固定 10 秒冷卻；下表是 10 ms 減去 1 ms 的差額。此項驗證不參與特徵挑選，主報告仍為 1 ms。",'',
            '| Seed／日期 | 交易差額 | 毛損益差額 JPY | 淨損益差額 JPY |','|---|---:|---:|---:|']
        for r in sensitivity['comparisons']:
            lines.append(f"| {r['seed']}／{r['episode'][:8]} | {r['tradesDelta']} | {r['grossDeltaJPY']:,.0f} | {r['netDeltaJPY']:,.0f} |")
        lines+=['','延遲會改變成交及後續策略回饋，因此差額包含策略路徑改變，不能全數稱為相同委託的純滑價。兩種延遲均使用之後收到行情判斷成交，沒有以當下價格假設立即成交。']
    (root/'research-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(dict(dailyRows=len(daily),models=len(masks)*3,transitions=sum(r['transitions'] for r in audits),combinationEligible=eligible)))


if __name__=='__main__':
    main()
