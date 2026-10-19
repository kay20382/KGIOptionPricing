import io,sys
from pathlib import Path
import torch
from execution_alpha import AlphaForecast
from research_resource_guard import limited_write
root,weights,kind,output=sys.argv[1:]
torch.set_num_threads(2);torch.set_num_interop_threads(1)
model=AlphaForecast(kind);model.load_state_dict(torch.load(weights,weights_only=True,map_location='cpu'));model.eval()
buffer=io.BytesIO()
torch.onnx.export(model,(torch.zeros(1,120,45),torch.zeros(1,16)),buffer,input_names=['sequence','background'],output_names=['win_logits','net_ticks'],opset_version=17,dynamo=False)
limited_write(Path(output),buffer.getvalue(),Path(root))
print('PASS ONNX export',kind,torch.__version__)