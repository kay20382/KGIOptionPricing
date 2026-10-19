"""Pair authoritative single-contract fills, rather than simulating another ledger."""
from direction_warmstart import parse_at
import math
import itertools


def fill_outcomes(fills, multiplier=10):
    entry=None;rows=[]
    roles={2:'TakeProfit',3:'StopLoss',4:'Flatten'}
    for fill in fills:
        if fill['quantity']!=1: raise ValueError('Diagnostics require the one-contract experiment')
        if fill['role'] in [0,1]:
            if entry is not None: raise ValueError('Multiple concurrent entry fills')
            entry=fill
        else:
            if entry is None or fill['role'] not in roles or fill['symbol']!=entry['symbol']:
                raise ValueError('Unpaired exit fill')
            holding=(parse_at(fill['timestamp'])-parse_at(entry['timestamp'])).total_seconds()
            if holding<0: raise ValueError('Exit before entry')
            gross=(fill['price']-entry['price'])*(1 if entry['role']==0 else -1)*multiplier
            costs=entry['commission']+fill['commission']
            rows.append(dict(exitRole=roles[fill['role']],side='Long' if entry['role']==0 else 'Short',
                grossPnlJPY=gross,costsJPY=costs,netPnlJPY=gross-costs,holdingSeconds=holding))
            entry=None
    if entry is not None: raise ValueError('Unclosed entry fill')
    return rows


def equal_number(a,b):
    return a is b if a is None or b is None else math.isclose(a,b,abs_tol=1e-6,rel_tol=1e-10)


def reconcile_episode(scenario,end,fills):
    for field in ['status','position']:
        if scenario[field]!=end[field]: raise ValueError('Scenario/feedback mismatch: '+field)
    for field in ['netPnl','costs','completedTrades','maxDrawdown','totalReward','winRate',
                  'averageWinningPoints','averageLosingPoints','userDefinedProfitFactor']:
        if not equal_number(scenario[field],end[field]): raise ValueError('Scenario/feedback mismatch: '+field)
    rows=fill_outcomes(fills)
    wins=[r['netPnlJPY'] for r in rows if r['netPnlJPY']>0]
    losses=[r['netPnlJPY'] for r in rows if r['netPnlJPY']<0]
    actual=dict(completedTrades=len(rows),costs=sum(r['costsJPY'] for r in rows),
        netPnl=sum(r['netPnlJPY'] for r in rows),winRate=100*len(wins)/len(rows) if rows else None,
        averageWinningPoints=sum(wins)/len(wins)/10 if wins else None,
        averageLosingPoints=sum(losses)/len(losses)/10 if losses else None)
    for field,value in actual.items():
        if not equal_number(value,end[field]): raise ValueError('Fills/ledger mismatch: '+field)
    return rows


def validate_daily_keys(rows,variants,seeds,cohort):
    actual=sorted((r['variant'],r['seed'],r['episode']) for r in rows)
    expected=sorted(itertools.product(variants,seeds,cohort))
    if actual!=expected: raise ValueError('Daily keys missing or duplicated')
