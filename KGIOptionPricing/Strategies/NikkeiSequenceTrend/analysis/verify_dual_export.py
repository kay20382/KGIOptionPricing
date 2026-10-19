import io,json,subprocess
import numpy as np
import torch
from execution_alpha import tree_predict
from research_resource_guard import limited_write,safe_cleanup
from verify_alpha_export import NATIVE

def verify(root,tag,kind,model):
    temp=root/'parity-temp';temp.mkdir(exist_ok=True);inputs=temp/(tag+'-input.json');actual=temp/(tag+'-actual.json');rng=np.random.default_rng(123)
    if kind.startswith('tree'):
        path=root/'models'/(tag+'.tree.json');payload=json.loads(path.read_text(encoding='utf-8'));x=rng.normal(size=(4,271))
        expected={'forecast30_ticks':tree_predict(payload['forecast30Head'],x),'net_ticks':np.column_stack([tree_predict(h,x) for h in payload['netHeads']]),'win_probabilities':np.column_stack([tree_predict(h,x) for h in payload['winHeads']])};rows=[dict(pooled=r.tolist()) for r in x]
    else:
        x=torch.tensor(rng.normal(size=(4,120,45)),dtype=torch.float32);a=torch.tensor(rng.normal(size=(4,16)),dtype=torch.float32);model.eval()
        with torch.inference_mode():f,p,n=model(x,a)
        expected={'forecast30_ticks':f.numpy(),'win_logits':p.numpy(),'net_ticks':n.numpy()};weights=temp/(tag+'-export.pt');buffer=io.BytesIO();torch.save(model.state_dict(),buffer);limited_write(weights,buffer.getvalue(),root);path=root/'models'/(tag+'.onnx')
        try:subprocess.run([r'E:\PROG\C#\KGIOptionPricing\.venv-rl\Scripts\python.exe',str(root/'source/export_dual_onnx.py'),str(root),str(weights),kind,str(path)],capture_output=True,text=True,check=True,timeout=120)
        finally:safe_cleanup(root,[weights])
        rows=[dict(sequence=x[j].flatten().tolist(),background=a[j].tolist()) for j in range(4)]
    limited_write(inputs,json.dumps(rows).encode(),root)
    subprocess.run(['dotnet',str(root/'parity-runtime/NikkeiSequenceParity.dll'),str(path),str(inputs),str(NATIVE),str(actual)],capture_output=True,text=True,check=True,timeout=120)
    observed=json.loads(actual.read_text(encoding='utf-8'));errors={}
    for name,target in expected.items():
        values=[r[name] if kind.startswith('tree') else r['values'][name] for r in observed]
        if name=='forecast30_ticks':values=[v[0] for v in values]
        value=np.asarray(values,dtype=target.dtype);errors[name]=float(np.max(np.abs(value-target)))
        if not np.isfinite(value).all() or errors[name]>(1e-10 if kind.startswith('tree') else 1e-5):raise ValueError('Dual parity mismatch '+str(errors))
    result=dict(passed=True,scope='Four deterministic normalized windows, actual fitted weights',maximumAbsoluteErrors=errors)
    (root/'models'/(tag+'.parity.json')).write_text(json.dumps(result,indent=2));safe_cleanup(root,[inputs,actual]);return result