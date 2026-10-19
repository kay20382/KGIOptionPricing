"""Train-only temporal predictive diagnostics; never selects using validation PnL."""
import argparse
import datetime
import gzip
import hashlib
import json
from pathlib import Path
import numpy as np
from direction_warmstart import parse_at
from observation_schema import feature_names

MARKET_INDICES=list(range(30))+list(range(40,56))

def retained_by_rule(improvements, lower_bound):
    return len(improvements)==2 and min(improvements)>0 and np.mean(improvements)>=.005 and lower_bound>0

def build_samples(states, horizon, threshold):
    if (horizon,threshold) not in [(5,0),(30,2)]: raise ValueError('Prespecified horizons only')
    times=[parse_at(s['at']) for s in states]
    if any(b<=a for a,b in zip(times,times[1:])): raise ValueError('Nonmonotonic states')
    observations=np.asarray([s['observation'] for s in states],dtype=np.float32)
    if observations.shape!=(len(states),56) or not np.isfinite(observations).all(): raise ValueError('Invalid56 inputs')
    lookup={t:i for i,t in enumerate(times)};xs=[];ys=[];ats=[]
    for i,(s,t) in enumerate(zip(states,times)):
        j=lookup.get(t+datetime.timedelta(seconds=horizon))
        if j is None or t.date()!=times[j].date(): continue
        if observations[i,30]!=0 or not all(s['mask'][1:3]) or observations[i,39]*3600<=310: continue
        if any(observations[k,38]!=1 or states[k].get('status','').startswith('Incomplete') for k in range(i,j+1)): continue
        change=observations[j,0 if horizon==5 else 2]
        xs.append(observations[i].copy());ys.append(1 if change>threshold else 2 if change < -threshold else 0);ats.append(s['at'])
    return np.asarray(xs,dtype=np.float32).reshape(-1,56),np.asarray(ys,dtype=np.int64),ats

def fit_feature_losses(train_x,train_y,test_x,test_y):
    """Independent cubic logistic probes, train-only scaling/class balancing."""
    import torch
    torch.set_num_threads(1)
    if any(np.bincount(y,minlength=3).min()==0 for y in [train_y,test_y]): raise ValueError('Missing direction class')
    mean=train_x.mean(0);std=train_x.std(0);constant=std<1e-8
    def inputs(x):
        z=np.clip((x-mean)/np.maximum(std,1e-6),-4,4)
        return torch.tensor(np.stack([np.ones_like(z),z,z*z,z*z*z],axis=2),dtype=torch.float32)
    x=inputs(train_x);target=torch.tensor(train_y)
    coefficient=torch.zeros((train_x.shape[1],4,3),requires_grad=True)
    weights=torch.tensor(len(train_y)/(3*np.bincount(train_y,minlength=3)),dtype=torch.float32)[target]
    optimizer=torch.optim.Adam([coefficient],lr=.03)
    for _ in range(250):
        logits=torch.einsum('nfd,fdc->nfc',x,coefficient)
        losses=-torch.log_softmax(logits,dim=2).gather(2,target[:,None,None].expand(-1,train_x.shape[1],1)).squeeze(2)
        loss=(losses*weights[:,None]).mean()+.001*coefficient[:,1:,:].square().mean()
        optimizer.zero_grad();loss.backward();optimizer.step()
    with torch.no_grad():
        logits=torch.einsum('nfd,fdc->nfc',inputs(test_x),coefficient)
        losses=-torch.log_softmax(logits,dim=2).gather(2,torch.tensor(test_y)[:,None,None].expand(-1,train_x.shape[1],1)).squeeze(2).numpy()
    losses[:,constant]=np.log(3)
    w=len(test_y)/(3*np.bincount(test_y,minlength=3))[test_y]
    return (np.log(3)-losses)*w[:,None],constant

def screen(samples):
    diagnostics=[];retained=set()
    for horizon in [5,30]:
        folds=[];constants=[]
        for test_index in [1,2]:
            tx=np.concatenate([s[horizon][0] for s in samples[:test_index]])[:,MARKET_INDICES]
            ty=np.concatenate([s[horizon][1] for s in samples[:test_index]])
            vx,vy=samples[test_index][horizon][:2]
            delta,constant=fit_feature_losses(tx,ty,vx[:,MARKET_INDICES],vy)
            folds.append(delta);constants.append(constant)
        rng=np.random.default_rng(20261008+horizon);bootstrap=[]
        # 30-second moving blocks reduce optimism from overlapping direction labels.
        for _ in range(399):
            means=[]
            for delta in folds:
                n=len(delta);starts=rng.integers(0,n,size=int(np.ceil(n/6)))
                indices=((starts[:,None]+np.arange(6))%n).reshape(-1)[:n]
                means.append(delta[indices].mean(0))
            bootstrap.append(np.mean(means,axis=0))
        lower=np.percentile(bootstrap,5,axis=0)
        for k,index in enumerate(MARKET_INDICES):
            improvements=[float(d[:,k].mean()) for d in folds]
            keep=retained_by_rule(improvements,float(lower[k])) and not any(c[k] for c in constants)
            if keep: retained.add(index)
            diagnostics.append(dict(index=index,name=feature_names(56)[index],horizonSeconds=horizon,
                foldBalancedLogLossImprovement=improvements,meanImprovement=float(np.mean(improvements)),
                bootstrap90PercentLower=float(lower[k]),constantInTrainingFold=bool(any(c[k] for c in constants)),retained=bool(keep)))
    return sorted(retained),diagnostics

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--bridge',required=True);parser.add_argument('--dataset',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args();out=Path(args.output);out.mkdir(parents=True,exist_ok=False)
    from rl_env import NikkeiRlEnv
    from feature_experiments import TRAIN,COHORT
    env=NikkeiRlEnv(args.bridge,args.dataset,TRAIN,dict(observationSize=56,cooldownSeconds=10,decisionLatencyMs=1,rewardCostWeight=0),training=True)
    samples=[];counts=[]
    try:
        for episode in TRAIN:
            env.reset(options={'episode':episode});states=[env.last]
            while True:
                _,_,terminated,truncated,info=env.step(0);states.append(info)
                if terminated or truncated:break
            with gzip.open(out/f'{episode}-states.json.gz','wt',encoding='utf-8') as stream:json.dump(states,stream,separators=(',',':'))
            entry={h:build_samples(states,h,0 if h==5 else 2) for h in [5,30]};samples.append(entry)
            counts.append(dict(episode=episode,horizons={str(h):dict(samples=len(entry[h][1]),classes=np.bincount(entry[h][1],minlength=3).tolist()) for h in [5,30]}))
    finally:env.close()
    kept,diagnostics=screen(samples)
    masks={'C0':list(range(40,56)),
        'R':sorted(set(MARKET_INDICES)-set(kept)|set(range(48,56))),
        'N':sorted(set(MARKET_INDICES)-set(kept))}
    dataset=json.loads(Path(args.dataset).read_text())
    evidence=dict(schema='nikkei-predictive-pruning-v1',train=TRAIN,validationExcludedFromScreening=COHORT,
        internalFolds=[dict(train=TRAIN[:i],test=TRAIN[i]) for i in [1,2]],label='Future last-trade direction proxy, not executable PnL',
        criterion='Each fold improvement>0, mean>=0.005, moving-block bootstrap90% lower>0 at either5s/30s',
        bootstrapReplicates=399,blockSamples=6,controlIndicesProtected=list(range(30,40)),counts=counts,
        retainedMarketIndices=kept,removedMarketIndices=sorted(set(MARKET_INDICES)-set(kept)),masks=masks,
        featureNames=feature_names(56),diagnostics=diagnostics,usage='TrainOnlyScreeningDevelopment',deploymentEligible=False,
        provenance=dict(bridgeSha256=hashlib.sha256(Path(args.bridge).read_bytes()).hexdigest().upper(),
            cacheSha256={e:hashlib.sha256(Path(dataset[e]).read_bytes()).hexdigest().upper() for e in TRAIN}))
    (out/'selection.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    print(json.dumps(dict(retained=kept,removed=len(evidence['removedMarketIndices']),masks=masks,counts=counts)),flush=True)

if __name__=='__main__':main()
