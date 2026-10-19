"""Report-only censoring of incomplete one-contract cycles; authoritative C# ledger retained."""
import math
from fill_diagnostics import fill_outcomes, equal_number

def completed_segment(state,fills):
    closed=[];entry=None
    for fill in fills:
        if fill['quantity']!=1:raise ValueError('Only one-contract fills allowed')
        if fill['role'] in [0,1]:
            if entry is not None:raise ValueError('Concurrent entries')
            entry=fill
        else:
            if entry is None:raise ValueError('Unpaired exit')
            closed.extend([entry,fill]);entry=None
    rows=fill_outcomes(closed)
    expected_position=0 if entry is None else (1 if entry['role']==0 else -1)
    if expected_position!=state['position']:raise ValueError('Excluded entry does not match held position')
    wins=[r['netPnlJPY'] for r in rows if r['netPnlJPY']>0]
    losses=[r['netPnlJPY'] for r in rows if r['netPnlJPY']<0]
    expected=dict(completedTrades=len(rows),completedGrossPnl=sum(r['grossPnlJPY'] for r in rows),
        completedNetPnl=sum(r['netPnlJPY'] for r in rows),completedCosts=sum(r['costsJPY'] for r in rows),
        winRate=100*len(wins)/len(rows) if rows else None,
        averageWinningPoints=sum(wins)/len(wins)/10 if wins else None,
        averageLosingPoints=sum(losses)/len(losses)/10 if losses else None,
        openPositionCosts=entry['commission'] if entry else 0)
    for field,value in expected.items():
        if not equal_number(state[field],value):raise ValueError('Completed fills/shared ledger mismatch: '+field)
    if not equal_number(state['netPnl']-state['excludedOpenNetPnl'],state['completedNetPnl']):raise ValueError('Censoring identity failed')
    if not equal_number(state['unrealizedPnl']-state['openPositionCosts'],state['excludedOpenNetPnl']):raise ValueError('Open cycle equity mismatch')
    if not equal_number(state['completedCosts']+state['openPositionCosts'],state['costs']):raise ValueError('Cost split mismatch')
    return dict(trades=state['completedTrades'],wins=len(wins),losses=len(losses),
        grossPnlJPY=state['completedGrossPnl'],netPnlJPY=state['completedNetPnl'],costsJPY=state['completedCosts'],
        winningNetJPY=sum(wins),losingNetJPY=sum(losses),excludedOpenPositions=int(entry is not None),
        excludedEntryCostsJPY=state['openPositionCosts'],excludedUnrealizedJPY=state['unrealizedPnl'],
        excludedOpenNetPnlJPY=state['excludedOpenNetPnl'],rawNetPnlJPY=state['netPnl'],excludedEntry=entry)

def aggregate_daily(segments):
    keys=['trades','wins','losses','grossPnlJPY','netPnlJPY','costsJPY','winningNetJPY','losingNetJPY',
          'excludedOpenPositions','excludedEntryCostsJPY','excludedUnrealizedJPY','excludedOpenNetPnlJPY','rawNetPnlJPY']
    row={k:sum(s[k] for s in segments) for k in keys}
    row.update(winRatePercent=100*row['wins']/row['trades'] if row['trades'] else None,
               averageWinJPY=row['winningNetJPY']/row['wins'] if row['wins'] else None,
               averageLossJPY=row['losingNetJPY']/row['losses'] if row['losses'] else None)
    if not math.isclose(row['rawNetPnlJPY']-row['excludedOpenNetPnlJPY'],row['netPnlJPY'],abs_tol=1e-5):raise ValueError('Daily identity failed')
    return row
