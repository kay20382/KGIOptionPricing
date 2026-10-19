"""Strict train-only loading of shared-engine counterfactual targets."""
import hashlib,json
from pathlib import Path
import numpy as np
from direction_warmstart import parse_at

def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()
def validate_example(row,observation_size=40):
    x=np.asarray(row['observation'],dtype=np.float32)
    if observation_size not in [40,60] or x.shape!=(observation_size,) or not np.isfinite(x).all() or x[30]!=0 or x[38]!=1 or x[39]*3600<=310:raise ValueError('Invalid flat decision observation')
    values=[]
    for side in ['long','short']:
        out=row[side];state=out['state']
        if out['status'] not in ['Unfilled','Completed'] or state['position']!=0:raise ValueError('Unclosed target forbidden')
        if out['status']=='Completed':
            if not out['completed'] or state['completedTrades']!=1:raise ValueError('Invalid completed target')
        elif state['completedTrades']!=0 or state['completedNetPnl']!=0:raise ValueError('Unfilled target has PnL')
        values.append(state['completedNetPnl'])
    expected=0 if max(values)<=0 else 1 if values[0]>=values[1] else 2
    if row['action']!=expected or row['proxyAction'] not in [None,0,1,2]:raise ValueError('Target does not reflect executable net')
    return x
def target_for(row,target,matched_only=True):
    if target not in ['proxy','executable']:raise ValueError('Unknown warmstart target')
    if not matched_only and target!='executable':raise ValueError('All-outcome mode requires executable targets')
    if matched_only and row['proxyAction'] is None:return None
    return row['proxyAction'] if target=='proxy' else row['action']

def validate_training_config(label_config,training_config):
    if training_config is None or not {'decisionLatencyMs','cooldownSeconds','rewardCostWeight','observationSize','decisionIntervalSeconds'}.issubset(training_config):raise ValueError('Actual training environment required')
    for key,value in training_config.items():
        if key not in label_config or label_config[key]!=value:raise ValueError(f'Warmstart training environment mismatch: {key}')

def validate_label_config(config,horizon_seconds):
    config=dict(config,decisionIntervalSeconds=config.get('decisionIntervalSeconds',5),featureProfile=config.get('featureProfile','three-product'),directionDrivenHolding=config.get('directionDrivenHolding',False))
    research=config['featureProfile']=='mini-micro-research'
    power=config['featureProfile'] in ['mini-micro-power','mini-micro-research']
    if config['featureProfile'] not in ['three-product','mini-micro-power','mini-micro-research'] or config['directionDrivenHolding']!=power:raise ValueError('Warmstart feature/holding profile mismatch')
    if horizon_seconds not in [15,30] or config['decisionIntervalSeconds'] not in [3,5] or horizon_seconds%config['decisionIntervalSeconds']:raise ValueError('Warmstart horizon/cadence mismatch')
    required=dict(decisionLatencyMs=1,cooldownSeconds=10,commission=36,stopTicks=6,maxHoldingSeconds=0 if power else 60,observationSize=60 if research else 40,tickSize=5,multiplier=10,rewardCostWeight=1)
    if research:required.update(stopMode='fixed',fixedStopPoints=150,maxStopPoints=150)
    if any(config[k]!=v for k,v in required.items()) or config['allowIncompleteEvaluation']:raise ValueError('Warmstart environment mismatch')
    return config

def load_samples(path,dataset_path,train,target,output=None,matched_only=True,training_config=None,horizon_seconds=30):
    path=Path(path);manifest_path=Path(str(path)+'.manifest.json');meta=json.loads(manifest_path.read_text(encoding='utf-8'))
    if meta['schema']!='nikkei-executable-labels-v1' or meta['usage']!='TrainingOnlyCounterfactual' or set(meta['trainingEpisodes'])!=set(train) or meta['labelSha256']!=sha(path):raise ValueError('Warmstart train-only/hash contract failed')
    if meta['horizonSeconds']!=horizon_seconds:raise ValueError('Warmstart prediction horizon mismatch')
    config=validate_label_config(meta['config'],horizon_seconds)
    validate_training_config(config,training_config)
    dataset=json.loads(Path(dataset_path).read_text(encoding='utf-8-sig'));sources={s['episode']:s for s in meta['sources']}
    if set(sources)!=set(train):raise ValueError('Warmstart source episodes mismatch')
    for episode in train:
        if Path(dataset[episode]).resolve()!=Path(sources[episode]['path']).resolve() or sha(dataset[episode])!=sources[episode]['cacheSha256']:raise ValueError('Warmstart cache changed')
    xs=[];ys=[];times=[];counts={e:[0,0,0] for e in train};previous={}
    with path.open(encoding='utf-8') as file:
        for raw in file:
            row=json.loads(raw);episode=row['episode'];r=row['example']
            if episode not in counts:raise ValueError('Validation episode in training labels')
            at=parse_at(r['at'])
            if episode[:8]!=at.strftime('%Y%m%d') or episode in previous and at<=previous[episode]:raise ValueError('Label timestamp mismatch')
            previous[episode]=at;x=validate_example(r,config['observationSize']);label=target_for(r,target,matched_only)
            if label is None:continue
            xs.append(x);ys.append(label);times.append([episode,r['at']]);counts[episode][label]+=1
    x=np.asarray(xs,dtype=np.float32).reshape(-1,config['observationSize']);y=np.asarray(ys,dtype=np.int64)
    if len(y)<100 or np.bincount(y,minlength=3).min()==0:raise ValueError('Insufficient executable target classes')
    info=dict(target=target,horizonSeconds=horizon_seconds,decisionIntervalSeconds=config['decisionIntervalSeconds'],matchedOnly=matched_only,selectedSamples=len(y),matchedTimestampSamples=len(y) if matched_only else meta['matchedProxySamples'],episodeClassCounts=counts,
        labelSha256=sha(path),labelManifestSha256=sha(manifest_path),datasetSha256=sha(dataset_path),
        matchedTimestampsSha256=hashlib.sha256(json.dumps(times,separators=(',',':')).encode()).hexdigest().upper(),sources=meta['sources'],scope='Training-only; matched proxy/executable arms use identical initial observations and timestamps; all-outcome arm changes sample availability')
    if output:
        out=Path(output);np.savez_compressed(out/'direction-training.npz',observation=x,action=y)
        info['npzSha256']=sha(out/'direction-training.npz');(out/'direction-training.json').write_text(json.dumps(info,indent=2),encoding='utf-8')
    return (x,y),info
