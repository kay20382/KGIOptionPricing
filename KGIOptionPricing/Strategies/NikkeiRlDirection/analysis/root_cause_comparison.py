"""Controlled root-cause experiments, frozen C# replay, and accounting/provenance audit."""
import argparse,csv,gzip,json,subprocess
from concurrent.futures import ThreadPoolExecutor,as_completed
from pathlib import Path
import numpy as np
from completed_daily import completed_segment,aggregate_daily
from expanded_daily_backtest import sha,read,write,DATES
from fill_diagnostics import fill_outcomes,equal_number

GROUPS=['P0','E0','E1','E2','M0'];SEEDS=[11,22,33]
def compare_state(expected,actual):
    for key,value in expected.items():
        same=value==actual[key] if isinstance(value,str) else equal_number(value,actual[key])
        if not same:raise ValueError('Feedback/report mismatch: '+key)
def validate_parity(row,model_hash,manifest_hash,fixture_hash):
    if row['count']!=1200 or not row['allActionsMatch'] or row['maxProbabilityError']>1e-6 or row['modelSha256']!=model_hash or row['manifestSha256']!=manifest_hash or row['paritySha256']!=fixture_hash:raise ValueError('ONNX parity artifact identity/accuracy failed')
def comparison_contract(reports):
    p=reports['P0'];e=reports['E0']
    if p['environmentConfig']!=e['environmentConfig'] or p['warmstartProvenance']['matchedTimestampsSha256']!=e['warmstartProvenance']['matchedTimestampsSha256']:raise ValueError('Paired target experiment changed environment or timestamps')
    if any(r['environmentConfig']!=p['environmentConfig'] for r in reports.values()):raise ValueError('Sample-expansion environment changed')
    n=lambda r:r['warmstartProvenance'].get('selectedSamples',r['warmstartProvenance'].get('matchedTimestampSamples'))
    if n(p)!=n(e) or not n(reports['E1'])>n(e) or not n(reports['E2'])>n(reports['E1']):raise ValueError('Sample expansion contract failed')
    if reports['E1']['warmstartProvenance'].get('matchedOnly',True) or reports['E2']['warmstartProvenance'].get('matchedOnly',True):raise ValueError('Expanded samples still require future book health')
    if 'M0' in reports and (n(reports['M0'])!=n(reports['E2']) or reports['M0']['warmstartProvenance']['matchedTimestampsSha256']!=reports['E2']['warmstartProvenance']['matchedTimestampsSha256']):raise ValueError('Mark repair changed training samples')

def feedback_audit(path,expected,allow_open):
    fills=[];steps=0;reward=delta=0;last=None
    with gzip.open(path,'rt',encoding='utf-8') as file:
        for line in file:
            row=json.loads(line)
            if row['stepId']!=steps:raise ValueError('Feedback step order')
            state=row['next'];fills.extend(state['fills']);reward+=state['reward'];delta+=state['deltaEquity'];last=state;steps+=1
    if not equal_number(reward,last['totalReward']) or not equal_number(delta,last['netPnl']):raise ValueError('Reward/raw equity mismatch')
    compare_state({field:expected[field] for field in ['status','position','netPnl','costs','completedTrades','winRate','averageWinningPoints','averageLosingPoints','totalReward']},last)
    return completed_segment(last,fills),steps,last

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',default='docs/superpowers/evidence/nikkei-rl-root-cause-20261008');parser.add_argument('--report-only',action='store_true');args=parser.parse_args()
    root=Path(args.root).resolve();original=root.parent/'nikkei-rl-expanded-20261008'
    bridge=root/'runtime-extended/NikkeiRlReplayBridge.dll';dataset=original/'dataset.json';models=root/'models'
    reports={g:read(models/g/'backtest-report.json') for g in GROUPS};comparison_contract(reports)
    if any(len(r['training'])!=3 or len(r['scenarios'])!=9 or any(t['actualSteps']!=102400 for t in r['training']) for r in reports.values()):raise ValueError('Training/evaluation incomplete')
    x0=np.load(models/'P0/direction-training.npz')['observation'];x1=np.load(models/'E0/direction-training.npz')['observation']
    if not np.array_equal(x0,x1):raise ValueError('Matched target experiment changed observations')
    extended=np.load(models/'E2/direction-training.npz');marked=np.load(models/'M0/direction-training.npz')
    if not np.array_equal(extended['observation'],marked['observation']) or not np.array_equal(extended['action'],marked['action']):raise ValueError('Mark timing repair changed executable training observations/targets')
    if any(read(models/g/f'policy-seed{s}.manifest.json')['environmentConfig']!=reports[g]['environmentConfig'] for g in GROUPS for s in SEEDS):raise ValueError('Model environment provenance changed')
    fingerprints={str(p):sha(p) for p in [*root.glob('runtime*/*.dll'),*root.glob('runtime*/*.json'),*root.glob('source-*/*.py'),*root.glob('source-*/*.cs'),*models.glob('*/*.onnx'),*models.glob('*/*.manifest.json'),*models.glob('*/*.zip'),*models.glob('*/*.pkl'),*models.glob('*/direction-training.*'),*models.glob('*/parity-seed*.json'),*root.glob('executable-labels*.jsonl'),*root.glob('executable-labels*.manifest.json'),dataset]}
    fp=root/'replay-fingerprints.json'
    if fp.exists():
        if read(fp)!=fingerprints:raise ValueError('Frozen replay inputs changed')
    else:write(fp,fingerprints)
    if not args.report_only:
        def run(g,seed):
            model_bridge=root/'runtime-mark/NikkeiRlReplayBridge.dll' if g=='M0' else bridge
            output=root/'evaluations'/f'{g}-seed{seed}';output.parent.mkdir(exist_ok=True)
            with (root/f'replay-{g}-seed{seed}.log').open('x',encoding='utf-8') as log:
                subprocess.run(['dotnet',str(model_bridge),'evaluate-frozen',str(models/g/f'policy-seed{seed}.onnx'),str(models/g/f'policy-seed{seed}.manifest.json'),str(dataset),str(output)],stdout=log,stderr=subprocess.STDOUT,check=True)
            subprocess.run(['dotnet',str(model_bridge),'benchmark',str(models/g/f'policy-seed{seed}.onnx'),str(models/g/f'policy-seed{seed}.manifest.json'),str(models/g/f'parity-seed{seed}.json'),str(root/f'parity-{g}-seed{seed}.json')],stdout=subprocess.DEVNULL,check=True)
            print(f'Frozen {g} seed{seed} replay/parity passed',flush=True)
        with ThreadPoolExecutor(max_workers=3) as pool:
            for task in as_completed([pool.submit(run,g,s) for g in GROUPS for s in SEEDS]):task.result()
    if fingerprints!={p:sha(p) for p in fingerprints}:raise ValueError('Frozen replay inputs changed')
    daily=[];segments=[];timings=[];costs=[];transitions=0;cost_transitions=0
    indexes=read(original/'daily-performance.json')['dataCoverage']
    for g in GROUPS:
        # All primary/cost Python feedback still reconciles actual shared completed cycles.
        for scenario in reports[g]['scenarios']:
            seed=scenario['seed'];cost=scenario['extraCostTicksPerSide']
            feedback=models/g/f'feedback-seed{seed}-delay1-cost{cost}.jsonl.gz'
            # File holds two episodes, each restarts stepId0; stream into per-episode buckets.
            episode_fills={};episode_last={};episode_steps={};reward={};delta={}
            with gzip.open(feedback,'rt',encoding='utf-8') as file:
                for raw in file:
                    row=json.loads(raw);id=row['episode'];state=row['next'];step=episode_steps.get(id,0)
                    if row['stepId']!=step:raise ValueError('Cost feedback step mismatch')
                    episode_steps[id]=step+1;episode_fills.setdefault(id,[]).extend(state['fills']);episode_last[id]=state;reward[id]=reward.get(id,0)+state['reward'];delta[id]=delta.get(id,0)+state['deltaEquity']
            for episode in scenario['episodes']:
                id=episode['episode'];state=episode_last[id]
                compare_state({k:v for k,v in episode.items() if k!='episode'},state)
                if not equal_number(delta[id],state['netPnl']) or not equal_number(reward[id],state['totalReward']):raise ValueError('Cost feedback raw equity/reward mismatch')
                row=completed_segment(state,episode_fills[id]);costs.append(dict(group=g,seed=seed,date=id[:8],extraCostTicksPerSide=cost,**row));cost_transitions+=episode_steps[id]
        for seed in SEEDS:
            validate_parity(read(root/f'parity-{g}-seed{seed}.json'),sha(models/g/f'policy-seed{seed}.onnx'),sha(models/g/f'policy-seed{seed}.manifest.json'),sha(models/g/f'parity-seed{seed}.json'))
            r=read(root/'evaluations'/f'{g}-seed{seed}'/'evaluation.json');timings.append(dict(group=g,seed=seed,**r['timing']))
            if r['modelSha256']!=sha(r['model']) or r['manifestSha256']!=sha(r['manifest']) or r['datasetSha256']!=sha(dataset):raise ValueError('C# frozen replay identity mismatch')
            if set(x['id'] for x in r['results'])!=set(read(dataset)):raise ValueError('Segment set mismatch')
            by_date={d:[] for d in DATES};manifest=read(r['manifest']);train_dates={e[:8] for e in manifest['trainingEpisodes']};cutoff=max(train_dates)
            for ep in r['results']:
                if ep['cacheSha256']!=sha(ep['path']) or ep['feedbackSha256']!=sha(ep['feedbackPath']):raise ValueError('C# segment/feedback changed')
                row,steps,last=feedback_audit(ep['feedbackPath'],ep['state'],True)
                if steps!=ep['steps']:raise ValueError('C# replay steps differ')
                by_date[ep['date']].append(row);segments.append(dict(group=g,seed=seed,date=ep['date'],id=ep['id'],status=last['status'],**row));transitions+=steps
            for date in DATES:
                row=aggregate_daily(by_date[date]);idx=indexes[date];eligible=idx['fullDayCoverage'] and date>cutoff
                initial=eligible and row['trades']>100 and (row['winRatePercent'] or 0)>33 and row['grossPnlJPY']>0
                daily.append(dict(group=g,seed=seed,date=date,usage='TrainingDateReplay' if date in train_dates else 'RetrospectiveBeforeTrainingCutoff' if date<=cutoff else 'DevelopmentPreviouslyInspected',fullDayCoverage=idx['fullDayCoverage'],targetEligible=eligible,initialTarget=initial,advancedTarget=initial and row['netPnlJPY']>0,**row))
                match=[c for c in costs if c['group']==g and c['seed']==seed and c['date']==date and c['extraCostTicksPerSide']==0]
                if match:
                    for k in ['trades','grossPnlJPY','netPnlJPY','costsJPY','wins','losses']:
                        if not equal_number(row[k],match[0][k]):raise ValueError('Python/frozen C# daily mismatch:'+k)
    if len(daily)!=105 or len(costs)!=90 or len({(r['group'],r['seed'],r['date']) for r in daily})!=105:raise ValueError('Daily keys missing')
    primary_summary={g:aggregate_daily([r for r in daily if r['group']==g and r['targetEligible']]) for g in GROUPS}
    result=dict(dates=DATES,variants=GROUPS,seeds=SEEDS,models=15,dailyRows=105,costRows=90,feedbackTransitions=transitions,costFeedbackTransitions=cost_transitions,targetEligibleRows=sum(r['targetEligible'] for r in daily),initialPassed=sum(r['initialTarget'] for r in daily),advancedPassed=sum(r['advancedTarget'] for r in daily),deploymentEligible=False,primaryDevelopmentSummary=primary_summary,daily=daily,costs=costs,segments=segments,timings=timings)
    write(root/'daily-performance.json',result)
    with (root/'daily-performance.csv').open('w',encoding='utf-8-sig',newline='') as file:
        writer=csv.DictWriter(file,fieldnames=list(daily[0]));writer.writeheader();writer.writerows(daily)
    fmt=lambda x:'—' if x is None else f'{x:,.2f}'
    lines=['# 日經微型：瓶頸修正後對照回測','', '1ms 延遲、10 秒冷卻、原始 40 維、每筆 72 JPY 基本成本。所有數字為已完成交易；最後未完成持倉與進場費對稱排除。','', 'P0：方向代理、扣費獎勵；E0：相同 1,754 時點改為可執行淨損益標籤；E1：3 個訓練日的全部 8,863 個完成結果樣本；E2：加入 9/30，4 個訓練日、12,226 個完成結果樣本；M0：E2 同資料／目標，加上成交後恢復實際行情標記價的回饋時序修正。每組種子 11／22／33、102,400 PPO 步數，不用驗證日選參數。',
        '', f'完整且晚於訓練截止日的 30 個模型／日期案例：初步目標 {result["initialPassed"]}/30、進階目標 {result["advancedPassed"]}/30。開發日期 10/6、10/7 已反覆檢視，不能稱全新樣本外。','', '## 10/6、10/7 開發日組別比較','', '以下合計跨 3 種子，用於比較實驗工作量，不能視為單一策略組合績效。','', '| 組別 | 交易筆數 | 勝率 % | 毛損益 JPY | 淨損益 JPY | 平均獲利 | 平均損失 |','|---|---:|---:|---:|---:|---:|---:|']
    for g,row in primary_summary.items():lines.append(f'| {g} | {row["trades"]} | {fmt(row["winRatePercent"])} | {fmt(row["grossPnlJPY"])} | {fmt(row["netPnlJPY"])} | {fmt(row["averageWinJPY"])} | {fmt(row["averageLossJPY"])} |')
    for date in DATES:
        lines+=['',f'## {date} 每日績效','', '| 模型／種子 | 交易筆數 | 勝率 % | 毛損益 JPY | 淨損益 JPY | 平均獲利 | 平均損失 | 排除持倉數 | 用途 |','|---|---:|---:|---:|---:|---:|---:|---:|---|']
        for row in [x for x in daily if x['date']==date]:lines.append(f'| {row["group"]}/{row["seed"]} | {row["trades"]} | {fmt(row["winRatePercent"])} | {fmt(row["grossPnlJPY"])} | {fmt(row["netPnlJPY"])} | {fmt(row["averageWinJPY"])} | {fmt(row["averageLossJPY"])} | {row["excludedOpenPositions"]} | {row["usage"]};完整={row["fullDayCoverage"]} |')
    lines+=['','## 成本壓力','','每邊再加 0／1／2 tick 成本（每 tick50JPY）；全部逐模型／種子／日期結果存於 JSON。模型凍結，狀態中的淨損益隨成本改變，因此成交筆數可能不同。','', '| 組別 | 每邊額外 tick | 交易筆數 | 毛損益 JPY | 淨損益 JPY |','|---|---:|---:|---:|---:|']
    for g in GROUPS:
        for cost in [0,1,2]:
            row=aggregate_daily([x for x in costs if x['group']==g and x['extraCostTicksPerSide']==cost]);lines.append(f'| {g} | {cost} | {row["trades"]} | {fmt(row["grossPnlJPY"])} | {fmt(row["netPnlJPY"])} |')
    lines+=['','## 推論耗時','','| 模型／種子 | actor p99 ms | 最大 ms | >1ms 次數 |','|---|---:|---:|---:|']
    for t in timings:lines.append(f'| {t["group"]}/{t["seed"]} | {t["p99Ms"]:.4f} | {t["maxMs"]:.4f} | {t["countAbove1Ms"]} |')
    lines+=['', '最多 3 程序並行；actor 時間不含特徵處理、排程及券商網路。固定 1ms 成交假設不是端到端延遲保證。各模型 Python／C# 1,200 筆動作及機率核對，總計 18,000 筆；另核對全部主要／成本 feedback 成交與共用帳本。P0/E0/E1/E2 以未修正時序的舊執行器作診斷控制組；M0 全程使用已修正執行器。']
    (root/'daily-performance.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    write(root/'verification.json',dict(primaryFeedbackTransitions=transitions,costFeedbackTransitions=cost_transitions,dailyRows=105,costRows=90,allFrozenArtifactsUnchanged=True,pythonCsharpDailyParity=True,sharedLedgerFillsReconciled=True,pairedInitialObservationsIdentical=True,markRepairInitialObservationsAndClosedTargetsIdentical=True,review='Pending'))
    print(json.dumps({g:primary_summary[g] for g in GROUPS},ensure_ascii=False),flush=True)

if __name__=='__main__':main()
