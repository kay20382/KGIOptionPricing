"""Evidence-first RL diagnostics. Does not mutate inputs, parameters or submit orders."""
import argparse,collections,gzip,hashlib,json,math
from pathlib import Path
import numpy as np
from completed_daily import completed_segment
from fill_diagnostics import fill_outcomes

def sha(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as f:
        for part in iter(lambda:f.read(1048576),b''):h.update(part)
    return h.hexdigest().upper()
def edge_attribution(rows):
    n=len(rows);gross=sum(x['grossPnlJPY'] for x in rows);cost=sum(x['costsJPY'] for x in rows);net=sum(x['netPnlJPY'] for x in rows)
    return dict(trades=n,grossPnlJPY=gross,costsJPY=cost,netPnlJPY=net,
        grossPerTradeJPY=gross/n if n else None,costPerTradeJPY=cost/n if n else None,netPerTradeJPY=net/n if n else None,
        requiredAdditionalGrossPerTradeJPY=-net/n if n else None,grossEdgePositive=gross>0,
        grossWinRatePercent=100*sum(x['grossPnlJPY']>0 for x in rows)/n if n else None,
        netWinRatePercent=100*sum(x['netPnlJPY']>0 for x in rows)/n if n else None,
        smallGrossWinnersLostToFees=sum(0<x['grossPnlJPY']<=x['costsJPY'] for x in rows),
        averageHoldingSeconds=sum(x['holdingSeconds'] for x in rows)/n if n and 'holdingSeconds' in rows[0] else None,
        exits=dict(collections.Counter(x.get('exitRole','Unknown') for x in rows)))
def funnel_row(feedback,counts):
    out=collections.Counter(counts);state=feedback['next'];mask=feedback['mask']
    out['decisions']+=1
    if all(mask[1:3]):
        out['eligibleFlatDecisions']+=1
        out['eligibleWaitDecisions']+=state['effectiveAction']==0
    out['entryIntentions']+=state['effectiveAction'] in [1,2]
    out['cancelIntentions']+=state['effectiveAction']==4
    out['flattenIntentions']+=state['effectiveAction']==3
    out['entryFills']+=sum(x['role'] in [0,1] for x in state['fills'])
    if state['overrideReason']:out['override:'+state['overrideReason']]+=1
    return dict(out)

def temporal_probes(labels):
    import torch
    torch.set_num_threads(1)
    episodes=sorted({r['episode'] for r in labels});reports=[]
    groups={'jointMarket30':list(range(30)),'miniMcReturnGap':[0,10],'timeOnly':[39]}
    for target in ['proxy','executable']:
        for fold in [1,2]:
            train=[r for r in labels if r['episode'] in episodes[:fold]];test=[r for r in labels if r['episode']==episodes[fold]]
            target_key='proxyAction' if target=='proxy' else 'action'
            y=np.array([r['example'][target_key] for r in train]);vy=np.array([r['example'][target_key] for r in test]);x=np.array([r['example']['observation'] for r in train]);vx=np.array([r['example']['observation'] for r in test])
            if min(np.bincount(y,minlength=3))==0 or min(np.bincount(vy,minlength=3))==0:raise ValueError('Temporal probe lacks a class')
            for group,indices in groups.items():
                torch.manual_seed(20261008);tx=x[:,indices];testx=vx[:,indices]
                mean=tx.mean(0);std=np.maximum(tx.std(0),1e-5)
                a=torch.tensor(np.clip((tx-mean)/std,-4,4),dtype=torch.float32);b=torch.tensor(np.clip((testx-mean)/std,-4,4),dtype=torch.float32)
                model=torch.nn.Sequential(torch.nn.Linear(len(indices),32),torch.nn.Tanh(),torch.nn.Linear(32,3))
                weights=torch.tensor(len(y)/(3*np.bincount(y,minlength=3)),dtype=torch.float32)
                optimizer=torch.optim.Adam(model.parameters(),lr=.003,weight_decay=.01)
                for _ in range(200):
                    loss=torch.nn.functional.cross_entropy(model(a),torch.tensor(y,dtype=torch.int64),weight=weights)
                    optimizer.zero_grad();loss.backward();optimizer.step()
                with torch.no_grad():
                    logits=model(b);logp=torch.log_softmax(logits,dim=1).numpy();pred=logits.argmax(1).numpy()
                perclass=[float(-logp[vy==k,k].mean()) for k in range(3)];balanced_loss=float(np.mean(perclass));balanced_acc=float(np.mean([np.mean(pred[vy==k]==k) for k in range(3)]))
                reports.append(dict(target=target,fold=fold,trainEpisodes=episodes[:fold],testEpisode=episodes[fold],group=group,trainSamples=len(y),testSamples=len(vy),balancedLogLoss=balanced_loss,improvementVsUniform=math.log(3)-balanced_loss,balancedAccuracyPercent=100*balanced_acc))
    return reports

def main():
    p=argparse.ArgumentParser();p.add_argument('--runs',required=True);p.add_argument('--output',required=True);p.add_argument('--labels');p.add_argument('--mode',choices=['logic','parameters','all'],default='all');args=p.parse_args()
    runs=Path(args.runs).resolve();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=False)
    report=json.loads((runs/'daily-performance.json').read_text(encoding='utf-8'));verification=json.loads((runs/'verification.json').read_text(encoding='utf-8'))
    if sha(runs/'daily-performance.json')!=verification['finalReportHashes']['daily-performance.json']:raise ValueError('Reviewed daily report hash changed')
    groups=collections.defaultdict(list);funnels={};sources={str(runs/'daily-performance.json'):sha(runs/'daily-performance.json')}
    for directory in sorted((runs/'evaluations').iterdir()):
        evaluation=directory/'evaluation.json';r=json.loads(evaluation.read_text(encoding='utf-8'));sources[str(evaluation)]=sha(evaluation)
        if sha(r['model'])!=r['modelSha256'] or sha(r['manifest'])!=r['manifestSha256']:raise ValueError('Frozen model identity mismatch')
        group=directory.name.split('-seed')[0];counts={}
        for segment in r['results']:
            completed_segment(segment['state'],segment['fills'])
            closed=segment['fills'][:-1] if segment['state']['position'] else segment['fills']
            groups[group]+=fill_outcomes(closed)
            if sha(segment['feedbackPath'])!=segment['feedbackSha256']:raise ValueError('Feedback changed')
            with gzip.open(segment['feedbackPath'],'rt',encoding='utf-8') as f:
                for line in f:counts=funnel_row(json.loads(line),counts)
        counts['entryFillRatePercent']=100*counts.get('entryFills',0)/counts.get('entryIntentions',1) if counts.get('entryIntentions') else None
        counts['eligibleWaitPercent']=100*counts.get('eligibleWaitDecisions',0)/counts.get('eligibleFlatDecisions',1) if counts.get('eligibleFlatDecisions') else None
        funnels[directory.name]=counts
        print(f'Audited {directory.name}',flush=True)
    edges={g:edge_attribution(rows) for g,rows in groups.items()}
    health=[]
    for date,idx in report['dataCoverage'].items():
        frames=sum(s['frames'] for s in idx['segments'] if not s['excluded']);valid=sum(s['validFrames'] for s in idx['segments'] if not s['excluded'])
        health.append(dict(date=date,fullDayCoverage=idx['fullDayCoverage'],frames=frames,healthyFrames=valid,healthyPercent=100*valid/frames,dataGapFrames=idx['dataGapFrames'],first=idx['first'],last=idx['last']))
    target_report=None;probes=[]
    if args.labels:
        path=Path(args.labels);meta=json.loads(Path(str(path)+'.manifest.json').read_text(encoding='utf-8'))
        if sha(path)!=meta['labelSha256']:raise ValueError('Labels hash changed')
        from executable_warmstart import validate_example
        labels=[];confusion=np.zeros((3,3),dtype=int);proxy_filled=[];proxy_unfilled=0;all_count=0
        with path.open(encoding='utf-8') as f:
            for line in f:
                row=json.loads(line);example=row['example'];validate_example(example);all_count+=1
                if example['proxyAction'] is None:continue
                labels.append(row);proxy=example['proxyAction'];confusion[proxy,example['action']]+=1
                if proxy in [1,2]:
                    result=example['long' if proxy==1 else 'short']
                    if result['completed']:proxy_filled.append(result['state']['completedNetPnl'])
                    else:proxy_unfilled+=1
        target_report=dict(allEligibleKnownExamples=all_count,matchedSamples=len(labels),originalIndependentTrainDates=len(meta['trainingEpisodes']),trainingEpisodes=meta['trainingEpisodes'],classCountsProxy=confusion.sum(1).tolist(),classCountsExecutable=confusion.sum(0).tolist(),proxyRowsExecutableColumnsConfusion=confusion.tolist(),targetDisagreementPercent=100*(1-np.trace(confusion)/confusion.sum()),proxyEntryFilled=len(proxy_filled),proxyEntryUnfilled=proxy_unfilled,proxyEntryFillRatePercent=100*len(proxy_filled)/(len(proxy_filled)+proxy_unfilled),proxyFilledNetWinRatePercent=100*sum(x>0 for x in proxy_filled)/len(proxy_filled),proxyFilledMeanNetJPY=float(np.mean(proxy_filled)),matchedSamplesOverlapping=True,oracleNotTradablePortfolio=True)
        probes=temporal_probes(labels);sources[str(path.resolve())]=sha(path)
    data=dict(schema='nikkei-rl-root-cause-v1',edgeAttribution=edges,funnels=funnels,dataHealth=health,targets=target_report,trainingOnlyTemporalProbes=probes,sources=sources,scope='Cross-seed totals diagnose workload; not one combined strategy. Probes train-only, narrow model tests, not proof of universal predictive absence.',deploymentEligible=False)
    (out/'analysis-report.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    lines=['# 日經微型 RL 瓶頸診斷','', '既有 7 日回測；跨種子合計僅供診斷，不能視為單一策略績效。','', '| 模型群 | 完成交易 | 每筆毛損益 JPY | 每筆成本 | 每筆淨損益 | 毛勝率 % | 淨勝率 % | 平均持有秒 |','|---|---:|---:|---:|---:|---:|---:|---:|']
    fmt=lambda x:'—' if x is None else f'{x:,.2f}'
    for g,r in edges.items():lines.append(f'| {g} | {r["trades"]} | {fmt(r["grossPerTradeJPY"])} | {fmt(r["costPerTradeJPY"])} | {fmt(r["netPerTradeJPY"])} | {fmt(r["grossWinRatePercent"])} | {fmt(r["netWinRatePercent"])} | {fmt(r["averageHoldingSeconds"])} |')
    lines+=['','## 資料處理與長度','','缺口、排序、完整日與訓練用途沿用已審查回測的標示，未重排、補價或合成平倉。','', '| 日期 | 健康影格 % | 缺口影格 | 完整日 |','|---|---:|---:|---|']
    for h in health:lines.append(f'| {h["date"]} | {h["healthyPercent"]:.2f} | {h["dataGapFrames"]} | {h["fullDayCoverage"]} |')
    lines+=['','只有 3 個獨立訓練日期；重疊 30 秒標籤與 102,400 個 PPO 步數反覆使用行情，不能當成 102,400 個獨立樣本。7 日績效中也包含訓練日與較早日期，沒有全新保留集。資料長度限制可信度，單純增加迭代步數無法證明改善。','', '## 決策漏斗','','| 模型／種子 | 可進場決策 | 其中等待 % | 有效進場意圖 | 進場成交 | 成交率 % |','|---|---:|---:|---:|---:|---:|']
    for model,r in funnels.items():lines.append(f'| {model} | {r.get("eligibleFlatDecisions",0)} | {fmt(r.get("eligibleWaitPercent"))} | {r.get("entryIntentions",0)} | {r.get("entryFills",0)} | {fmt(r.get("entryFillRatePercent"))} |')
    if target_report:
        t=target_report;lines+=['','## 訓練目標對齊','',f'相同 {t["matchedSamples"]} 個訓練決策時點，方向代理與可執行淨損益標籤的差異率 {t["targetDisagreementPercent"]:.2f}%。代理建議進場的 {t["proxyEntryFilled"]+t["proxyEntryUnfilled"]} 次反事實意圖，成交率 {t["proxyEntryFillRatePercent"]:.2f}%；成交者扣費勝率 {t["proxyFilledNetWinRatePercent"]:.2f}%，每筆平均淨損益 {t["proxyFilledMeanNetJPY"]:.2f} JPY。',
            '', '新標籤以既有 C# 共用成交／風控／帳本，逐時點分別模擬多空，30 秒後以真實後續成交平倉；包含限價未成交、進場費、固定停損及出場費。只用決策當下觀測當輸入；未完成尾部排除。反事實樣本互相重疊，其理想結果不是可交易投資組合。',
            '', '## 特徵：訓練日時間外推診斷','', '| 目標 | 折 | 特徵組 | 訓練筆数 | 測試筆數 | 平衡準確率 % | 相對均勻模型 log-loss 改善 |','|---|---:|---|---:|---:|---:|---:|']
        for x in probes:lines.append(f'| {x["target"]} | {x["fold"]} | {x["group"]} | {x["trainSamples"]} | {x["testSamples"]} | {x["balancedAccuracyPercent"]:.2f} | {x["improvementVsUniform"]:.4f} |')
    lines+=['','單變量篩選未通過，只表示既定單變量／時距／資料條件下未找到穩定改善，不能據此斷言其他特徵沒有聯合預測能力。本輪恢復原始 40 維契約，使用同條件 P0 方向代理與 E0 可執行目標對照，不用驗證日績效重選特徵。',
            '', 'B0/C0 連毛損益已為負，因此不是單純降低費用就能修復。N 的小幅毛利不足以覆蓋 72 JPY 成本，R/N5 在可進場條件下持續等待須與訊號不存在／學習退化分開解讀。冷卻、資料健康與保護停損不為衝交易筆數而放寬。']
    (out/'analysis-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(dict(edges=edges,targets=target_report),ensure_ascii=False),flush=True)

if __name__=='__main__':main()
