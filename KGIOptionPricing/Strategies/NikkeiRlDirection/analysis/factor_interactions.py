"""Causal interaction screening of existing computable factors, never validation PnL."""
import numpy as np

def market_inputs(x,include_flow):
    x=np.asarray(x,dtype=np.float64)
    if x.ndim!=2 or x.shape[1]!=60 or not np.isfinite(x).all():
        raise ValueError('Finite raw60 market inputs required')
    indices=list(range(30))+list(range(40,60))
    if not include_flow:indices=[i for i in indices if i not in range(48,54)]
    return x[:,indices].copy(),indices

def temporal_folds(episodes):
    if len(episodes)!=4 or len(set(episodes))!=4 or any(b[:8]<=a[:8] for a,b in zip(episodes,episodes[1:])):
        raise ValueError('Four distinct chronological training episodes required')
    return [(episodes[:i],episodes[i]) for i in [2,3]]

def retained(improvements,lower):
    return len(improvements)==2 and min(improvements)>0 and np.mean(improvements)>=.005 and lower>0

def block_lower_bound(deltas,blocks,replicates=399):
    if len(deltas)!=2 or len(blocks)!=2:raise ValueError('Two temporal folds required')
    stats=[]
    for delta,block in zip(deltas,blocks):
        delta=np.asarray(delta);block=np.asarray(block)
        if delta.shape!=block.shape or delta.ndim!=1 or not np.isfinite(delta).all():raise ValueError('Invalid block scores')
        keys=np.unique(block)
        if len(keys)<5:raise ValueError('Too few independent time blocks')
        stats.append(np.array([(delta[block==k].sum(),(block==k).sum()) for k in keys]))
    rng=np.random.default_rng(20261009);results=[]
    for _ in range(replicates):
        scores=[]
        for stat in stats:
            sample=stat[rng.integers(0,len(stat),len(stat))].sum(0)
            scores.append(sample[0]/sample[1])
        results.append(np.mean(scores))
    return float(np.percentile(results,5))
