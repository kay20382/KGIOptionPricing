"""Keep 30s market prediction separate from fill-conditional 180s net value."""
import numpy as np
import torch
from torch import nn
from execution_alpha import MARKET

class DualForecast(nn.Module):
    def __init__(self,kind='gru'):
        super().__init__();self.kind=kind
        if kind in ('gru','lstm'):
            self.encoder=(nn.GRU if kind=='gru' else nn.LSTM)(45,32,batch_first=True);width=32
        elif kind=='linear':width=45+52
        else:raise ValueError('Unknown architecture')
        self.price=nn.Linear(width+16,1);self.net=nn.Linear(width+16,2);self.win=nn.Linear(width+16,2)
    def forward(self,x,a):
        if self.kind=='linear':z=torch.cat((x[:,-1,:],x[:,:,MARKET].mean(1),x[:,:,MARKET].std(1,unbiased=False)),1)
        else:z=self.encoder(x)[0][:,-1,:]
        z=torch.cat((z,a),1)
        return self.price(z).squeeze(-1)*4,self.win(z),self.net(z)*8

def dual_loss(forecast,logits,net,price30,target,closed):
    if not torch.isfinite(price30).all() or not torch.isfinite(target[closed]).all():raise ValueError('Nonfinite target')
    price=.2*torch.mean(((forecast-price30.clamp(-64,64))/4)**2)
    if not closed.any():return price+(logits.sum()+net.sum())*0
    classification=torch.nn.functional.binary_cross_entropy_with_logits(logits,(target>0).to(logits.dtype),reduction='none')
    # Squared loss estimates the conditional mean; Huber can suppress positive
    # expectation when a majority of small losses is offset by larger wins.
    value=((net-target.clamp(-128,128))/8)**2
    return price+(value+.5*classification)[closed].mean()

def dual_signals(forecast30,net,p,trend60,trend300):
    f=np.asarray(forecast30);net=np.asarray(net);p=np.asarray(p)
    if net.shape!=(len(f),2) or p.shape!=net.shape or not np.isfinite(f).all() or not np.isfinite(net).all() or not np.isfinite(p).all() or np.any((p<0)|(p>1)):raise ValueError('Invalid dual predictions')
    output=np.column_stack((p[:,1],np.maximum(0,1-np.max(p,axis=1)),p[:,0]));delta=np.zeros(len(f))
    for j in range(len(f)):
        side=0 if trend60[j]>0 and trend300[j]>0 and f[j]>0 else 1 if trend60[j]<0 and trend300[j]<0 and f[j]<0 else None
        if side is not None and net[j,side]>0:delta[j]=net[j,side]*(1 if side==0 else -1)
    return output,delta
def value_future_mask(samples,indices):
    # Label-only horizon: <=10s entry wait +180s holding +5s exit observation.
    # This function must never be used to choose live inference windows.
    bad=np.concatenate(([0],np.cumsum(~samples.valid)));result=np.zeros(len(indices),dtype=bool)
    for j,index in enumerate(indices):
        i=int(index);end=i+39
        if end<len(samples.times):result[j]=bad[end+1]-bad[i+1]==0 and abs(samples.times[end]-samples.times[i]-195)<1e-6
    return result