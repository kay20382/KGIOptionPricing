"""Training-only feedback shaping from authoritative C# completed ledger fields."""
import math

def objective_config(mode,trade_incentive=1,win_weight=6,gross_weight=2):
    if mode not in ['ledger','constrained-gross']:raise ValueError('Unknown training objective')
    if any(not math.isfinite(x) or x<0 for x in [trade_incentive,win_weight,gross_weight]):raise ValueError('Objective weights must be finite and nonnegative')
    return dict(trade_incentive=trade_incentive,win_weight=win_weight,gross_weight=gross_weight) if mode=='constrained-gross' else None

def completed_values(state):
    count=state['completedTrades'];rate=state['winRate']
    if count<0 or count!=int(count) or count and (rate is None or not 0<=rate<=100):raise ValueError('Invalid shared ledger count/rate')
    wins=0 if not count else round(count*rate/100)
    if count and not math.isclose(wins,count*rate/100,abs_tol=1e-6):raise ValueError('Inexact shared winning count')
    return count,state['completedGrossPnl'],wins

def constrained_reward(before,after,trade_incentive=1,win_weight=6,gross_weight=2,potential_weight=.1,gamma=.99**(3/5),drawdown_weight=.1):
    if min(trade_incentive,win_weight,gross_weight,potential_weight,drawdown_weight)<0:raise ValueError('Negative objective weight')
    n0,g0,w0=completed_values(before);n1,g1,w1=completed_values(after)
    if n1<n0 or w1<w0:raise ValueError('Shared completed ledger moved backwards')
    # All PnL comes from C#; this changes training reward, never accounting or fills.
    terms=dict(completedTradesDelta=n1-n0,completedGrossDeltaJPY=g1-g0,
        grossReward=(g1-g0)/50,
        frequencyIncentive=trade_incentive*(min(n1,50)-min(n0,50)),
        winConstraint=win_weight*(min(0,w1-.4*n1)-min(0,w0-.4*n0)),
        grossConstraint=gross_weight*(min(0,(g1-50*n1)/50)-min(0,(g0-50*n0)/50)),
        potential=potential_weight*((0 if after['terminated'] or after['truncated'] else gamma*after['unrealizedPnl'])-before['unrealizedPnl'])/50,
        drawdown=-drawdown_weight*after['drawdownPenalty'])
    reward=sum(terms[k] for k in ['grossReward','frequencyIncentive','winConstraint','grossConstraint','potential','drawdown'])
    if not math.isfinite(reward):raise ValueError('Non-finite objective reward')
    return reward,terms
