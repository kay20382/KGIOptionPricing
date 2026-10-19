"""Projected dual feedback from completed training episodes; no validation or actor updates."""
import math
from training_objective import completed_values,objective_config

class DualConstraintFeedback:
    KEYS=['trade_incentive','win_weight','gross_weight']
    def __init__(self,initial,rates=(10,20,2),caps=(80,200,20)):
        objective_config('constrained-gross',**initial)
        if len(rates)!=3 or len(caps)!=3 or any(not math.isfinite(v) or v<=0 for v in [*rates,*caps]):raise ValueError('Positive finite rates/caps required')
        if any(initial[k]>cap for k,cap in zip(self.KEYS,caps)):raise ValueError('Initial weights exceed caps')
        self.weights=dict(initial);self.rates=tuple(rates);self.caps=tuple(caps);self.history=[]
    def config(self):return dict(self.weights)
    def update(self,episode,state):
        if not state['terminated'] or state['truncated']:raise ValueError('Only completed training episode may update feedback')
        n,g,w=completed_values(state)
        if not math.isfinite(g) or not math.isfinite(state['completedNetPnl']):raise ValueError('Nonfinite shared ledger')
        violations=[(50-n)/50,(.4*n-w)/max(n,50),(50*n-g)/(50*max(n,50))]
        # Bounded updates prevent one price outlier from dominating the multiplier.
        clipped=[max(-1,min(1,v)) for v in violations];before=self.config()
        self.weights={k:max(0,min(cap,before[k]+rate*v)) for k,cap,rate,v in zip(self.KEYS,self.caps,self.rates,clipped)}
        record=dict(episode=episode,completedTrades=n,completedGrossPnlJPY=g,completedNetPnlJPY=state['completedNetPnl'],
            netWinRatePercent=100*w/n if n else None,grossExpectancyJPY=g/n if n else None,
            weightsUsed=before,violations=violations,weightsNextEpisode=self.config(),scope='TrainingEpisodeOnly; not validation qualification')
        self.history.append(record);return record
