import json
from pathlib import Path
import numpy as np
import torch
from sequence_model import SequenceModel
from research_resource_guard import limited_read

def diagnose(root,days):
    old=root.parent/'20261014-v1'
    with np.load(limited_read(old/'normalizer.npz')) as f:norm={k:f[k] for k in f.files}
    results=[]
    for path in sorted((old/'models').glob('*.pt')):
        model=SequenceModel(path.stem.split('-')[0]);model.load_state_dict(torch.load(path,weights_only=True));model.eval()
        for day in days:
            s=day['samples'];indices=s.inference_indices;net=[];confidence=[];before=[];after=[]
            for start in range(0,len(indices),32):
                batch=indices[start:start+32];rows=[s.window(int(i)) for i in batch]
                x=np.stack([r[0][:,:39] for r in rows]);a=np.stack([r[1] for r in rows])
                with torch.inference_mode():
                    p,d=model(torch.tensor(np.clip((x-norm['mean'])/norm['scale'],-10,10),dtype=torch.float32),torch.tensor(np.clip((a-norm['auxMean'])/norm['auxScale'],-10,10),dtype=torch.float32));p=p.softmax(1).numpy();d=d.numpy()
                required=(day['quotes'][batch,2]-day['quotes'][batch,1])/5+72/50+1
                c=np.where(d>0,p[:,2],p[:,0]);net.extend(np.abs(d).tolist());confidence.extend(c.tolist())
                before.extend((c>=.55).tolist());after.extend(((c>=.55)&(np.abs(d)>required)).tolist())
            results.append(dict(model=path.stem,session=day['id'],inferenceWindows=len(indices),maximumAbsoluteTicks=max(net,default=0),p95AbsoluteTicks=float(np.quantile(net,.95)) if net else 0,confidencePass=sum(before),confidenceAndCostPass=sum(after),scope='Full validation session; old frozen weights/normalizer, current reliable history-only eligibility'))
    (root/'legacy-full-day-diagnosis.json').write_text(json.dumps(results,indent=2))