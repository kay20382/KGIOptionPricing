"""User objective: daily hard constraints before gross expectancy ranking."""
import argparse
import csv
import json
from pathlib import Path
from resume_contract import select_iteration_parent

TARGET = dict(minTradesPerDay=50,minNetWinRatePercent=40,minExpectedGrossPnlPerTradeJPY=50)

def assess_day(row, eligible):
    result=dict(row)
    trades=row['trades']; wins=row['wins']; gross=row['grossPnlJPY']
    if trades<0 or not 0<=wins<=trades:
        raise ValueError('Invalid completed trade counts')
    expectation=gross/trades if trades else None
    reasons=[]
    if not eligible or not row['fullDayCoverage']:reasons.append('NotCompleteValidationDay')
    if trades<TARGET['minTradesPerDay']:reasons.append('TradesBelow50')
    if not trades or wins*100<trades*TARGET['minNetWinRatePercent']:reasons.append('NetWinRateBelow40Percent')
    if not trades or gross<trades*TARGET['minExpectedGrossPnlPerTradeJPY']:reasons.append('GrossExpectancyBelow50JPY')
    result.update(expectedGrossPnlPerTradeJPY=expectation,objectiveEligible=bool(eligible and row['fullDayCoverage']),
                  hardTargetPass=not reasons,hardTargetReasons=';'.join(reasons))
    return result

def rank_candidates(rows, required_dates):
    if not required_dates or len(set(required_dates))!=len(required_dates):raise ValueError('Explicit unique validation dates required')
    primary=[r for r in rows if r['extraCostTicksPerSide']==0 and r['date'] in required_dates]
    keys=[(r['group'],r['seed'],r['date']) for r in primary]
    if len(set(keys))!=len(keys):raise ValueError('Duplicate candidate day')
    candidates=[]
    for group,seed in sorted({k[:2] for k in keys}):
        data=[assess_day(r,True) for r in primary if r['group']==group and r['seed']==seed]
        missing=sorted(set(required_dates)-{r['date'] for r in data})
        qualified=not missing and all(r['hardTargetPass'] for r in data)
        trades=sum(r['trades'] for r in data); gross=sum(r['grossPnlJPY'] for r in data)
        expectations=[r['expectedGrossPnlPerTradeJPY'] for r in data]
        candidates.append(dict(group=group,seed=seed,qualified=qualified,requiredDays=len(required_dates),
             passedDays=sum(r['hardTargetPass'] for r in data),missingDates=missing,
             totalTrades=trades,grossPnlJPY=gross,netPnlJPY=sum(r['netPnlJPY'] for r in data),
             expectedGrossPnlPerTradeJPY=gross/trades if trades else None,
             worstDayExpectedGrossPnlPerTradeJPY=min(expectations) if expectations and all(x is not None for x in expectations) else None,
             minimumDayTrades=min(r['trades'] for r in data),
             minimumDayNetWinRatePercent=min(r['winRatePercent'] or 0 for r in data)))
    ranking=sorted([r for r in candidates if r['qualified']],key=lambda r:(-r['expectedGrossPnlPerTradeJPY'],-r['worstDayExpectedGrossPnlPerTradeJPY'],r['group'],r['seed']))
    for rank,row in enumerate(ranking,1):row['qualifiedRank']=rank
    try:research_leader=select_iteration_parent(candidates)
    except ValueError:research_leader=None
    return dict(targets=TARGET,requiredDates=required_dates,candidates=candidates,qualifiedRanking=ranking,
                highestGrossExpectancyResearchCandidate=research_leader,
                selectedCandidate=ranking[0] if ranking else None,deploymentEligible=False)

def write_report(rows, required_dates, output):
    out=Path(output);out.mkdir(parents=True,exist_ok=True)
    daily=[assess_day(r,r['date'] in required_dates and r['extraCostTicksPerSide']==0) for r in rows]
    ranked=rank_candidates(daily,required_dates)
    report=dict(**ranked,daily=daily,definition='Sample gross expectancy = completed gross PnL / completed trades. Net winners define win rate; average win/loss are net.',
                status='QualifiedDevelopmentCandidates' if ranked['qualifiedRanking'] else 'NoQualifiedCandidate')
    (out/'objective-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    with (out/'daily-performance.csv').open('w',encoding='utf-8-sig',newline='') as f:
        fields=list(dict.fromkeys(k for r in daily for k in r));writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader();writer.writerows(daily)
    fmt=lambda x:'—' if x is None else f'{x:,.2f}'
    lines=['# 因子庫：每日硬性目標與每筆毛期望值','',
           '每日完成交易≥50筆、扣成本後勝率≥40%、每筆期望毛損益≥50JPY。每筆期望毛損益＝當日已完成交易毛損益÷筆數；零筆為無法計算。平均獲利／損失採扣成本後交易。','',
           '只有每個指定完整驗證日都通過的單一模型／種子才可排序；合格者以全部驗證交易的每筆毛期望值由高至低排序。未過關的候選不會因平均值較高而升級。','',
           '指定驗證日：'+', '.join(required_dates)+f'；合格模型 {len(ranked["qualifiedRanking"])}/{len(ranked["candidates"])}。這些日期已用於開發，未證明全新樣本外。','',
           '3秒判斷／15秒標籤、1ms動作延遲、10秒冷卻、無最長持倉、固定／最大150點、36JPY每邊；額外0／1／2跳成本明細保留於CSV。資料缺口末端未完成浮盈浮虧和進場費對稱排除。','',
           '|日期|組別|seed|完整日|交易筆數|淨勝率%|毛損益JPY|淨損益JPY|平均淨獲利|平均淨損失|每筆期望毛損益JPY|硬性過關|',
           '|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---|']
    for r in sorted([r for r in daily if r['extraCostTicksPerSide']==0],key=lambda r:(r['date'],r['group'],r['seed'])):
        lines.append(f'|{r["date"]}|{r["group"]}|{r["seed"]}|{r["fullDayCoverage"]}|{r["trades"]}|{fmt(r["winRatePercent"])}|{fmt(r["grossPnlJPY"])}|{fmt(r["netPnlJPY"])}|{fmt(r.get("averageWinJPY"))}|{fmt(r.get("averageLossJPY"))}|{fmt(r["expectedGrossPnlPerTradeJPY"])}|{r["hardTargetPass"]}|')
    lines+=['','1ms尚未涵蓋逐筆實測推論／特徵／排程／網路延遲；strict-cross按委託價成交，150點不是實際最大損失保證。目標為研究驗收門檻，不是獲利保證。']
    leader=ranked['highestGrossExpectancyResearchCandidate']
    if leader:lines+=['',f'最高毛期望研究起點：{leader["group"]} seed{leader["seed"]}，{fmt(leader["expectedGrossPnlPerTradeJPY"])} JPY／筆；此研究排序不代表通過每日硬性目標。']
    (out/'objective-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--runs',required=True);p.add_argument('--output',required=True);p.add_argument('--dates',nargs='+',required=True);a=p.parse_args()
    rows=json.loads(Path(a.runs).read_text(encoding='utf-8-sig'))['daily']
    report=write_report(rows,a.dates,a.output)
    print(json.dumps(dict(status=report['status'],candidates=len(report['candidates']),qualified=len(report['qualifiedRanking']))))
