"""Screen the supplied 20 computable factors on training dates only."""
import argparse,json
from pathlib import Path
import numpy as np
from predictive_pruning import fit_feature_losses,retained_by_rule
from observation_schema import feature_names
from expanded_daily_backtest import sha,read

def screen(path,output):
    dates=['20260928','20260929','20260930','20261005']
    samples={d:[] for d in dates}
    manifest=read(Path(str(path)+'.manifest.json'))
    if manifest['labelSha256']!=sha(path) or {e[:8] for e in manifest['trainingEpisodes']}!=set(dates):
        raise ValueError('Training-only label source mismatch')
    with Path(path).open(encoding='utf-8') as reader:
        for line in reader:
            row=json.loads(line);date=row['episode'][:8];r=row['example']
            if date not in samples:raise ValueError('Validation data in training screen')
            samples[date].append((r['observation'][40:60],r['action']))
    arrays=[(np.asarray([r[0] for r in samples[d]],dtype=np.float32),np.asarray([r[1] for r in samples[d]],dtype=np.int64)) for d in dates]
    folds=[];constants=[]
    for test_index in [2,3]:
        x=np.concatenate([a[0] for a in arrays[:test_index]]);y=np.concatenate([a[1] for a in arrays[:test_index]])
        delta,constant=fit_feature_losses(x,y,*arrays[test_index]);folds.append(delta);constants.append(constant)
    # Blocks are30seconds of3s decisions. A training-only screen, not proof of alpha.
    rng=np.random.default_rng(20261009);bootstrap=[]
    for _ in range(399):
        means=[]
        for delta in folds:
            n=len(delta);starts=rng.integers(0,n,size=int(np.ceil(n/10)))
            indices=((starts[:,None]+np.arange(10))%n).reshape(-1)[:n]
            means.append(delta[indices].mean(0))
        bootstrap.append(np.mean(means,axis=0))
    lower=np.percentile(bootstrap,5,axis=0);names=feature_names(60,'mini-micro-research');diagnostics=[];kept=[]
    for k in range(20):
        improvement=[float(d[:,k].mean()) for d in folds]
        keep=retained_by_rule(improvement,float(lower[k])) and not any(c[k] for c in constants)
        if keep:kept.append(k+40)
        diagnostics.append(dict(index=k+40,name=names[k+40],foldImprovement=improvement,
                          bootstrapLower=float(lower[k]),retained=bool(keep)))
    result=dict(usage='TrainingOnlyFactorScreen',dates=dates,holdoutTrainingDates=dates[2:],
         rule='Both temporal folds improve; mean balanced logloss improvement>=0.005;30s block bootstrap90% lower>0.',
         target='Existing executable15second action labels; predictive proxy only. Final selection uses daily hard constraints and gross expectancy.',
         labelSha256=sha(path),retainedIndices=kept,excludedIndices=sorted(set(range(40,60))-set(kept)),diagnostics=diagnostics)
    Path(output).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--labels',required=True);p.add_argument('--output',required=True);a=p.parse_args()
    result=screen(Path(a.labels),a.output);print(json.dumps(dict(retained=result['retainedIndices'],excluded=result['excludedIndices'])))
