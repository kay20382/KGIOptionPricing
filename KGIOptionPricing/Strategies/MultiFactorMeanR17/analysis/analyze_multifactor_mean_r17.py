"""R17 shared-artifact analysis; no replay, parameter writeback or orders."""
import argparse,importlib.util,json,sys,math
from pathlib import Path

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('r17_shared_analysis',HERE.parents[1]/'PowerReversal/analysis/analyze_power_reversal.py')
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
AnalysisError=base.AnalysisError

def validate_parameters(values):
    base.validate_parameters(values)
    ranges={
        'ResearchFactorMode':(1,8,True),'ResearchScoreThreshold':(.001,1000,False),
        'ResearchFlowFloor':(-1,1,False),'ResearchQueueFloor':(-1,1,False),
        'ResearchFactorConfirmSeconds':(0,300,False),'ResearchDecisionFrameMilliseconds':(100,1000,True),
        'ResearchVolatilityRiskMultiplier':(.01,10,False),'ResearchVolatilityRewardRatio':(.01,10,False),
        'ResearchVolatilityMinStopTicks':(1,30,True),'ResearchVolatilityMaxStopTicks':(1,30,True),
        'ResearchVolatilityMaxTargetTicks':(1,2000,True),'ResearchMinLargePrintRate':(0,100000,False),
        'ResearchMinLargeVariancePressure':(-1,1,False),'ResearchMinSignedMiniLastReturnTicks':(-1000,1000,False)}
    for k in ('ResearchRequireOfi','ResearchRequireMomentum','ResearchUseLargeVariancePressure','ResearchAnchorRiskToFill','ResearchUseMiniLastReturnFloor'):ranges[k]=(0,1,True)
    try:
        for k,(lo,hi,integer) in ranges.items():
            v=float(values[k])
            if not math.isfinite(v) or not lo<=v<=hi or integer and v!=int(v):raise ValueError(k)
        if float(values['ResearchVolatilityMinStopTicks'])>float(values['ResearchVolatilityMaxStopTicks']):raise ValueError('停損上下限')
        if values['SignalMode']!='1' or values['AdaptiveExitMode']!='0':raise ValueError('訊號或風險模式')
    except (KeyError,ValueError,TypeError) as error:raise AnalysisError('R17參數無效：'+str(error)) from error

def load_run(folder):
    return base.load_run(folder,strategy_type='MultiFactorMeanR17',parameter_validator=validate_parameters)

def analyze(runs,mode='all'):
    result=base.analyze(runs,mode,strategy_type='MultiFactorMeanR17')
    result['model']='round17/mean_mini_return_minus05_score025'
    result['researchEvidence']='原六日683筆，毛期望39.82JPY、淨期望-32.18JPY，僅3日達標；不代表整合版已完成多日或實盤驗證。'
    for report,run in zip(result['runs'],runs):
        p=run['manifest']['effectiveParameters']
        report['factorParameters']={k:v for k,v in p.items() if k.startswith('Research')}
        if report['logic']:
            report['logic']['recommendations']=['依R17.ScoreMagnitude、SignedQueue、SignedPower、MiniSignedCompletedReturn、LargeSignedVariancePressure、LargePrintRate分解漏斗；缺值不能補零。','先檢查每日期望值及成交成本，再評估均價回歸在趨勢行情中的逆勢風險。']
        report['limitations'].append('未通過逐日100筆／淨勝率>40%／毛期望>=50JPY的全日驗證，不自動推薦實盤。')
        report['status']='Incomplete'
    return result

def main():
    p=argparse.ArgumentParser();p.add_argument('--runs',required=True,type=Path);p.add_argument('--output',required=True,type=Path)
    p.add_argument('--mode',choices=('logic','parameters','all'),default='all');args=p.parse_args()
    try:
        folders=[args.runs] if (args.runs/'run-manifest.json').exists() else sorted({x.parent for x in args.runs.rglob('run-manifest.json')})
        if not folders:raise AnalysisError('找不到R17共用執行產物')
        result=analyze([load_run(f) for f in folders],args.mode)
        args.output.mkdir(parents=True,exist_ok=True)
        (args.output/'analysis-report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
        lines=['# 多因子均價回歸 R17 分析','','## 觀察事實',result['researchEvidence']]
        for r in result['runs']:lines+=['',r['runId'],json.dumps(r['metrics'],ensure_ascii=False),*r['limitations']]
        lines+=['','## 分析推論','缺少樣本外與相鄰參數證據時不推定穩健性。','','## 邏輯修改建議','依因子門檻拒絕分布與各日表現提出後續假說；本工具不更新程式。','','## 參數建議',result['rankingReason'],'','## 仍需驗證','現行框架整合後多日重播、模擬與券商成交；禁止把合成測試當市場績效。']
        (args.output/'analysis-report.md').write_text('\n'.join(lines),encoding='utf-8')
        print('R17分析完成；未自動改參數或下單。');return 0
    except AnalysisError as error:print(str(error),file=sys.stderr);return 1

if __name__=='__main__':raise SystemExit(main())
