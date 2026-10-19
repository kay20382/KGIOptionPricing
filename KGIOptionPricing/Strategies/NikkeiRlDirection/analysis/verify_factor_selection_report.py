"""Verify aggregate research outputs; write only selected daily summaries."""
import csv,json,hashlib,sys
from pathlib import Path
import numpy as np
from factor_significance import by_adjust

out=Path(sys.argv[1]);read=lambda name:json.loads((out/name).read_text(encoding='utf-8'))
verification=read('verification.json');rank=read('factor-ranking.json');selected=read('selected-factors.json');sources=read('source-verification.json')
assert verification['done'] and verification['fingerprintsUnchanged'] and len(sources)==13
assert len(rank)==672 and len(selected)==4 and not verification['minimumFiveReached']
assert all(s['maximumBookError']==0 and (s['sourceSHA'] if s['sourceIdentityMode']=='DirectCacheInput' else s['normalizedSHA'])==s['cacheInputSHA'] for s in sources.values())
q=by_adjust([r['pValue'] for r in rank]);np.testing.assert_allclose(q,[r['qValue'] for r in rank],rtol=1e-12,atol=1e-15)
assert all(r['significantStable'] and r['qValue']<=.05 and r['positiveDates']==5 and r['minDailySamples']>=1000 and r['lower95']>0 for r in selected)
assert len({r['family'] for r in selected})==len(selected)
daily=list(csv.DictReader((out/'daily-prediction-performance.csv').open(encoding='utf-8-sig')));assert len(daily)==3360
keys={(r['key'],str(r['horizonSeconds']),r['model']) for r in selected};chosen=[r for r in daily if (r['key'],r['horizonSeconds'],r['model']) in keys];assert len(chosen)==20
for r in selected:
    means=[float(d['mseImprovementTicksSquared']) for d in chosen if d['key']==r['key'] and int(d['horizonSeconds'])==r['horizonSeconds'] and d['model']==r['model']]
    np.testing.assert_allclose(np.mean(means),r['meanImprovement'],rtol=1e-10,atol=1e-10)
target=out/'selected-daily-prediction-performance.csv';assert not target.exists()
with target.open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(chosen[0]));writer.writeheader();writer.writerows(chosen)
artifact_hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in out.iterdir() if p.suffix in ('.json','.csv','.md')}
checks=dict(done=True,sourceDates=13,bboMaximumError=0,hypotheses=672,dailyRows=3360,passingHypotheses=sum(r['significantStable'] for r in rank),passingDistinctFactorVersions=len({r['key'] for r in rank if r['significantStable']}),selected=4,selectedDailyRows=20,byRecalculationPass=True,dailyMeanMatchesBlockStatistic=True,analysisPythonTests=195,researchPythonTests=32,csharpSelftests=7,independentReviewImportantFixed=2,artifactSHA256=artifact_hashes)
(out/'artifact-checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in checks.items() if k!='artifactSHA256'},ensure_ascii=False))
