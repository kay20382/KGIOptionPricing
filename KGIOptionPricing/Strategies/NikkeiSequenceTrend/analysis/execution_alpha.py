"""Cost-aware conditional outcomes, causal pooled baseline and portable tree heads."""
import numpy as np
import torch
from torch import nn

MARKET=list(range(20))+list(range(39,45))
DYNAMIC=[0,6,8,10,16,18,39,42]
def pooled_features(samples,indices):
    result=np.empty((len(indices),271),np.float64)
    for j,i in enumerate(indices):
        x,a=samples.window(int(i));x=x.astype(np.float64)
        parts=[x[-1],a.astype(np.float64)]
        for length in (6,12,24,60,120):
            z=x[-length:];parts += [z[:,MARKET].mean(0),z[:,DYNAMIC].std(0),z[-1,DYNAMIC]-z[0,DYNAMIC]]
        result[j]=np.concatenate(parts)
    if not np.isfinite(result).all():raise ValueError('Nonfinite feature')
    return result

def project_signals(net,p,trend60,trend300):
    net=np.asarray(net);p=np.asarray(p)
    if not np.isfinite(net).all() or not np.isfinite(p).all():raise ValueError('Nonfinite predictions')
    output=np.zeros((len(net),3));output[:,1]=1;delta=np.zeros(len(net))
    for j in range(len(net)):
        side=0 if trend60[j]>0 and trend300[j]>0 else 1 if trend60[j]<0 and trend300[j]<0 else None
        if side is not None and net[j,side]>0:
            output[j]=[p[j,1],max(0,1-max(p[j])),p[j,0]]
            delta[j]=net[j,side]*(1 if side==0 else -1)
    return output,delta

def export_head(model,kind='regression'):
    if isinstance(model,(int,float)):
        return dict(kind='constant',value=float(model))
    trees=[]
    for stage in model._predictors:
        for predictor in stage:
            nodes=[]
            for n in predictor.nodes:
                if n['is_categorical']:raise ValueError('Categorical tree export unsupported')
                nodes.append(dict(value=float(n['value']),leaf=bool(n['is_leaf']),feature=int(n['feature_idx']),threshold=float(n['num_threshold']),left=int(n['left']),right=int(n['right']),missingLeft=bool(n['missing_go_to_left'])))
            trees.append(nodes)
    return dict(kind=kind,bias=float(model._baseline_prediction[0,0]),trees=trees)

def tree_predict(head,x):
    if head['kind']=='constant':return np.full(len(x),head['value'])
    y=np.full(len(x),head['bias'])
    for tree in head['trees']:
        for j,row in enumerate(x):
            i=0
            while not tree[i]['leaf']:
                n=tree[i];value=row[n['feature']]
                i=n['left'] if (n['missingLeft'] if np.isnan(value) else value<=n['threshold']) else n['right']
            y[j]+=tree[i]['value']
    return 1/(1+np.exp(-np.clip(y,-40,40))) if head['kind']=='classification' else y

class AlphaForecast(nn.Module):
    def __init__(self,kind='gru'):
        super().__init__();self.kind=kind
        if kind in ('gru','lstm'):
            self.encoder=(nn.GRU if kind=='gru' else nn.LSTM)(45,32,batch_first=True);width=32
        elif kind=='linear':width=45+52
        else:raise ValueError('Unknown architecture')
        self.net=nn.Linear(width+16,2);self.win=nn.Linear(width+16,2)
    def forward(self,x,a):
        if self.kind=='linear':
            z=torch.cat((x[:,-1,:],x[:,:,MARKET].mean(1),x[:,:,MARKET].std(1,unbiased=False)),1)
        else:z=self.encoder(x)[0][:,-1,:]
        z=torch.cat((z,a),1)
        return self.win(z),self.net(z)
def masked_loss(logits,net,target,closed):
    if not closed.any():return (logits.sum()+net.sum())*0
    if not torch.isfinite(target[closed]).all():raise ValueError('Nonfinite closed target')
    labels=(target>0).to(logits.dtype)
    classification=torch.nn.functional.binary_cross_entropy_with_logits(logits,labels,reduction='none')
    regression=torch.nn.functional.smooth_l1_loss(net,target.clamp(-64,64),reduction='none')
    return (classification+regression)[closed].mean()