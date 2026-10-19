"""Explicit5-second warmstart adapter; PPO/execution/cost settings are unchanged."""
import hashlib
import json
import sys
from pathlib import Path
import numpy as np
import train_rl_direction as trainer
from predictive_pruning import build_samples

def collect_five_second_direction(args,output):
    if args.observation_size!=56:raise ValueError('5s candidate requires56 schema')
    env=trainer.NikkeiRlEnv(args.bridge,args.dataset,args.train,args.environment_config,training=True)
    xs=[];ys=[];details=[]
    try:
        for episode in args.train:
            env.reset(options={'episode':episode});states=[env.last]
            while True:
                _,_,terminated,truncated,info=env.step(0);states.append(info)
                if terminated or truncated:break
            x,y,_=build_samples(states,5,0);xs.append(x);ys.append(y)
            details.append(dict(episode=episode,samples=len(y),classCounts=np.bincount(y,minlength=3).tolist()))
    finally:env.close()
    x,y=np.concatenate(xs),np.concatenate(ys)
    if len(y)<100 or np.bincount(y,minlength=3).min()==0:raise ValueError('Missing5s direction classes')
    np.savez_compressed(output/'direction-training.npz',observation=x,action=y)
    dataset=json.loads(Path(args.dataset).read_text())
    evidence=dict(horizonSeconds=5,exclusiveThresholdTicks=0,label='Future last-trade direction proxy, not executable PnL',
        episodes=details,cacheSha256={e:hashlib.sha256(Path(dataset[e]).read_bytes()).hexdigest().upper() for e in args.train},
        npzSha256=hashlib.sha256((output/'direction-training.npz').read_bytes()).hexdigest().upper())
    (output/'direction-training.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    return x,y

if __name__=='__main__':
    output=Path(sys.argv[sys.argv.index('--output')+1])
    trainer.collect_direction_data=collect_five_second_direction
    trainer.main()
    label=dict(horizonSeconds=5,exclusiveThresholdTicks=0)
    report=json.loads((output/'backtest-report.json').read_text());report['directionLabel']=label
    (output/'backtest-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    for path in output.glob('policy-seed*.manifest.json'):
        manifest=json.loads(path.read_text());manifest['directionLabel']=label
        path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
