"""Prices are supervised; trading objectives are evaluated by the shared ledger."""
import torch
from torch import nn

class SequenceModel(nn.Module):
    def __init__(self,kind='lstm',use_time=True,use_aux=True):
        super().__init__();self.kind=kind;self.use_time=use_time;self.use_aux=use_aux
        width=39 if use_time else 20
        if kind=='linear':
            encoded=width+40
        elif kind in ('lstm','gru'):
            self.encoder=(nn.LSTM if kind=='lstm' else nn.GRU)(width,32,batch_first=True);encoded=32
        else:raise ValueError('Unknown architecture')
        self.direction=nn.Linear(encoded+16,3);self.magnitude=nn.Linear(encoded+16,1)
    def forward(self,x,aux):
        z=x if self.use_time else x[:,:,:20]
        if self.kind=='linear':z=torch.cat((z[:,-1,:],x[:,:,:20].mean(1),x[:,:,:20].std(1,unbiased=False)),dim=1)
        else:z=self.encoder(z)[0][:,-1,:] # Initial h/c zero on every independent window.
        z=torch.cat((z,aux if self.use_aux else aux*0),dim=1)
        return self.direction(z),self.magnitude(z).squeeze(-1)

ACCEPTANCE_CRITERIA=dict(version='net-pnl-v2-daily-10',objective='maximize net PnL among eligible candidates',winRateMinExclusive=40,netProfitFactorMinExclusive=2,dailyLossMaxExclusiveJPY=8000,dailyCompletedTradesMinInclusive=10,tradeCountReward=False)

def qualify(net,win_rate,pf,daily_losses,daily_trades):
    return bool(daily_losses and len(daily_losses)==len(daily_trades) and net>0 and win_rate is not None and win_rate>40 and pf is not None and pf>2 and all(x<8000 for x in daily_losses) and all(n>=10 for n in daily_trades))
