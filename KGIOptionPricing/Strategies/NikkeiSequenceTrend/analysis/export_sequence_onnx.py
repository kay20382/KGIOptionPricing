"""ONNX export using the existing verified RL environment; C: outputs only."""
import sys,json,io
from pathlib import Path
import torch
from sequence_model import SequenceModel
from research_resource_guard import limited_write

def main(root):
    root=Path(root);torch.set_num_threads(2);torch.set_num_interop_threads(1)
    results={}
    for path in sorted((root/'models').glob('*.pt')):
        kind=path.stem.split('-seed')[0];model=SequenceModel(kind)
        model.load_state_dict(torch.load(path,map_location='cpu',weights_only=True));model.eval()
        try:
            buffer=io.BytesIO()
            torch.onnx.export(model,(torch.zeros(1,120,39),torch.zeros(1,16)),buffer,input_names=['sequence','background'],output_names=['direction_logits','delta_ticks'],opset_version=17,dynamo=False)
            limited_write(path.with_suffix('.onnx'),buffer.getvalue(),root)
            results[path.stem]=dict(status='exported',torchVersion=torch.__version__)
        except Exception as e:results[path.stem]=dict(status='unavailable',reason=type(e).__name__+': '+str(e))
    (root/'onnx-export-report.json').write_text(json.dumps(results,indent=2),encoding='utf-8')

if __name__=='__main__':main(sys.argv[1])
