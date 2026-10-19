"""Incremental frozen-model research replay. No training or broker calls."""
import argparse
import csv
import hashlib
import json
import re
import shutil
import subprocess
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, time, timedelta, timezone
from pathlib import Path

from expanded_daily_backtest import sha, read
from constrained_objective import assess_day, rank_candidates, TARGET

TZ = timezone(timedelta(hours=8))
GROUPS = ['baseline', 'book', 'flow', 'price', 'all']
SEEDS = [11, 22, 33]
COMPACT_BRIDGE = Path('C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-rl-entry-mode-20261009/runtime/CompactBridge.dll')
READINESS_BRIDGE = Path('C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-rl-cache-isolation-20261009/runtime/ReadinessBridge.dll')
PATTERN = re.compile(r'^(\d{8})_nikkei_.+\.jsonl$')


def write(path, value):
    path = Path(path)
    temp = path.with_name(path.name + '.tmp')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    temp.replace(path)


def discover(roots, start):
    dates = {}
    for root in roots:
        for p in Path(root).glob('*_nikkei_*.jsonl'):
            match = PATTERN.match(p.name)
            if match and match[1] >= start:
                datetime.strptime(match[1], '%Y%m%d')
                dates.setdefault(match[1], set()).add(p.resolve())
    return {d: sorted(paths) for d, paths in sorted(dates.items())}


def signature(paths):
    stats = [(str(p), p.stat().st_size, p.stat().st_mtime_ns) for p in paths]
    return hashlib.sha256(json.dumps(stats).encode()).hexdigest()


def needs_processing(entry, sig):
    return not entry or entry['signature'] != sig or entry['status'] in ['Failed', 'Deferred']


def date_usage(date, frozen_date):
    return 'FutureFrozenEvaluation' if date > frozen_date else 'NewDateRetrospective'


def pending_recording_dates(roots, historical, registry_dates, include_dates=None):
    covered={row['date'] for row in historical}
    start=min(covered,default='00000000')
    return {date:sources for date,sources in discover(roots,start).items()
            if (date not in covered or date in registry_dates) and (include_dates is None or date in include_dates)}


def pin_coverage_index(path, date):
    path=Path(path).resolve();expected=sha(path);index=read(path)
    if sha(path)!=expected:raise ValueError('完整日索引讀取期間變動')
    if index['date']!=date or not isinstance(index['fullDayCoverage'],bool):raise ValueError('完整日索引日期／型別不符')
    return index,{str(path):expected}


def verify_coverage_index(pin):
    if not pin or any(sha(path)!=expected for path,expected in pin.items()):
        raise ValueError('完整日索引指紋變動，結果不升級')


def pin_clock_mode(root,mode):
    if mode not in ['wall','monotonic']:raise ValueError('未知研究時間軸')
    path=Path(root)/'execution-clock.json';config=dict(researchClockMode=mode)
    if path.exists():
        if read(path)!=config:raise ValueError('研究時間軸變動；請建立新輸出根目錄')
    else:write(path,config)
    return {str(path.resolve()):sha(path)}


def verify_accepted_date(entry):
    result=Path(entry['version'])/'daily-performance.json'
    if not entry.get('dailyResultSha256') or sha(result)!=entry['dailyResultSha256']:
        raise ValueError('已驗收日期績效指紋變動或缺失')
    verify_coverage_index(entry.get('coverageInputs',{}))


def timestamp_key(value):
    # Python3.10 accepts six fractional digits; retain .NET's seventh for ordering.
    fraction = re.search(r'\.(\d+)', value)
    digits = fraction[1] if fraction else ''
    if len(digits) > 7:
        raise ValueError('行情時間精度超過.NET ticks')
    normalized = re.sub(r'\.(\d+)(?=[+-]|Z$)', lambda m: '.'+m[1].ljust(6,'0')[:6], value).replace('Z', '+00:00')
    at = datetime.fromisoformat(normalized)
    if at.tzinfo is None:
        raise ValueError('行情時間缺少時區')
    return at, int(digits.ljust(7, '0')[6])


def extract_day(source, output, date, clock_mode='wall'):
    if clock_mode not in ['wall','monotonic']:raise ValueError('Unknown research clock mode')
    source, output = Path(source), Path(output)
    before = source.stat()
    digest = hashlib.sha256()
    first = last = None
    rows = 0
    products = set()
    frequency=None;anchor=None;previous_monotonic=None;previous_sequence=None;session_count=0
    min_drift=max_drift=None
    with source.open('rb') as reader, output.open('xb') as writer:
        for raw in reader:
            digest.update(raw)
            row = json.loads(raw)
            if row.get('Dropped', 0):
                raise ValueError('Dropped：記錄器遺失事件')
            kind = row.get('Kind')
            if kind=='session' and clock_mode=='monotonic':
                session_count+=1;frequency=row.get('StopwatchFrequency')
                if session_count!=1 or type(frequency) is not int or frequency<=0:raise ValueError('單調時鐘需要單一session及正整數頻率')
            if kind == 'trade' or isinstance(kind, str) and kind.startswith('depth'):
                if row.get('Product') not in ['225MC', '225M']:
                    continue
                key = timestamp_key(row['Time'])
                if clock_mode=='monotonic':
                    counter=row.get('Monotonic');sequence=row.get('Sequence')
                    if frequency is None or type(counter) is not int or type(sequence) is not int:raise ValueError('缺少接收單調時鐘／序號／頻率')
                    if previous_monotonic is not None and (counter<previous_monotonic or sequence<=previous_sequence):raise ValueError('接收單調時鐘或序號倒置，拒絕重建')
                    previous_monotonic=counter;previous_sequence=sequence
                    if anchor is None:anchor=(key,counter,row['Time'])
                    delta_ticks=(counter-anchor[1])*10000000//frequency
                    microseconds,remainder=divmod(anchor[0][0].microsecond*10+anchor[0][1]+delta_ticks,10)
                    corrected=anchor[0][0].replace(microsecond=0)+timedelta(microseconds=microseconds)
                    text=corrected.isoformat(timespec='seconds')
                    corrected_text=text[:19]+f'.{corrected.microsecond:06d}{remainder}'+text[19:]
                    drift=(key[0]-corrected).total_seconds()+(key[1]-remainder)*1e-7
                    min_drift=drift if min_drift is None else min(min_drift,drift);max_drift=drift if max_drift is None else max(max_drift,drift)
                    if 'OriginalTime' in row:raise ValueError('原始記錄已帶重建時間，拒絕重複正規化')
                    row=dict(row,OriginalTime=row['Time'],Time=corrected_text,ResearchClock='MonotonicFirstMarketAnchor')
                    key=(corrected,remainder)
                at = key[0]
                local = at.astimezone(TZ)
                if local.strftime('%Y%m%d') != date or not time(7,45) <= local.time() <= time(14,45):
                    continue
                if last is not None and key < timestamp_key(last):
                    raise ValueError('行情時間順序倒置，拒絕排序修補')
                first = first or row['Time']
                last = row['Time']
                products.add(row['Product'])
                writer.write(raw if clock_mode=='wall' else (json.dumps(row,ensure_ascii=False,separators=(',',':'))+'\n').encode('utf-8'))
                rows += 1
            elif kind not in ['session', 'status', 'contract', 'api_packet']:
                raise ValueError('未知記錄格式：'+str(kind))
    after = source.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError('行情檔仍在寫入，延後重試')
    return dict(source=str(source.resolve()), sourceSha256=digest.hexdigest().upper(),
                path=str(output.resolve()), sha256=sha(output), marketRows=rows,
                first=first, last=last,
                products=sorted(products),clockMode=clock_mode,clockAnchor=anchor[2] if anchor else None,
                stopwatchFrequency=frequency,wallMinusMonotonicMinSeconds=min_drift,wallMinusMonotonicMaxSeconds=max_drift)


def combine_parts(parts, output):
    usable = sorted([p for p in parts if p['marketRows']], key=lambda p: timestamp_key(p['first']))
    for a, b in zip(usable, usable[1:]):
        if timestamp_key(a['last']) >= timestamp_key(b['first']):
            raise ValueError('同日期記錄區間重疊，不能猜測去重或事件順序')
    with Path(output).open('xb') as writer:
        for part in usable:
            with Path(part['path']).open('rb') as reader:
                shutil.copyfileobj(reader, writer)
    return dict(path=str(Path(output).resolve()), sha256=sha(output), marketRows=sum(p['marketRows'] for p in usable))


def run(command, log):
    with Path(log).open('x', encoding='utf-8') as writer:
        subprocess.run(command, stdout=writer, stderr=subprocess.STDOUT, check=True)

def model_entries(models):
    entries=[]
    for group,folder in sorted(models.items()):
        found=[]
        for model in sorted(Path(folder).glob('policy-seed*.onnx')):
            match=re.fullmatch(r'policy-seed(\d+)\.onnx',model.name)
            if not match:raise ValueError('Invalid model name')
            seed=int(match[1]);manifest=model.with_suffix('.manifest.json')
            if read(manifest)['seed']!=seed:raise ValueError('Model seed mismatch')
            found.append((group,seed,model))
        if not found:raise ValueError('Empty model group: '+group)
        entries+=found
    return entries


def readiness_route(meta,version):
    from compact_daily import readiness_contract
    config=meta['environmentConfig']
    readiness_contract(meta,config,dict(config,schema='nikkei-rl-cache-pruned-flow-v1' if config.get('researchReadinessProfile')=='exclude-flow' else 'nikkei-rl-cache-v4'))
    profile=config.get('researchReadinessProfile','all-research')
    pruned=profile=='exclude-flow'
    return dict(profile=profile,bridge=READINESS_BRIDGE if pruned else COMPACT_BRIDGE,
                cacheCommand='cache-evaluation-research-pruned-flow' if pruned else 'cache-evaluation-research',
                cacheDirectory=Path(version)/('cache-exclude-flow' if pruned else 'cache'),
                datasetPath=Path(version)/('dataset-exclude-flow.json' if pruned else 'dataset.json'))

def evaluate_date(experiment, version, date, jobs, models=None):
    from compact_daily import compact_daily_rows
    models = models or {g: experiment/'models'/g for g in GROUPS}
    entries=model_entries(models)
    extraction=read(version/'extraction.json')
    clock_modes={part.get('clockMode','wall') for part in extraction['parts']}
    if len(clock_modes)!=1:raise ValueError('同日來源不可混用研究時間軸')
    clock_mode=next(iter(clock_modes))
    routes={(g,s):readiness_route(read(model.with_suffix('.manifest.json')),version) for g,s,model in entries}
    caches={}
    for route in routes.values():
        profile=route['profile']
        if profile in caches:continue
        bridge=route['bridge']
        if not bridge.is_file():raise ValueError('摘要回測執行器不存在')
        run(['dotnet',str(bridge),route['cacheCommand'],str(version/'market-events.jsonl'),str(route['cacheDirectory']),'3'],version/('cache-'+profile+'.log'))
        index,coverage_pin=pin_coverage_index(route['cacheDirectory']/'segment-index.json',date)
        dataset={s['id']:s['path'] for s in index['segments'] if not s['excluded']}
        if not dataset:raise ValueError('沒有可重播片段：'+profile)
        if any(not key.startswith(date) for key in dataset):raise ValueError('回測日期不符')
        write(route['datasetPath'],dataset)
        caches[profile]=dict(index=index,dataset=dataset,coveragePin=coverage_pin)
    coverage=next(iter(caches.values()))['index']
    for cache in caches.values():
        if any(cache['index'][key]!=coverage[key] for key in ['date','first','last','fullDayCoverage','dataGapFrames']):raise ValueError('不同就緒模式的行情範圍／完整性分類不一致')
    configs = {}
    for cost in [0, 1, 2]:
        configs[cost] = dict(read(experiment/'risk150.json'), extraCostTicksPerSide=cost)
        write(version/f'risk-cost{cost}.json', configs[cost])

    def evaluate(group, seed):
        rows, audits, timings = [], [], []
        model = Path(models[group])/f'policy-seed{seed}.onnx'
        manifest = model.with_suffix('.manifest.json')
        route=routes[(group,seed)];bridge=route['bridge'];dataset_path=route['datasetPath']
        index=caches[route['profile']]['index'];dataset=caches[route['profile']]['dataset']
        entry_mode=read(manifest)['environmentConfig'].get('entryPriceMode','same-side')
        for cost in [0, 1, 2]:
            verify_coverage_index(caches[route['profile']]['coveragePin'])
            folder = version/'evaluations'/f'{group}-seed{seed}-cost{cost}'
            run(['dotnet', str(bridge), 'evaluate-risk-summary', str(model), str(manifest),
                 str(dataset_path), str(version/f'risk-cost{cost}.json'), str(folder)],
                version/f'{group}-seed{seed}-cost{cost}.log')
            verify_coverage_index(caches[route['profile']]['coveragePin'])
            result = read(folder/'evaluation.json')
            if result['modelSha256'] != sha(model) or result['manifestSha256'] != sha(manifest) or result['datasetSha256'] != sha(dataset_path):
                raise ValueError('回測模型／資料指紋不符')
            if {e['id'] for e in result['results']} != set(dataset):
                raise ValueError('回測片段缺失')
            config = result['config']
            expected = dict(decisionLatencyMs=1, decisionIntervalSeconds=3, cooldownSeconds=10,
                            maxHoldingSeconds=0, directionDrivenHolding=True, featureProfile='mini-micro-research',
                            observationSize=60, stopMode='fixed', fixedStopPoints=150, maxStopPoints=150,
                            commission=36, tickSize=5, multiplier=10, extraCostTicksPerSide=cost)
            if any(config[k] != v for k, v in expected.items()):
                raise ValueError('交易規則不符')
            segments = []
            for ep in result['results']:
                if ep['date'] != date or ep['cacheSha256'] != sha(ep['path']):
                    raise ValueError('回測快取不符')
                audits.append(dict(group=group, seed=seed, cost=cost, episode=ep['id'], steps=ep['steps'], sharedLedgerReconciled=ep['sharedLedgerReconciled'],summaryOnly=True))
            values=compact_daily_rows(folder/'evaluation.json',dataset_path,model,{date:index},group,seed,cost,trained_entry_mode=entry_mode)
            if len(values)!=1:raise ValueError('摘要回測日期缺失或重複')
            value={k:v for k,v in values[0].items() if k not in ['group','seed','date','extraCostTicksPerSide','fullDayCoverage','usage']}
            eligible = index['fullDayCoverage'] and cost == 0
            value = assess_day(dict(value,fullDayCoverage=index['fullDayCoverage']),eligible)
            initial = value['hardTargetPass']
            rows.append(dict(group=group, seed=seed, date=date, extraCostTicksPerSide=cost,
                             researchClockMode=clock_mode,
                             researchReadinessProfile=route['profile'],
                             fullDayCoverage=index['fullDayCoverage'], targetEligible=eligible,
                             initialTarget=initial, advancedTarget=initial and value['netPnlJPY'] > 0,
                             parameterId=f'{group}-fixed150-cost{cost}', **{k:v for k,v in value.items() if k!='fullDayCoverage'}))
            timings.append(dict(group=group, seed=seed, cost=cost, **result['timing']))
        return rows, audits, timings

    daily, audits, timings = [], [], []
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        for future in as_completed([pool.submit(evaluate, g, s) for g,s,model in entries]):
            rows, a, t = future.result()
            daily.extend(rows); audits.extend(a); timings.extend(t)
            print(f'{date} {rows[0]["group"]} seed{rows[0]["seed"]} audited', flush=True)
    expected_rows = len(entries)*3
    if len(daily) != expected_rows or len({(d['group'], d['seed'], d['extraCostTicksPerSide']) for d in daily}) != expected_rows:
        raise ValueError('每日情境缺失或重複')
    for cache in caches.values():verify_coverage_index(cache['coveragePin'])
    write(version/'coverage-inputs.json',{path:expected for cache in caches.values() for path,expected in cache['coveragePin'].items()})
    write(version/'daily-performance.json', dict(daily=daily, audits=audits, timings=timings,
          coverage=coverage, coverageByReadinessProfile={profile:cache['index'] for profile,cache in caches.items()},parameters=dict(expected_parameters(),researchReadinessProfiles=sorted(caches),researchClockMode=clock_mode), verification=dict(sharedLedgerReconciled=True,
          feedbackTransitions=0, sharedLedgerTransitions=sum(a['steps'] for a in audits),summaryOnly=True,coverageFingerprintsVerified=True), deploymentEligible=False))


def expected_parameters():
    return dict(product='225MC', reference='225M', decisionIntervalSeconds=3, predictionHorizonSeconds=15,
                decisionLatencyMs=1, cooldownSeconds=10, maxHoldingSeconds=0, fixedStopPoints=150,
                maxStopPoints=150, commissionJPYPerSide=36, extraCostTicksPerSide=[0,1,2],targets=TARGET)


def frozen_files(experiment, catalog=None, historical_report=None):
    paths = [experiment/'daily-performance.csv', experiment/'analysis-report.json', experiment/'risk150.json']
    paths += list((experiment/'runtime').glob('*.dll')) + list((experiment/'runtime').glob('*.json'))
    models = read(catalog) if catalog else {g:str(experiment/'models'/g) for g in GROUPS}
    if catalog:paths.append(Path(catalog))
    if historical_report:paths.append(Path(historical_report))
    for g,s,model in model_entries(models):
        paths += [model, model.with_suffix('.manifest.json')]
    paths += list(Path(__file__).parent.glob('*.py'))
    paths += list(COMPACT_BRIDGE.parent.glob('*.dll')) + list(COMPACT_BRIDGE.parent.glob('*.json'))
    paths += list(READINESS_BRIDGE.parent.glob('*.dll')) + list(READINESS_BRIDGE.parent.glob('*.json'))
    return {str(p.resolve()): sha(p) for p in paths}


def refresh_report(experiment, root, state, cutoff='99999999', historical_report=None):
    historical = read(historical_report or experiment/'analysis-report.json')['daily']
    rows = [dict(d, usage=d.get('usage','HistoricalPreviouslyInspected'), firstEvaluatedAt=d.get('firstEvaluatedAt'), revision=d.get('revision')) for d in historical]
    transitions = 0
    for date, entry in sorted(state['dates'].items()):
        if entry['status'] != 'Complete':
            continue
        verify_accepted_date(entry)
        result = read(Path(entry['version'])/'daily-performance.json')
        transitions += result['verification']['feedbackTransitions']
        rows += [dict(d, usage=date_usage(date, state['frozenDate']),
                      firstEvaluatedAt=entry['firstEvaluatedAt'], revision=entry['version']) for d in result['daily']]
    keys = {(r['date'],r['group'],r['seed'],r['extraCostTicksPerSide']) for r in rows}
    if len(keys) != len(rows):
        raise ValueError('歷史／新增績效日期重複')
    required_dates = sorted({r['date'] for r in rows if r['date']>cutoff and r['fullDayCoverage'] and r['extraCostTicksPerSide']==0})
    rows = [assess_day(r,r['date'] in required_dates and r['extraCostTicksPerSide']==0) for r in rows]
    for row in rows:
        row['targetEligible']=row['objectiveEligible']
        row['initialTarget']=row['hardTargetPass'];row['advancedTarget']=row['hardTargetPass'] and row['netPnlJPY']>0
    ranking = rank_candidates(rows,required_dates) if required_dates else dict(targets=TARGET,requiredDates=[],qualifiedRanking=[],selectedCandidate=None)
    write(root/'daily-performance.json', dict(parameters=expected_parameters(), daily=rows,
          dates=state['dates'], frozenAt=state['frozenAt'], newFeedbackTransitions=transitions,
          objective=ranking,deploymentEligible=False))
    temp = root/'daily-performance.csv.tmp'
    with temp.open('w',encoding='utf-8-sig',newline='') as writer:
        fields = list(dict.fromkeys(k for row in rows for k in row))
        csv_writer = csv.DictWriter(writer, fieldnames=fields)
        csv_writer.writeheader(); csv_writer.writerows(sorted(rows,key=lambda d:(d['date'],d['group'],d['seed'],d['extraCostTicksPerSide'])))
    temp.replace(root/'daily-performance.csv')
    lines = ['# 日經微型：每日增量回測', '',
             '每次執行自動掃描日期，未變更的成功日期跳過；補齊／修改資料以獨立版本重播。模型凍結，只更新研究排序，沒有自動重訓或正式模型切換。', '',
             '3秒判斷／15秒標籤、1ms動作延遲、10秒冷卻、無最長持倉、固定／最大150點、36JPY每邊、0／1／2跳成本。勝率及平均盈虧按扣成本後完成交易計算。缺口末端浮盈、浮虧與進場成本對稱排除，完整虧損保留。', '',
             '硬性目標：每天完成交易≥50、淨勝率≥40%、每筆期望毛損益≥50JPY（當日毛損益÷完成筆數）。所有完整驗證日均過關後，才以每筆毛期望值排序；零交易期望值不補零。', '',
             f'模型凍結於 {state["frozenAt"]}。晚於凍結日期的行情才標記FutureFrozenEvaluation；首次補入的較早日期仍是NewDateRetrospective。首次評估時間與版本保留，不能因首次加入就宣稱全新樣本外。', '',
             f'共 {len(rows)} 列日績效；歷史{len(historical)}列保留，新增回饋稽核 {transitions:,} 步。合格模型{len(ranking["qualifiedRanking"])}個。完整成本情境見daily-performance.csv。', '',
             '|新增日期|狀態|原因／版本|', '|---|---|---|']
    for date, entry in sorted(state['dates'].items()):
        lines.append(f'|{date}|{entry["status"]}|{entry.get("reason",entry.get("version",""))}|')
    lines += ['', '|日期|組別|seed|完整日|筆數|淨勝率%|毛損益JPY|淨損益JPY|平均淨獲利|平均淨損失|每筆期望毛損益JPY|硬性過關|', '|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---|']
    fmt=lambda v:'—' if v is None else f'{v:,.2f}'
    for row in sorted([r for r in rows if r['extraCostTicksPerSide']==0],key=lambda d:(d['date'],d['group'],d['seed'])):
        win = '—' if row['winRatePercent'] is None else f'{row["winRatePercent"]:.2f}'
        lines.append(f'|{row["date"]}|{row["group"]}|{row["seed"]}|{row["fullDayCoverage"]}|{row["trades"]}|{win}|{row["grossPnlJPY"]}|{row["netPnlJPY"]}|{fmt(row.get("averageWinJPY"))}|{fmt(row.get("averageLossJPY"))}|{fmt(row["expectedGrossPnlPerTradeJPY"])}|{row["hardTargetPass"]}|')
    lines += ['', '1ms仍是回測假設：actor／特徵／排程／網路的完整延遲未帶入逐筆成交；strict-cross按委託價成交，跳空實際損失可能超過150點。失敗／缺資料日期不捏造績效，舊版本保留於versions供查核。']
    leader=ranking.get('highestGrossExpectancyResearchCandidate')
    if leader:lines+=['',f'最高毛期望研究起點：{leader["group"]} seed{leader["seed"]}，{fmt(leader["expectedGrossPnlPerTradeJPY"])} JPY／筆；研究排序不代表通過每日硬性目標。']
    (root/'daily-performance.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')


def main():
    p = argparse.ArgumentParser(description='自動納入新增行情日期的凍結模型回測')
    p.add_argument('--experiment', default='docs/superpowers/evidence/nikkei-rl-candidates-v3-20261009')
    p.add_argument('--recordings', nargs='+', default=['KGIOptionPricing/bin/Debug/每日執行檔/NikkeiResearch'])
    p.add_argument('--output', default='C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-rl-daily-compact-20261009')
    p.add_argument('--catalog', default='docs/superpowers/evidence/nikkei-rl-bestparent-factors-20261009/model-catalog.json')
    p.add_argument('--historical-report', default='docs/superpowers/evidence/nikkei-rl-bestparent-factors-20261009/objective-report.json')
    p.add_argument('--jobs', type=int, choices=[1,2], default=2)
    p.add_argument('--dates',nargs='+',help='限定本次補回日期；排程預設掃描全部待補日期')
    p.add_argument('--clock',choices=['wall','monotonic'],default='wall',help='研究時間軸；monotonic首筆定位重建，原時間保留')
    args = p.parse_args()
    experiment, root = Path(args.experiment).resolve(), Path(args.output).resolve()
    catalog = Path(args.catalog).resolve()
    historical_report = Path(args.historical_report).resolve()
    for path in args.recordings:
        if not Path(path).is_dir():
            raise ValueError('行情目錄不存在：'+path)
    root.mkdir(parents=True,exist_ok=True)
    lock = root/'.update.lock'
    with lock.open('x') as writer:
        writer.write(str(uuid.uuid4()))
    try:
        clock_pin=pin_clock_mode(root,args.clock)
        before = dict(frozen_files(experiment, catalog, historical_report),**clock_pin)
        pin = root/'frozen-inputs.json'
        if pin.exists():
            if read(pin) != before:
                raise ValueError('模型、執行器或回測程式已變更；請建立新的輸出根目錄')
        else:
            write(pin,before)
        state_path = root/'registry.json'
        now = datetime.now(TZ)
        state = read(state_path) if state_path.exists() else dict(frozenAt=now.isoformat(), frozenDate=now.strftime('%Y%m%d'), dates={})
        write(state_path, state)
        models = read(catalog) if catalog.exists() else {g:str(experiment/'models'/g) for g in GROUPS}
        training_dates = [e[:8] for g,s,model in model_entries(models) for e in read(model.with_suffix('.manifest.json'))['trainingEpisodes']] if catalog.exists() else []
        cutoff = max(training_dates,default='99999999')
        historical=read(historical_report if historical_report.exists() else experiment/'analysis-report.json')['daily']
        failures = []
        for date, sources in pending_recording_dates(args.recordings,historical,state['dates'],set(args.dates) if args.dates else None).items():
            sig = signature(sources); previous = state['dates'].get(date)
            if not needs_processing(previous,sig):
                continue
            entry = dict(signature=sig, sources=[str(p) for p in sources], checkedAt=now.isoformat())
            if date > now.strftime('%Y%m%d') or date == now.strftime('%Y%m%d') and now.time() < time(15):
                entry.update(status='Deferred',reason='當日日盤尚未結束')
                state['dates'][date]=entry; write(state_path,state)
                continue
            version = root/'versions'/date/(datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S')+'-'+uuid.uuid4().hex[:8])
            version.mkdir(parents=True)
            entry['version']=str(version)
            try:
                parts = [extract_day(source,version/f'part{i}.jsonl',date,clock_mode=args.clock) for i,source in enumerate(sources)]
                combined = combine_parts(parts,version/'market-events.jsonl')
                write(version/'extraction.json',dict(parts=parts,combined=combined))
                # Extraction metadata retains hashes; originals remain authoritative.
                for part in parts:Path(part['path']).unlink()
                if not combined['marketRows']:
                    entry.update(status='NoMarketData',reason='僅啟動／非日盤紀錄，沒有行情')
                elif {p for part in parts for p in part['products']} != {'225MC','225M'}:
                    raise ValueError('缺少225MC或225M日盤行情')
                elif previous and previous['status']=='Complete' and previous.get('marketSha256') == combined['sha256']:
                    verify_accepted_date(previous)
                    entry.update(status='Complete',version=previous['version'],marketSha256=combined['sha256'],firstEvaluatedAt=previous['firstEvaluatedAt'])
                    entry.update(dailyResultSha256=previous['dailyResultSha256'],coverageInputs=previous['coverageInputs'],researchClockMode=previous.get('researchClockMode','wall'))
                    write(version/'same-day-snapshot.json',dict(reusedVersion=previous['version']))
                else:
                    evaluate_date(experiment,version,date,args.jobs,models)
                    if sig != signature(sources):
                        raise ValueError('來源在回測期間變更，結果不升級')
                    entry.update(status='Complete',marketSha256=combined['sha256'],
                                 firstEvaluatedAt=(previous or {}).get('firstEvaluatedAt',datetime.now(TZ).isoformat()))
                    entry.update(dailyResultSha256=sha(version/'daily-performance.json'),coverageInputs=read(version/'coverage-inputs.json'),researchClockMode=args.clock)
                print(date+' '+entry['status'],flush=True)
            except Exception as error:
                entry.update(status='Failed',reason=str(error)); failures.append(date)
                write(version/'failure.json',entry)
                print(date+' Failed: '+str(error),flush=True)
            finally:
                # Derived raw copy is reproducible from preserved originals; retain binary cache only.
                temporary_raw=version/'market-events.jsonl'
                if temporary_raw.exists():temporary_raw.unlink()
                for temporary_part in version.glob('part*.jsonl'):temporary_part.unlink()
            state['dates'][date]=entry; write(state_path,state)
        if dict(frozen_files(experiment, catalog, historical_report),**{path:sha(path) for path in clock_pin})!=before:
            raise ValueError('凍結輸入在執行期間變更')
        refresh_report(experiment,root,state,cutoff=cutoff,historical_report=historical_report if historical_report.exists() else None)
        write(root/'last-run.json',dict(at=datetime.now(TZ).isoformat(), failures=failures,
              frozenArtifactsUnchanged=True, registrySha256=sha(state_path),
              reportSha256=sha(root/'daily-performance.json')))
        if failures:
            raise ValueError('部分日期未通過資料／回測品質：'+','.join(failures))
    finally:
        lock.unlink()


if __name__ == '__main__':
    main()
