"""Frozen causal shallow-tree interaction probabilities, embedded in the ONNX actor."""
import numpy as np
import torch
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor

DERIVED_NAMES=['executable_wait_probability15s','executable_long_probability15s','executable_short_probability15s']

def tree_arrays(classifier,indices,temperatures):
    if list(classifier.classes_)!=[0,1,2]:raise ValueError('Three executable classes required')
    paths=[];values=[]
    for trees in classifier._predictors:
        for k,tree in enumerate(trees):
            def visit(node,path):
                n=tree.nodes[node]
                if n['is_leaf']:
                    paths.append(path);v=np.zeros(3);v[k]=n['value'];values.append(v);return
                index=indices[int(n['feature_idx'])]
                if index in range(30,40):raise ValueError('Account path cannot be a predictive factor')
                split=(index,float(n['num_threshold']))
                visit(int(n['left']),path+[(split,-1)])
                visit(int(n['right']),path+[(split,1)])
            visit(0,[])
    depth=max(1,max(map(len,paths)));shape=(len(paths),depth)
    ix=np.zeros(shape,dtype=np.int64);threshold=np.zeros(shape);sign=np.zeros(shape);valid=np.zeros(shape);temp=np.ones(shape)
    for i,path in enumerate(paths):
        for j,((index,t),s) in enumerate(path):
            ix[i,j]=index;threshold[i,j]=t;sign[i,j]=s;valid[i,j]=1;temp[i,j]=temperatures[index]
    if not np.isfinite(temp).all() or (temp<=0).any():raise ValueError('Positive finite smoothing temperatures required')
    return dict(indices=ix,thresholds=threshold,signs=sign,valid=valid,temperatures=temp,values=np.array(values),baseline=classifier._baseline_prediction.reshape(3))

def head_spec(arrays):return dict(leaves=int(arrays['indices'].shape[0]),depth=int(arrays['indices'].shape[1]))

class SoftInteractionHead(torch.nn.Module):
    def __init__(self,spec):
        super().__init__();shape=(spec['leaves'],spec['depth'])
        self.register_buffer('indices',torch.zeros(shape,dtype=torch.long))
        for name in ['thresholds','signs','valid']:self.register_buffer(name,torch.zeros(shape))
        self.register_buffer('temperatures',torch.ones(shape))
        self.register_buffer('values',torch.zeros(spec['leaves'],3));self.register_buffer('baseline',torch.zeros(3))
        self.register_buffer('mean',torch.zeros(60));self.register_buffer('std',torch.ones(60))
    def load_arrays(self,arrays,mean,var):
        with torch.no_grad():
            for k,value in arrays.items():getattr(self,k).copy_(torch.as_tensor(value,dtype=getattr(self,k).dtype))
            self.mean.copy_(torch.as_tensor(mean,dtype=torch.float32))
            self.std.copy_(torch.sqrt(torch.as_tensor(var,dtype=torch.float32)+1e-8))
    def forward(self,x):
        raw=x*self.std+self.mean
        p=torch.sigmoid(self.signs*(raw[:,self.indices]-self.thresholds)/self.temperatures)
        path=(p*self.valid+(1-self.valid)).prod(dim=2)
        return torch.softmax(path@self.values+self.baseline,dim=1)

class InteractionFactorExtractor(BaseFeaturesExtractor):
    def __init__(self,observation_space,head_spec):
        super().__init__(observation_space,features_dim=63)
        self.head=SoftInteractionHead(head_spec);self.head.requires_grad_(False)
    def forward(self,x):return torch.cat([x,self.head(x)],dim=1)
