"""Frozen actor risk overlays; shared C# execution/ledger remains authoritative."""
import argparse,csv,gzip,json,subprocess,shutil
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor,as_completed
from collections import Counter
from completed_daily import completed_segment,aggregate_daily
from expanded_daily_backtest import sha,read,write,DATES
from fill_diagnostics import equal_number
from direction_warmstart import parse_at

def audit_episode(ep,config):
    maximum=config.get('maxStopPoints')
    fills=[];steps=0;reward=delta=0;previous=None;adjustments=0;entry=None;gap_overruns=[];last_exit=None
    with gzip.open(ep['feedbackPath'],'rt',encoding='utf-8') as file:
        for raw in file:
            row=json.loads(raw);state=row['next']
            if row['stepId']!=steps:raise ValueError('Feedback order mismatch')
            steps+=1;reward+=state['reward'];delta+=state['deltaEquity'];fills.extend(state['fills'])
            if state['overrideReason']=='MAX_HOLDING':raise ValueError('Unexpected time exit')
            if state['position'] and config['stopMode']!='legacy-fixed':
                if not 0<state['activeStopDistancePoints']<=maximum or state['activeStopPrice']%5:raise ValueError('Stop cap/tick alignment violated')
                if previous and previous['position']==state['position'] and previous['completedTrades']==state['completedTrades']:
                    if state['activeStopDistancePoints']>previous['activeStopDistancePoints']:raise ValueError('Held stop loosened')
            for fill in state['fills']:
                if fill['role'] in [0,1]:
                    if entry is not None:raise ValueError('Duplicate held entry')
                    if last_exit is not None and (parse_at(fill['timestamp'])-last_exit).total_seconds()<10:raise ValueError('Cooldown less than10s')
                    entry=fill
                else:
                    if entry is None:raise ValueError('Unpaired exit')
                    if fill['role']==3 and config['stopMode']!='legacy-fixed':
                        direction=1 if entry['role']==0 else -1
                        modeled_loss=max(0,direction*(entry['price']-fill['price']))
                        trigger_loss=max(0,direction*(entry['price']-fill['triggerMarketPrice']))
                        if modeled_loss>maximum:raise ValueError('Modeled stop beyond maximum')
                        if trigger_loss>maximum:gap_overruns.append(trigger_loss-maximum)
                    entry=None;last_exit=parse_at(fill['timestamp'])
            previous=state;adjustments=state['stopAdjustmentCount']
    if steps!=ep['steps'] or previous!=ep['state'] or fills!=ep['fills'] or sha(ep['feedbackPath'])!=ep['feedbackSha256'] or not equal_number(reward,previous['totalReward']) or not equal_number(delta,previous['netPnl']):raise ValueError('Shared ledger/reward/hash feedback mismatch')
    return completed_segment(previous,fills),dict(steps=steps,adjustments=adjustments,gapOverrunStops=len(gap_overruns),maxTriggerExcessPoints=max(gap_overruns,default=0))

def main():
    p=argparse.ArgumentParser();p.add_argument('--root',required=True);p.add_argument('--bridge',required=True);p.add_argument('--power-root',required=True);p.add_argument('--max-stop-points',type=int,choices=[20,150],default=20);p.add_argument('--fixed-stops',type=int,nargs='+');args=p.parse_args()
    maximum=args.max_stop_points;fixed=args.fixed_stops or ([10,15,20] if maximum==20 else [30,60,90,120,150])
    if len(set(fixed))!=len(fixed) or any(point<5 or point>maximum or point%5 for point in fixed):raise ValueError('Fixed stops must be distinct tick-aligned values within maximum')
    r=Path(args.root).resolve();power=Path(args.power_root).resolve();bridge=Path(args.bridge).resolve();models=power/'models/Power'
    if (r/'experiment-inputs.json').exists():raise ValueError('Immutable experiment exists')
    r.mkdir(parents=True,exist_ok=True);(r/'configs').mkdir(exist_ok=True);(r/'evaluations').mkdir(exist_ok=True);(r/'source-csharp').mkdir(exist_ok=True);(r/'source-python').mkdir(exist_ok=True)
    files=[bridge,*bridge.parent.glob('*.dll'),power/'evaluation-dataset.json',Path(__file__).resolve()]
    for seed in [11,22,33]:files.extend([models/f'policy-seed{seed}.onnx',models/f'policy-seed{seed}.manifest.json'])
    dataset=read(power/'evaluation-dataset.json')
    for cache in dataset.values():files.extend([Path(cache),Path(cache+'.manifest.json')])
    for path in Path('tools/NikkeiRlReplayBridge').glob('*.cs'):
        shutil.copy2(path,r/'source-csharp'/path.name);files.append(path.resolve())
    for path in Path(__file__).resolve().parent.glob('*.py'):
        shutil.copy2(path,r/'source-python'/path.name);files.append(path)
    profiles={f'fixed{point}':dict(stopMode='fixed',fixedStopPoints=point) for point in fixed}
    profiles.update({'vol1':dict(stopMode='adaptive-volatility',stopVolatilityMultiplier=1),'vol2':dict(stopMode='adaptive-volatility',stopVolatilityMultiplier=2)})
    tasks=[]
    for seed in [11,22,33]:tasks.append(('legacy30',seed,0,None))
    for name,profile in profiles.items():
        for cost in [0,1,2]:
            config=dict(minStopPoints=5,maxStopPoints=maximum,fixedStopPoints=maximum,stopVolatilitySeconds=60,stopVolatilityMultiplier=2,extraCostTicksPerSide=cost);config.update(profile)
            path=r/'configs'/f'{name}-cost{cost}.json';write(path,config);files.append(path)
            for seed in [11,22,33]:tasks.append((name,seed,cost,path))
    frozen={str(path):sha(path) for path in files};write(r/'experiment-inputs.json',frozen)
    write(r/'default-stop-parameters.json',dict(stopMode='fixed',fixedStopPoints=maximum,minStopPoints=5,maxStopPoints=maximum,stopVolatilitySeconds=60,stopVolatilityMultiplier=2))
    def run(task):
        name,seed,cost,risk=task;folder=r/'evaluations'/f'{name}-seed{seed}-cost{cost}'
        cmd=['dotnet',str(bridge),'evaluate-risk' if risk else 'evaluate-frozen',str(models/f'policy-seed{seed}.onnx'),str(models/f'policy-seed{seed}.manifest.json'),str(power/'evaluation-dataset.json')]
        if risk:cmd.append(str(risk))
        cmd.append(str(folder))
        with (r/f'{name}-seed{seed}-cost{cost}.log').open('x',encoding='utf-8') as log:subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT,check=True)
        print('Replay complete',name,seed,cost,flush=True)
        return task,read(folder/'evaluation.json')
    daily=[];audits=[];transitions=0
    baseline=read(power/'daily-performance.json')['daily']
    with ThreadPoolExecutor(max_workers=3) as pool:
        for future in as_completed([pool.submit(run,t) for t in tasks]):
            task,evaluation=future.result();name,seed,cost,_=task
            config=evaluation['config'];by_date={d:[] for d in DATES};audit_by_date={d:[] for d in DATES}
            if config['decisionLatencyMs']!=1 or config['cooldownSeconds']!=10 or config['maxHoldingSeconds']!=0 or config['featureProfile']!='mini-micro-power':raise ValueError('Protected contract changed')
            for ep in evaluation['results']:
                complete,audit=audit_episode(ep,config);by_date[ep['date']].append(complete);audit_by_date[ep['date']].append(audit);transitions+=audit['steps']
            for date in DATES:
                row=aggregate_daily(by_date[date]);index=read(power/'cache'/date/'segment-index.json');eligible=index['fullDayCoverage'] and date>'20261005'
                initial=eligible and cost==0 and name!='legacy30' and row['trades']>100 and (row['winRatePercent'] or 0)>33 and row['grossPnlJPY']>0
                daily.append(dict(profile=name,seed=seed,date=date,extraCostTicksPerSide=cost,fullDayCoverage=index['fullDayCoverage'],targetEligible=eligible and cost==0 and name!='legacy30',initialTarget=initial,advancedTarget=initial and row['netPnlJPY']>0,**row))
                audits.append(dict(profile=name,seed=seed,date=date,extraCostTicksPerSide=cost,steps=sum(a['steps'] for a in audit_by_date[date]),stopAdjustments=sum(a['adjustments'] for a in audit_by_date[date]),gapOverrunStops=sum(a['gapOverrunStops'] for a in audit_by_date[date]),maxTriggerExcessPoints=max((a['maxTriggerExcessPoints'] for a in audit_by_date[date]),default=0)))
                if name=='legacy30':
                    old=next(d for d in baseline if d['seed']==seed and d['date']==date)
                    for key in ['trades','wins','losses','grossPnlJPY','netPnlJPY','costsJPY','excludedOpenPositions','excludedOpenNetPnlJPY']:
                        if not equal_number(row[key],old[key]):raise ValueError('Legacy baseline changed: '+key)
    expected_rows=len(tasks)*len(DATES)
    if len(daily)!=expected_rows or len({(d['profile'],d['seed'],d['date'],d['extraCostTicksPerSide']) for d in daily})!=expected_rows:raise ValueError('Missing/duplicate daily rows')
    if any(sha(path)!=value for path,value in frozen.items()):raise ValueError('Frozen input changed')
    summaries=[dict(profile=name,seed=seed,**aggregate_daily([d for d in daily if d['profile']==name and d['seed']==seed and d['date'] in ['20261006','20261007'] and d['extraCostTicksPerSide']==0])) for name in ['legacy30',*profiles] for seed in [11,22,33]]
    result=dict(status='PreviouslyInspectedRiskSensitivityNotRetrained',deploymentEligible=False,maxStopPoints=maximum,minimumAdaptivePoints=5,profiles=profiles,daily=daily,developmentPerCandidate=summaries,initialPassed=sum(d['initialTarget'] for d in daily),advancedPassed=sum(d['advancedTarget'] for d in daily),targetEligibleRows=sum(d['targetEligible'] for d in daily),audits=audits,verification=dict(dailyRows=expected_rows,scenarios=len(tasks),feedbackTransitions=transitions,legacyBaselineUnchanged=True,allFrozenInputsUnchanged=True,frozenInputs=len(frozen),allRiskStopsCapped=True,heldStopsNeverLoosen=True,cooldownAtLeast10Seconds=True,sharedLedgerReconciled=True))
    write(r/'analysis-report.json',result)
    with (r/'daily-performance.csv').open('w',encoding='utf-8-sig',newline='') as file:
        writer=csv.DictWriter(file,fieldnames=list(daily[0]));writer.writeheader();writer.writerows(sorted(daily,key=lambda d:(d['profile'],d['seed'],d['extraCostTicksPerSide'],d['date'])))
    lines=[f'# 225MC：最大{maximum}點停損比較','', f'新設定最大{maximum}點（{maximum//5}跳），固定'+ '/'.join(map(str,fixed))+f'點或60秒成交事件等權價格標準差×1/2，下限5點，持倉後只收緊，以進場價為錨、未暖機使用{maximum}點；合法tick向下取整，無最長持倉。3秒判斷、15秒原始模型標籤、1ms交易延遲、10秒冷卻、一口、36JPY/邊。','',
    '凍結三組Power模型，原模型在30點固定停損環境訓練；此輪改變執行停損而未重新訓練，是風險設定敏感度，不改寫ONNX或manifest。10/6、10/7仍是反覆查看的開發日期，四訓練日期與資料缺口日另列，不宣稱全新樣本外。','',
    f'{maximum}點為價格觸發上限，不是實盤成交損失保證。strict-cross paper在嚴格穿越後按停損委託價成交，跳空價差未反映為實際成交滑價；成本0/1/2單邊tick是額外成本敏感度。JSON另外統計觸發市場價超過{maximum}點的停損，不能把模擬上限誤當實盤最大損失。','',
    f"初步達標{result['initialPassed']}/{result['targetEligibleRows']}；進階達標{result['advancedPassed']}/{result['targetEligibleRows']}（每個模型／開發日獨立判斷）。",'',
    '| 停損設定 | 種子 | 兩日交易筆數 | 淨勝率% | 毛損益JPY | 淨損益JPY | 平均淨獲利JPY | 平均淨損失JPY |','|---|---:|---:|---:|---:|---:|---:|---:|']
    fmt=lambda value:'—' if value is None else f'{value:,.2f}'
    for d in summaries:lines.append(f"| {d['profile']} | {d['seed']} | {d['trades']} | {fmt(d['winRatePercent'])} | {fmt(d['grossPnlJPY'])} | {fmt(d['netPnlJPY'])} | {fmt(d['averageWinJPY'])} | {fmt(d['averageLossJPY'])} |")
    lines+=['','## 各日績效（基本費用；完整成本矩陣見CSV／JSON）','', '| 設定 | 種子 | 日期 | 完整日 | 交易筆數 | 淨勝率% | 毛損益JPY | 淨損益JPY | 平均淨獲利 | 平均淨損失 | 排除未完成持倉 |','|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|']
    for d in sorted([d for d in daily if d['extraCostTicksPerSide']==0],key=lambda d:(d['profile'],d['seed'],d['date'])):lines.append(f"| {d['profile']} | {d['seed']} | {d['date']} | {d['fullDayCoverage']} | {d['trades']} | {fmt(d['winRatePercent'])} | {fmt(d['grossPnlJPY'])} | {fmt(d['netPnlJPY'])} | {fmt(d['averageWinJPY'])} | {fmt(d['averageLossJPY'])} | {d['excludedOpenPositions']} |")
    lines+=['',f'基準legacy30僅歷史比較，不是新設定。缺口尾端未完成持倉的浮盈／浮損及進場費對稱排除，已完成交易虧損保留。所有{expected_rows}日別rows、{len(tasks)}情境及逐筆回饋均核對共用帳本；模型的觀測及動作契約保持不變。']
    (r/'analysis-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(result['verification']),flush=True)
if __name__=='__main__':main()
