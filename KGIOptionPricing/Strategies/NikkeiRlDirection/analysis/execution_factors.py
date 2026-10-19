"""Train-only learned execution features; deployed head only consumes current market inputs."""
import math
import torch
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor
from direction_warmstart import parse_at
from incremental_daily_backtest import timestamp_key

DERIVED_NAMES=['long_fill_probability15s','short_fill_probability15s','long_capped_wait15s_div15','short_capped_wait15s_div15',
 'long_expected_adverse_logticks15s','short_expected_adverse_logticks15s','long_net_win_probability15s','short_net_win_probability15s']

def targets_from_example(row):
    start,start_tick=timestamp_key(row['at']);fill=[];wait=[];adverse=[];wins=[]
    for side in ['long','short']:
        result=row[side];entries=[f for f in result['fills'] if f['role'] in [0,1]]
        if len(entries)>1:raise ValueError('Counterfactual single entry required')
        if entries:
            end,end_tick=timestamp_key(entries[0]['timestamp'])
            seconds=(end-start).total_seconds()+(end_tick-start_tick)*1e-7
        else:seconds=15
        if seconds<0:raise ValueError('Entry before decision')
        observed=bool(entries and seconds<=15)
        fill.append(float(observed));wait.append(min(seconds,15)/15)
        adverse.append(math.log1p(max(0,-result['state']['completedGrossPnl'])/50) if observed else 0)
        wins.append(float(observed and result['state'].get('completedNetPnl',0)>0))
    return fill+wait+adverse+wins

class ExecutionFactorHead(torch.nn.Module):
    def __init__(self):
        super().__init__();self.network=torch.nn.Sequential(torch.nn.Linear(50,48),torch.nn.Tanh(),torch.nn.Linear(48,8))
    def forward(self,x):
        # No position, PnL, drawdown, pending flags or session-clock path dependency.
        market=torch.cat([x[:,:30],x[:,40:60]],dim=1)
        z=self.network(market)
        return torch.cat([torch.sigmoid(z[:,:4]),torch.nn.functional.softplus(z[:,4:6]),torch.sigmoid(z[:,6:8])],dim=1)

class ExecutionFactorExtractor(BaseFeaturesExtractor):
    def __init__(self,observation_space):
        super().__init__(observation_space,features_dim=68)
        self.head=ExecutionFactorHead()
        self.head.requires_grad_(False)
    def forward(self,x):return torch.cat([x,self.head(x)],dim=1)

def copy_augmented_linear(source,target):
    if source.out_features!=target.out_features or source.in_features>target.in_features:raise ValueError('Invalid policy augmentation')
    with torch.no_grad():
        target.weight.zero_();target.weight[:,:source.in_features].copy_(source.weight);target.bias.copy_(source.bias)

def transplant_policy(source,target):
    for name in ['policy_net','value_net']:
        a=getattr(source.mlp_extractor,name);b=getattr(target.mlp_extractor,name)
        copy_augmented_linear(a[0],b[0]);b[2].load_state_dict(a[2].state_dict())
    target.action_net.load_state_dict(source.action_net.state_dict());target.value_net.load_state_dict(source.value_net.state_dict())

def head_loss(prediction,target):
    fill=torch.nn.functional.binary_cross_entropy(prediction[:,:2],target[:,:2])
    wait=torch.nn.functional.mse_loss(prediction[:,2:4],target[:,2:4])
    observed=target[:,:2]>0
    adverse=torch.nn.functional.smooth_l1_loss(prediction[:,4:6][observed],target[:,4:6][observed]) if observed.any() else prediction.sum()*0
    win=torch.nn.functional.binary_cross_entropy(prediction[:,6:8],target[:,6:8])
    return fill+win+.5*(wait+adverse)
