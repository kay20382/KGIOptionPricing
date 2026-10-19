"""Saved tensor parity only. Raw feature parity is a separate deployment gate."""
import io,json,subprocess
from pathlib import Path
import numpy as np
import torch
from research_resource_guard import limited_write,safe_cleanup
from execution_alpha import tree_predict

NATIVE=Path(r'C:\Users\user\.codex\visualizations\2026\10\07\01a1161a-64d8-7c81-9e7d-b725164d7ff5\rl-performance-only-20261014\app\onnxruntime.dll')

def verify(root,tag,kind,model):
    temp=root/'parity-temp';temp.mkdir(exist_ok=True)
    fixture=temp/(tag+'-input.json');actual=temp/(tag+'-actual.json')
    rng=np.random.default_rng(123)
    if kind.startswith('tree'):
        path=root/'models'/(tag+'.tree.json');payload=json.loads(path.read_text(encoding='utf-8'))
        x=rng.normal(size=(4,271))
        expected={'net_ticks':np.column_stack([tree_predict(h,x) for h in payload['netHeads']]),'win_probabilities':np.column_stack([tree_predict(h,x) for h in payload['winHeads']])}
        rows=[dict(pooled=r.tolist()) for r in x]
    else:
        x=torch.tensor(rng.normal(size=(4,120,45)),dtype=torch.float32);a=torch.tensor(rng.normal(size=(4,16)),dtype=torch.float32)
        model.eval()
        with torch.inference_mode():p,n=model(x,a)
        expected={'win_logits':p.numpy(),'net_ticks':n.numpy()}
        weights=temp/(tag+'-export.pt');buffer=io.BytesIO();torch.save(model.state_dict(),buffer);limited_write(weights,buffer.getvalue(),root)
        path=root/'models'/(tag+'.onnx')
        try:
            subprocess.run([r'E:\PROG\C#\KGIOptionPricing\.venv-rl\Scripts\python.exe',str(root/'source/export_alpha_onnx.py'),str(root),str(weights),kind,str(path)],capture_output=True,text=True,check=True,timeout=120)
        finally:safe_cleanup(root,[weights])
        rows=[dict(sequence=x[j].flatten().tolist(),background=a[j].tolist()) for j in range(4)]
    limited_write(fixture,json.dumps(rows).encode(),root)
    subprocess.run(['dotnet',str(root/'parity-runtime/NikkeiSequenceParity.dll'),str(path),str(fixture),str(NATIVE),str(actual)],capture_output=True,text=True,check=True,timeout=120)
    observed=json.loads(actual.read_text(encoding='utf-8'));errors={}
    for name,target in expected.items():
        value=np.asarray([r[name] if kind.startswith('tree') else r['values'][name] for r in observed],dtype=target.dtype)
        errors[name]=float(np.max(np.abs(target-value)))
        if not np.isfinite(value).all() or errors[name]>(1e-10 if kind.startswith('tree') else 1e-5):raise ValueError('Python/C# tensor parity failed '+str(errors))
    result=dict(scope='Four deterministic normalized tensors; raw market feature parity not verified',maximumAbsoluteErrors=errors,passed=True,rawFeatureParityVerified=False)
    (root/'models'/(tag+'.parity.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    safe_cleanup(root,[fixture,actual])
    return result