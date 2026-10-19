"""Read completed shared-ledger metrics; averages are net points per round trip."""
import math

def episode_metrics(episode, multiplier):
    if episode['status'] != 'CompleteEpisode' or episode['position'] != 0:
        raise ValueError('完整績效要求 episode 已結束且空手')
    def amount(points):
        return None if points is None else points * multiplier
    return dict(grossPnlJPY=episode['netPnl'] + episode['costs'],
                netPnlJPY=episode['netPnl'], costsJPY=episode['costs'],
                completedTrades=episode['completedTrades'], winRatePercent=episode['winRate'],
                averageWinningNetJPY=amount(episode['averageWinningPoints']),
                averageLosingNetJPY=amount(episode['averageLosingPoints']),
                averageWinningNetPoints=episode['averageWinningPoints'],
                averageLosingNetPoints=episode['averageLosingPoints'], maxDrawdownJPY=episode['maxDrawdown'])


def aggregate_round_trips(rows):
    trades = sum(row['completedTrades'] for row in rows)
    wins = sum(row['winningTrades'] for row in rows)
    losses = sum(row['losingTrades'] for row in rows)
    return dict(completedTrades=trades, winningTrades=wins, losingTrades=losses,
        winRatePercent=100 * wins / trades if trades else None,
        grossPnlJPY=sum(row['grossPnlJPY'] for row in rows),
        netPnlJPY=sum(row['netPnlJPY'] for row in rows), costsJPY=sum(row['costsJPY'] for row in rows),
        averageWinningNetJPY=sum((row['averageWinningNetJPY'] or 0) * row['winningTrades'] for row in rows) / wins if wins else None,
        averageLosingNetJPY=sum((row['averageLosingNetJPY'] or 0) * row['losingTrades'] for row in rows) / losses if losses else None)


def completed_trade_counts(row):
    total = row['completedTrades']
    wins = round((row['winRatePercent'] or 0) * total / 100)
    winning_pnl = wins * (row['averageWinningNetJPY'] or 0)
    mean_loss = row['averageLosingNetJPY']
    losses = round((row['netPnlJPY'] - winning_pnl) / mean_loss) if mean_loss else 0
    if wins < 0 or losses < 0 or wins + losses > total:
        raise ValueError('Invalid shared-ledger trade statistics')
    if not math.isclose(winning_pnl + losses * (mean_loss or 0), row['netPnlJPY'], abs_tol=1e-6):
        raise ValueError('Mean trade statistics do not reconstruct shared net PnL')
    return dict(winningTrades=wins, losingTrades=losses, breakevenTrades=total-wins-losses)


def daily_targets(row, partial=False):
    frequency = row['completedTrades'] > 100
    win_rate = row['winRatePercent'] is not None and row['winRatePercent'] > 33
    gross = row['grossPnlJPY'] > 0
    net = row['netPnlJPY'] > 0
    return dict(fullDayEligible=not partial, frequencyPassed=frequency, winRatePassed=win_rate,
                grossPassed=gross, netPassed=net,
                initialTargetPassed=not partial and frequency and win_rate and gross,
                advancedTargetPassed=not partial and frequency and win_rate and gross and net)
