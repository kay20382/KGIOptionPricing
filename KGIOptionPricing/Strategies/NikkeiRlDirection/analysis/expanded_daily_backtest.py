"""Frozen ONNX replay orchestration and audited per-date reports. No training/live orders."""
import argparse,csv,gzip,hashlib,json,subprocess
from concurrent.futures import ThreadPoolExecutor,as_completed
from pathlib import Path
from completed_daily import completed_segment,aggregate_daily
from fill_diagnostics import equal_number

DATES=['20260928','20260929','20260930','20261002','20261005','20261006','20261007']
GROUPS=['B0','C0','R','N','N5'];SEEDS=[11,22,33]
def sha(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as f:
        for part in iter(lambda:f.read(1048576),b''):h.update(part)
    return h.hexdigest().upper()
def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def write(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',default='docs/superpowers/evidence/nikkei-rl-expanded-20261008');parser.add_argument('--report-only',action='store_true');args=parser.parse_args()
    root=Path(args.root).resolve();evidence=root.parent
    bridge=Path('tools/NikkeiRlReplayBridge/bin/Release/net10.0-windows/NikkeiRlReplayBridge.dll').resolve()
    dirs={'B0':evidence/'nikkei-rl-papers-20261008/B0-40',**{g:evidence/'nikkei-rl-pruning-20261008/models'/g for g in GROUPS[1:]}}
    indexes={d:read(root/'cache'/d/'segment-index.json') for d in DATES}
    dataset={s['id']:s['path'] for idx in indexes.values() for s in idx['segments'] if not s['excluded']}
    if not all(any(k.startswith(d) for k in dataset) for d in DATES):raise ValueError('Date lacks replayable segments')
    dataset_path=root/'dataset.json'
    if dataset_path.exists():
        if read(dataset_path)!=dataset:raise ValueError('Dataset changed')
    else:write(dataset_path,dataset)
    bundles=[p for folder in dirs.values() for p in folder.iterdir() if p.suffix in ['.onnx','.zip','.pkl'] or p.name.endswith('.manifest.json')]
    runtime=[p for p in bridge.parent.iterdir() if p.suffix in ['.dll','.json']]
    source=[*Path('tools/NikkeiRlReplayBridge').glob('*.cs'),*Path('tools/NikkeiRlModel').glob('*.cs'),Path(__file__),Path(__file__).with_name('completed_daily.py'),Path(__file__).with_name('rl_env.py')]
    fingerprint_path=root/'frozen-fingerprints.json'
    before={str(p.resolve()):sha(p) for p in bundles+runtime+source}
    if fingerprint_path.exists():
        if read(fingerprint_path)!=before:raise ValueError('Model/runtime/source changed since replay; use new output root')
    else:write(fingerprint_path,before)
    if not args.report_only:
        def run(g,seed):
            output=root/'evaluations'/f'{g}-seed{seed}';output.parent.mkdir(exist_ok=True)
            with (root/f'{g}-seed{seed}.log').open('x',encoding='utf-8') as log:
                subprocess.run(['dotnet',str(bridge),'evaluate-frozen',str(dirs[g]/f'policy-seed{seed}.onnx'),str(dirs[g]/f'policy-seed{seed}.manifest.json'),str(dataset_path),str(output)],stdout=log,stderr=subprocess.STDOUT,check=True)
            print(f'{g} seed{seed} completed',flush=True)
        with ThreadPoolExecutor(max_workers=3) as pool:
            for task in as_completed([pool.submit(run,g,s) for g in GROUPS for s in SEEDS]):task.result()
    if before!={p:sha(p) for p in before}:raise ValueError('Frozen artifacts changed')
    # Independently verify the source/cache chain, including full-gap parent caches.
    source_hashes={};cache_hashes={}
    for idx in indexes.values():
        p=idx['inputPath'];source_hashes.setdefault(p,sha(p))
        if source_hashes[p]!=idx['inputSha256']:raise ValueError('Input hash mismatch')
        p=idx['parentCache'];cache_hashes[p]=sha(p)
        if cache_hashes[p]!=idx['parentCacheSha256']:raise ValueError('Parent cache hash mismatch')
        for seg in idx['segments']:
            if seg['excluded']:continue
            cache_hashes[seg['path']]=sha(seg['path'])
            if cache_hashes[seg['path']]!=seg['cacheSha256']:raise ValueError('Segment hash mismatch')
    daily=[];details=[];timings=[];transition_count=0
    for g in GROUPS:
        for seed in SEEDS:
            result=read(root/'evaluations'/f'{g}-seed{seed}'/'evaluation.json')
            if result['modelSha256']!=sha(result['model']) or result['manifestSha256']!=sha(result['manifest']) or result['datasetSha256']!=sha(dataset_path):raise ValueError('Replay identity mismatch')
            if set(r['id'] for r in result['results'])!=set(dataset):raise ValueError('Missing segments')
            timings.append(dict(group=g,seed=seed,**result['timing']))
            by_date={d:[] for d in DATES}
            for episode in result['results']:
                if episode['cacheSha256']!=cache_hashes[episode['path']]:raise ValueError('Result/cache identity mismatch')
                if sha(episode['feedbackPath'])!=episode['feedbackSha256']:raise ValueError('Feedback hash mismatch')
                delta=reward=0;fills=[];last=None;steps=0
                with gzip.open(episode['feedbackPath'],'rt',encoding='utf-8') as file:
                    for raw in file:
                        feedback=json.loads(raw)
                        if feedback['episode']!=episode['id'] or feedback['stepId']!=steps:raise ValueError('Feedback sequence mismatch')
                        last=feedback['next'];fills.extend(last['fills']);delta+=last['deltaEquity'];reward+=last['reward'];steps+=1
                if steps!=episode['steps'] or fills!=episode['fills']:raise ValueError('Replay fills mismatch')
                for k in ['netPnl','costs','completedTrades','completedGrossPnl','completedNetPnl','completedCosts','excludedOpenNetPnl','openPositionCosts','unrealizedPnl','winRate','averageWinningPoints','averageLosingPoints','totalReward','position']:
                    if not equal_number(last[k],episode['state'][k]):raise ValueError('Feedback final ledger mismatch: '+k)
                if not equal_number(delta,last['netPnl']) or not equal_number(reward,last['totalReward']):raise ValueError('Raw reward/equity mismatch')
                segment=completed_segment(last,fills);by_date[episode['date']].append(segment)
                details.append(dict(group=g,seed=seed,id=episode['id'],date=episode['date'],status=last['status'],start=episode['start'],end=episode['end'],**segment))
                transition_count+=steps
            model_manifest=read(result['manifest']);training_dates={x[:8] for x in model_manifest['trainingEpisodes']};cutoff=max(training_dates)
            for d in DATES:
                row=aggregate_daily(by_date[d]);idx=indexes[d]
                usage='TrainingDateReplay' if d in training_dates else 'RetrospectiveBeforeTrainingCutoff' if d<=cutoff else 'DevelopmentPreviouslyInspected'
                eligible=idx['fullDayCoverage'] and d>cutoff
                trades_pass=row['trades']>100;win_pass=(row['winRatePercent'] or 0)>33
                daily.append(dict(group=g,seed=seed,date=d,usage=usage,fullDayCoverage=idx['fullDayCoverage'],targetEligible=eligible,
                    first=idx['first'],last=idx['last'],dataGapFrames=idx['dataGapFrames'],segments=len(by_date[d]),
                    initialTarget=eligible and trades_pass and win_pass and row['grossPnlJPY']>0,
                    advancedTarget=eligible and trades_pass and win_pass and row['grossPnlJPY']>0 and row['netPnlJPY']>0,**row))
    if len(daily)!=105 or len({(r['group'],r['seed'],r['date']) for r in daily})!=105:raise ValueError('Daily keys mismatch')
    summary=dict(dates=DATES,models=15,segments=len(dataset),dailyRows=105,feedbackTransitions=transition_count,initialPass=sum(r['initialTarget'] for r in daily),advancedPass=sum(r['advancedTarget'] for r in daily),deploymentEligible=False,
                 excludedPositionCount=sum(r['excludedOpenPositions'] for r in daily),excludedEntryCostsJPY=sum(r['excludedEntryCostsJPY'] for r in daily),
                 accounting='Report completed cycles only. Both open gains and losses plus their actual entry fees excluded symmetrically. Raw ledger/reward retained; no fabricated terminal exit.',
                 warning='All models previously selected/inspected. Training and retrospective dates are not pristine out-of-sample. Counts across 15 models are audit workload, not one trading strategy.',
                 unavailableDates=[dict(date='20261001',reason='109 out-of-order in-session source events; strict quality rejection'),dict(date='20261008',reason='Recorder file 189 bytes; startup metadata only, no market data')])
    write(root/'daily-performance.json',dict(summary=summary,parameters=result['config'],timings=timings,dataCoverage=indexes,daily=daily,segments=details))
    with (root/'daily-performance.csv').open('w',encoding='utf-8-sig',newline='') as file:
        writer=csv.DictWriter(file,fieldnames=list(daily[0]));writer.writeheader();writer.writerows(daily)
    fmt=lambda x:'—' if x is None else f'{x:,.2f}'
    lines=['# 日經微型 225MC：7 日凍結模型回測','',
           '涵蓋 2026/9/28、9/29、9/30、10/2、10/5、10/6、10/7。共 15 個既有 ONNX 模型，105 筆逐日結果；沒有重新訓練。',
           '',f'初步目標通過 {summary["initialPass"]} 筆，進階目標通過 {summary["advancedPass"]} 筆（僅完整且晚於訓練截止日的日期可計入）。模型仍不符合部署條件。',
           '', '交易延遲 1ms、冷卻 10 秒、決策間隔 5 秒、每次 1 口；停損 6 ticks、最長持有 60 秒；tick 5 點、每點 10 JPY；每邊費用 36 JPY、完成一筆成本 72 JPY。',
           '', '資料中斷時分段重播，恢復後重新暖機 60 秒。各段最後未完成持倉的浮盈、浮虧與進場費用皆排除。原始損益／獎勵／成交明細保留於 feedback，沒有虛構平倉。平均獲利／損失與勝率均以扣費後已完成交易計算；交易筆數是一進一出的完成交易。',
           '', '完整日判定為快取首筆不晚於 07:46:05、末筆不早於 14:44:55、無接收缺口且單一連續段。報告同時列出健康影格數，完整涵蓋不等於所有影格皆可進場。',
           '', '訓練日與訓練截止日前的歷史重播不可視為全新樣本外驗證。10/6、10/7 亦已反覆檢視。本輪維持既有凍結模型，避免利用新增日期重選參數。10/1 有 109 筆時間倒序事件，拒絕回測；10/8 只有啟動紀錄，未產生績效列。',
           '', '## 每日資料範圍', '', '| 日期 | 首筆 | 末筆 | 缺口影格 | 重播段數 | 涵蓋整日 |', '|---|---|---|---:|---:|---|']
    for d,idx in indexes.items():lines.append(f'| {d} | {idx["first"][11:19]} | {idx["last"][11:19]} | {idx["dataGapFrames"]} | {sum(not s["excluded"] for s in idx["segments"])} | {"是" if idx["fullDayCoverage"] else "否"} |')
    labels={'TrainingDateReplay':'訓練日重播','RetrospectiveBeforeTrainingCutoff':'訓練截止日前歷史重播','DevelopmentPreviouslyInspected':'已檢視開發日'}
    for d in DATES:
        lines+=['',f'## {d} 每日績效（JPY）','',f'用途：{labels[next(r["usage"] for r in daily if r["date"]==d)]}。B0 為原始 40 特徵；C0/R/N/N5 為前輪 56 特徵消融模型。',
                '', '| 模型／種子 | 交易筆數 | 勝率 % | 毛損益 | 淨損益 | 平均獲利 | 平均損失 | 排除持倉數 | 排除浮損益 | 排除進場費 |', '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
        for r in [r for r in daily if r['date']==d]:lines.append(f'| {r["group"]}/{r["seed"]} | {r["trades"]} | {fmt(r["winRatePercent"])} | {fmt(r["grossPnlJPY"])} | {fmt(r["netPnlJPY"])} | {fmt(r["averageWinJPY"])} | {fmt(r["averageLossJPY"])} | {r["excludedOpenPositions"]} | {fmt(r["excludedUnrealizedJPY"])} | {fmt(r["excludedEntryCostsJPY"])} |')
    lines+=['','## C# 推論耗時','','| 模型／種子 | p99 ms | 最大 ms | 超過 1ms 次數 |','|---|---:|---:|---:|']
    for t in timings:lines.append(f'| {t["group"]}/{t["seed"]} | {t["p99Ms"]:.4f} | {t["maxMs"]:.4f} | {t["countAbove1Ms"]} |')
    lines+=['', '以上為 actor 正規化、ONNX、遮罩推論時間，不包含特徵擷取、排程與網路／券商時間；回測固定套用 1ms，不因本次電腦量測值而事後更改成交。完整 end-to-end 耗時尚未驗證。',
            '',f'共核對 {transition_count:,} 筆 feedback 轉移，已完成成交與 C# 共用帳本損益一致。各模型、快取、原始資料、執行 DLL 與程式 SHA 保留於 frozen-fingerprints.json／verification.json。未下實單、未提交或推送 Git。']
    (root/'daily-performance.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    write(root/'verification.json',dict(summary=summary,sourceHashes=source_hashes,cacheHashes=cache_hashes,frozenArtifactsUnchanged=True,sharedLedgerFillsReconciled=True,rawFeedbackRewardEquityReconciled=True,review='Pending'))
    print(json.dumps(summary,ensure_ascii=False),flush=True)

if __name__=='__main__':main()
