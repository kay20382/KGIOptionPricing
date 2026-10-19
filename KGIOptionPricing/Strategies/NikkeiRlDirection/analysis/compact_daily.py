"""Aggregate C# summary-only reports. No trade reconstruction or per-step storage."""
from pathlib import Path
from expanded_daily_backtest import read,sha
from completed_daily import aggregate_daily
import math

def stop_contract(profile=None):
    result=dict(stopMode='fixed',fixedStopPoints=150,maxStopPoints=150)
    if profile is None:return result
    allowed={'stopMode','fixedStopPoints','minStopPoints','maxStopPoints','stopVolatilitySeconds','stopVolatilityMultiplier'}
    if set(profile)-allowed:raise ValueError('Stop profile cannot change execution/data/costs')
    result.update(profile)
    if result['stopMode'] not in ['fixed','adaptive-volatility'] or result['maxStopPoints']!=150:raise ValueError('Stop profile requires150point hard cap')
    fixed=result['fixedStopPoints'];minimum=result.get('minStopPoints',5)
    if any(not math.isfinite(v) or v<5 or v>150 or v%5 for v in [fixed,minimum]):raise ValueError('Stop distances must align with5point ticks within150')
    seconds=result.get('stopVolatilitySeconds',60);mult=result.get('stopVolatilityMultiplier',2)
    if not math.isfinite(seconds) or not 10<=seconds<=600 or not math.isfinite(mult) or not 0<mult<=10:raise ValueError('Invalid adaptive stop settings')
    return result

def entry_contract(profile=None):
    if profile is None:return dict(entryPriceMode='same-side')
    if set(profile)!={'entryPriceMode'} or profile['entryPriceMode'] not in ['same-side','opposite-touch']:raise ValueError('Entry profile must specify only supported strict-cross quote mode')
    return dict(profile)

def readiness_contract(meta,config,cache):
    profile=config.get('researchReadinessProfile','all-research')
    if profile not in ['all-research','exclude-flow'] or meta['environmentConfig'].get('researchReadinessProfile','all-research')!=profile or cache.get('researchReadinessProfile','all-research')!=profile:raise ValueError('Compact readiness model/config/cache mismatch')
    if (meta.get('schema')=='nikkei-rl-research-pruned-flow-v1')!=(profile=='exclude-flow'):raise ValueError('Compact readiness schema mismatch')
    isolated=cache.get('schema') in ['nikkei-rl-cache-pruned-flow-v1','nikkei-rl-cache-pruned-flow-v1-evaluation','nikkei-rl-cache-pruned-flow-v1-derived']
    if isolated!=(profile=='exclude-flow'):raise ValueError('Compact cache schema/readiness mismatch')
    if profile=='exclude-flow':
        excluded=meta.get('actorExcludedFeatures',[])
        if meta.get('observationSize')!=60 or config.get('featureProfile')!='mini-micro-research' or config.get('decisionIntervalSeconds')!=3 or len(set(excluded))!=len(excluded) or any(type(i)!=int or not 0<=i<60 for i in excluded) or not set(range(48,54)).issubset(excluded):raise ValueError('Compact readiness needs complete valid flow mask')

def compact_daily_rows(evaluation_path,dataset_path,model_path,indexes,group,seed,cost,stop_profile=None,entry_profile=None,trained_entry_mode='same-side'):
    report=read(evaluation_path);dataset=read(dataset_path);model=Path(model_path)
    if not report.get('summaryOnly') or not report.get('sharedLedgerReconciled'):raise ValueError('Summary-only shared ledger verification required')
    entry_contract(dict(entryPriceMode=trained_entry_mode))
    if trained_entry_mode!='same-side' and read(model.with_suffix('.manifest.json'))['environmentConfig'].get('entryPriceMode','same-side')!=trained_entry_mode:raise ValueError('Compact trained entry manifest differs')
    expected=dict(decisionLatencyMs=1,decisionIntervalSeconds=3,cooldownSeconds=10,maxHoldingSeconds=0,
        featureProfile='mini-micro-research',observationSize=60,stopMode='fixed',fixedStopPoints=150,maxStopPoints=150,
        commission=36,tickSize=5,multiplier=10,extraCostTicksPerSide=cost)
    expected.update(stop_contract(stop_profile))
    entry=entry_contract(entry_profile) if entry_profile is not None else dict(entryPriceMode=trained_entry_mode)
    if report['config'].get('entryPriceMode','same-side')!=entry['entryPriceMode']:raise ValueError('Compact entry price contract changed')
    if entry_profile is not None:
        if report.get('purpose')!='EntrySensitivityPreviouslyInspectedNotRetrained' or report.get('entryOverrideSha256')!=sha(report['entryOverridePath']):raise ValueError('Explicit entry sensitivity provenance required')
    if any(report['config'][k]!=v for k,v in expected.items()):raise ValueError('Compact trading contract changed')
    if report['modelSha256']!=sha(model) or report['manifestSha256']!=sha(model.with_suffix('.manifest.json')) or report['datasetSha256']!=sha(dataset_path):raise ValueError('Compact model/dataset identity changed')
    if len(report['results'])!=len(dataset) or {ep['id'] for ep in report['results']}!=set(dataset):raise ValueError('Compact episodes missing or duplicated')
    model_meta=read(model.with_suffix('.manifest.json'))
    segments={d:[] for d in indexes}
    for ep in report['results']:
        readiness_contract(model_meta,report['config'],read(ep['path']+'.manifest.json'))
        if not ep['sharedLedgerReconciled'] or ep['cacheSha256']!=sha(ep['path']) or ep['path']!=dataset[ep['id']]:raise ValueError('Compact episode/cache identity changed')
        if ep['wins']+ep['losses']+ep['breakeven']!=ep['trades'] or ep['winningNetJPY']+ep['losingNetJPY']!=ep['netPnlJPY'] or ep['grossPnlJPY']-ep['costsJPY']!=ep['netPnlJPY']:raise ValueError('Compact completed metrics inconsistent')
        segments[ep['date']].append(ep)
    return [dict(group=group,seed=seed,date=date,extraCostTicksPerSide=cost,
                 **(stop_contract(stop_profile) if stop_profile is not None else {}),
                 **(dict(entry,executionSensitivityNotRetrained=entry_profile is not None) if entry_profile is not None or trained_entry_mode!='same-side' else {}),
                 fullDayCoverage=indexes[date]['fullDayCoverage'],usage='PreviouslyInspectedDevelopment' if date>'20261005' else 'TrainingOrHistoricalReplay',
                 **aggregate_daily(values)) for date,values in segments.items() if values]
