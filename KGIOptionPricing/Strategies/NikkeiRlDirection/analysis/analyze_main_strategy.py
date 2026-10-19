"""Frozen main-application actor: compact shared-ledger reports, no model changes."""
import argparse,csv,json,math
from pathlib import Path

def summarize(manifest,summary):
    if manifest.get('strategyType')!='NikkeiRlDirection':raise ValueError('策略類型不符')
    parameters=manifest.get('effectiveParameters',manifest.get('parameters',{}))
    if parameters.get('ModelId') not in ('twelve-training-control-seed22','pruned-earlier-seed22'):raise ValueError('模型參數不符')
    for key in ('completedTrades','grossPnl','netPnl'):
        if not isinstance(summary.get(key),(int,float)) or not math.isfinite(summary[key]):raise ValueError('缺少績效欄位：'+key)
    n=summary['completedTrades']
    if n<0 or int(n)!=n:raise ValueError('交易筆數無效')
    product=next((p for p in manifest.get('universe',[]) if p.get('productId')=='225MC'),None)
    if product is None or product.get('contractMultiplier')!=10:raise ValueError('商品規格不符')
    def jpy(key):
        value=summary.get(key)
        return value*10 if isinstance(value,(int,float)) else None
    expectancy=summary['grossPnl']/n if n else None
    reasons=[]
    if n<50:reasons.append('交易不足50筆')
    if summary.get('winRate') is None or summary['winRate']<40:reasons.append('淨勝率不足40%')
    if expectancy is None or expectancy<50:reasons.append('每筆毛期望不足50JPY')
    reasons.append('主程式執行摘要不是完整逐日驗收證據')
    return dict(runId=manifest.get('runId'),model=parameters['ModelId'],trades=n,netWinRatePercent=summary.get('winRate'),grossPnlJPY=summary['grossPnl'],netPnlJPY=summary['netPnl'],averageNetWinJPY=jpy('averageWinningPoints'),averageNetLossJPY=jpy('averageLosingPoints'),expectedGrossPnlPerTradeJPY=expectancy,qualified=False,reasons=';'.join(reasons),parameters=parameters)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--runs',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--mode',choices=['all','logic','parameters'],default='all');args=parser.parse_args()
    manifests=sorted(args.runs.rglob('run-manifest.json'))
    if not manifests:raise ValueError('缺少run-manifest.json，不產生空績效')
    rows=[]
    for path in manifests:
        m=json.loads(path.read_text(encoding='utf-8-sig'))
        if m.get('strategyType')!='NikkeiRlDirection':continue
        rows.append(summarize(m,json.loads(path.with_name('run-summary.json').read_text(encoding='utf-8-sig'))))
    if not rows:raise ValueError('找不到日經RL執行摘要')
    args.output.mkdir(parents=True,exist_ok=True)
    (args.output/'analysis-report.json').write_text(json.dumps({'rows':rows,'mode':args.mode,'limitations':['缺少完整日、來源連續性及樣本外驗收，不能宣稱合格','未保存逐筆明細，不推測MFE/MAE或委託佇列','固定模型參數，不自動排序升級或修改模型']},ensure_ascii=False,indent=2),encoding='utf-8')
    fields=[k for k in rows[0] if k!='parameters']
    with (args.output/'performance.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=fields,extrasaction='ignore');writer.writeheader();writer.writerows(rows)
    lines=['# 日經RL主程式績效摘要','','觀察事實：只統計共用帳本已完成交易，排除未完成持倉成本。','','|執行|交易筆數|淨勝率%|毛損益JPY|淨損益JPY|平均淨獲利JPY|平均淨損失JPY|每筆毛期望JPY|','|---|---:|---:|---:|---:|---:|---:|---:|']
    for r in rows:
        values=[r.get(k) for k in ['runId','trades','netWinRatePercent','grossPnlJPY','netPnlJPY','averageNetWinJPY','averageNetLossJPY','expectedGrossPnlPerTradeJPY']]
        lines.append('|'+ '|'.join('--' if v is None else str(v) for v in values)+'|')
    lines+=['','分析推論：這些是執行摘要，未證明逐日穩健性。','邏輯修改建議：先與凍結離線模型按相同來源比較，未提供逐筆原因不推論因果。','參數建議：保留凍結參數，沒有相鄰參數實驗不推薦調整。','仍需驗證：完整日、資料連續性、樣本外及0/1/2跳成本；不得將報表壓力扣費視為重新撮合回測。']
    (args.output/'analysis-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
if __name__=='__main__':
    try:main()
    except (ValueError,OSError,json.JSONDecodeError) as exc:raise SystemExit(str(exc))
