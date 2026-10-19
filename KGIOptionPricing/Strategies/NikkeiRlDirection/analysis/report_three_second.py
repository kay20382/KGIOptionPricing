"""Frozen 3-second/15-second experiment, with shared-ledger daily audit."""
import csv,gzip,json,subprocess,argparse
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor,as_completed
from completed_daily import completed_segment,aggregate_daily
from expanded_daily_backtest import sha,read,write,DATES
from root_cause_comparison import validate_parity,compare_state
from fill_diagnostics import equal_number

def feedback(path):
    buckets={}
    with gzip.open(path,'rt',encoding='utf-8') as file:
        for raw in file:
            row=json.loads(raw);s=row['next'];b=buckets.setdefault(row['episode'],dict(fills=[],steps=0,reward=0,delta=0))
            if row['stepId']!=b['steps']:raise ValueError('Feedback step order mismatch')
            b['steps']+=1;b['fills'].extend(s['fills']);b['reward']+=s['reward'];b['delta']+=s['deltaEquity'];b['last']=s
    for b in buckets.values():
        if not equal_number(b['reward'],b['last']['totalReward']) or not equal_number(b['delta'],b['last']['netPnl']):raise ValueError('Reward/delta mismatch')
        b['completed']=completed_segment(b['last'],b['fills'])
    return buckets

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);args=parser.parse_args();r=Path(args.root).resolve();models=r/'models/S315';bridge=r/'runtime-training/Bridge.dll'
    report=read(models/'backtest-report.json');expected=dict(cooldownSeconds=10,decisionLatencyMs=1,rewardCostWeight=1,observationSize=40,decisionIntervalSeconds=3)
    if report['environmentConfig']!=expected or report['predictionHorizonSeconds']!=15 or report['warmstartProvenance']['horizonSeconds']!=15:raise ValueError('Wrong 3s/15s training contract')
    sources=[p for folder in [r/'runtime-training',r/'source-python',r/'source-csharp',models,r/'training-cache'] for p in folder.iterdir() if p.is_file() and p.suffix not in ['.log']]+[r/'training-dataset.json',r/'evaluation-dataset.json',r/'executable-labels-3s15s.jsonl',r/'executable-labels-3s15s.jsonl.manifest.json']
    sources+=list((r/'cache').glob('*/*.br'))+list((r/'cache').glob('*/*.manifest.json'))+list((r/'cache').glob('*/segment-index.json'))+[Path(__file__).resolve()]
    frozen={str(p):sha(p) for p in sources};write(r/'frozen-fingerprints.json',frozen)
    def replay(seed):
        out=r/'evaluations'/f'S315-seed{seed}';out.parent.mkdir(exist_ok=True)
        with (r/f'replay-seed{seed}.log').open('x',encoding='utf-8') as log:subprocess.run(['dotnet',str(bridge),'evaluate-frozen',str(models/f'policy-seed{seed}.onnx'),str(models/f'policy-seed{seed}.manifest.json'),str(r/'evaluation-dataset.json'),str(out)],stdout=log,stderr=subprocess.STDOUT,check=True)
        subprocess.run(['dotnet',str(bridge),'benchmark',str(models/f'policy-seed{seed}.onnx'),str(models/f'policy-seed{seed}.manifest.json'),str(models/f'parity-seed{seed}.json'),str(r/f'parity-seed{seed}.json')],stdout=subprocess.DEVNULL,check=True)
        print(f'Frozen S315 seed{seed} complete',flush=True)
    with ThreadPoolExecutor(max_workers=3) as pool:
        for f in as_completed([pool.submit(replay,s) for s in [11,22,33]]):f.result()
    costs=[];daily=[];timings=[];steps=cost_steps=0
    for case in report['scenarios']:
        if case['latencyMs']!=1:raise ValueError('Wrong latency')
        buckets=feedback(models/f"feedback-seed{case['seed']}-delay1-cost{case['extraCostTicksPerSide']}.jsonl.gz")
        for ep in case['episodes']:
            b=buckets[ep['episode']];compare_state({k:v for k,v in ep.items() if k!='episode'},b['last']);cost_steps+=b['steps'];costs.append(dict(seed=case['seed'],date=ep['episode'][:8],extraCostTicksPerSide=case['extraCostTicksPerSide'],**b['completed']))
    for seed in [11,22,33]:
        parity=read(r/f'parity-seed{seed}.json');validate_parity(parity,sha(models/f'policy-seed{seed}.onnx'),sha(models/f'policy-seed{seed}.manifest.json'),sha(models/f'parity-seed{seed}.json'));timings.append(dict(seed=seed,**parity))
        by_date={d:[] for d in DATES};evaluation=read(r/'evaluations'/f'S315-seed{seed}'/'evaluation.json')
        if evaluation['config']['decisionIntervalSeconds']!=3:raise ValueError('Replay cadence differs')
        for ep in evaluation['results']:
            b=feedback(ep['feedbackPath'])[ep['id']]
            if b['last']!=ep['state'] or b['fills']!=ep['fills'] or b['steps']!=ep['steps'] or sha(ep['feedbackPath'])!=ep['feedbackSha256']:raise ValueError('C# replay mismatch')
            by_date[ep['date']].append(b['completed']);steps+=b['steps']
        for d in DATES:
            row=aggregate_daily(by_date[d]);index=read(r/'cache'/d/'segment-index.json');eligible=index['fullDayCoverage'] and d>'20261005'
            initial=eligible and row['trades']>100 and (row['winRatePercent'] or 0)>33 and row['grossPnlJPY']>0
            daily.append(dict(seed=seed,date=d,fullDayCoverage=index['fullDayCoverage'],targetEligible=eligible,initialTarget=initial,advancedTarget=initial and row['netPnlJPY']>0,**row))
            matches=[c for c in costs if c['seed']==seed and c['date']==d and c['extraCostTicksPerSide']==0]
            if matches:
                for k in ['trades','grossPnlJPY','netPnlJPY','costsJPY','wins','losses']:
                    if not equal_number(row[k],matches[0][k]):raise ValueError('Python/C# daily parity mismatch')
    if frozen!={p:sha(p) for p in frozen}:raise ValueError('Frozen input changed')
    if len(daily)!=21 or len(costs)!=18:raise ValueError('Daily/cost rows missing')
    result=dict(environmentConfig=expected,predictionHorizonSeconds=15,dates=DATES,seeds=[11,22,33],daily=daily,costs=costs,timings=timings,developmentSummary=aggregate_daily([d for d in daily if d['targetEligible']]),initialPassed=sum(d['initialTarget'] for d in daily),advancedPassed=sum(d['advancedTarget'] for d in daily),targetEligibleRows=sum(d['targetEligible'] for d in daily),deploymentEligible=False)
    write(r/'daily-performance.json',result)
    with (r/'daily-performance.csv').open('w',encoding='utf-8-sig',newline='') as file:
        writer=csv.DictWriter(file,fieldnames=list(daily[0]));writer.writeheader();writer.writerows(daily)
    fmt=lambda x:'—' if x is None else f'{x:,.2f}'
    lines=['# 225MC：3秒判斷、15秒預測回測','', '設定：每3秒取樣及判斷，15秒可執行淨損益預訓練標籤、延遲1ms、冷卻10秒、一口、來回基本費用72 JPY、原40維、停損6ticks、最長持倉60秒。15秒是標籤平倉意圖時點，實際成交仍受行情與延遲約束；PPO仍學習進出場。','', '5／15／30／60秒報酬按原始成交時間計算；PPO gamma／GAE按3/5次方維持原5秒時間衰減。每種子102,400步，與舊模型的總回放時間不同。本次同時更改頻率、標籤期間及非整除窗口處理，不能歸因為單一因素。','', '9/28、9/29、9/30、10/5為訓練日期；10/2為歷史檢視；10/6、10/7是反覆查看的開發日期，沒有全新樣本外。三種子是三個獨立候選模型，不是策略組合。資料缺口分段、暖機60秒；最後未完成持倉的浮盈／浮損及進場費對稱排除，已完成虧損保留。','',f"適格模型日期案例：初步目標{result['initialPassed']}/{result['targetEligibleRows']}；進階目標{result['advancedPassed']}/{result['targetEligibleRows']}。",'', '| 日期 | 種子 | 完整日 | 交易筆數 | 淨勝率% | 毛損益JPY | 淨損益JPY | 平均淨獲利 | 平均淨損失 | 排除持倉 |','|---|---:|---|---:|---:|---:|---:|---:|---:|---:|']
    for d in sorted(daily,key=lambda d:(d['date'],d['seed'])):lines.append(f"| {d['date']} | {d['seed']} | {d['fullDayCoverage']} | {d['trades']} | {fmt(d['winRatePercent'])} | {fmt(d['grossPnlJPY'])} | {fmt(d['netPnlJPY'])} | {fmt(d['averageWinJPY'])} | {fmt(d['averageLossJPY'])} | {d['excludedOpenPositions']} |")
    lines+=['','## C#推論耗時','', '| 種子 | actor p99 ms | 最大ms | 超過1ms次數 |','|---|---:|---:|---:|']
    for t in timings:lines.append(f"| {t['seed']} | {t['p99Ms']:.4f} | {t['maxMs']:.4f} | {t['countAbove1Ms']} |")
    lines+=['','最多三程序並行；actor計時不含特徵處理、排程與券商網路。固定1ms為回測假設，非端到端保證。所有模型僅供研究。成本0／1／2單邊額外ticks及逐筆原始回饋見JSON與models/S315。']
    (r/'daily-performance.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    write(r/'verification.json',dict(pythonTests=57,csharpTests=98,frozenInputFiles=len(frozen),allFrozenHashesUnchanged=True,onnxParitySnapshots=3600,primaryFeedbackTransitions=steps,costFeedbackTransitions=cost_steps,sharedLedgerReconciled=True,pythonCsharpDailyParity=True,decisionIntervalSeconds=3,predictionHorizonSeconds=15,review='Self-reviewed'))
    print(json.dumps(result['developmentSummary']),flush=True)
if __name__=='__main__':main()
