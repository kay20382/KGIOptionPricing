import unittest
from completed_daily import completed_segment, aggregate_daily
from rl_env import NikkeiRlEnv

class CompletedDailyTests(unittest.TestCase):
    def test_open_gain_and_loss_both_excluded(self):
        for unrealized in [-50,200]:
            state=dict(completedTrades=1,completedGrossPnl=100,completedCosts=72,completedNetPnl=28,
                       position=1,costs=108,openPositionCosts=36,unrealizedPnl=unrealized,
                       excludedOpenNetPnl=unrealized-36,netPnl=28+unrealized-36,
                       winRate=100,averageWinningPoints=2.8,averageLosingPoints=None)
            fills=[dict(quantity=1,role=0,symbol='MC',price=100,commission=36,timestamp='2026-09-01T08:00:00+08:00'),
                   dict(quantity=1,role=4,symbol='MC',price=110,commission=36,timestamp='2026-09-01T08:00:05+08:00'),
                   dict(quantity=1,role=0,symbol='MC',price=110,commission=36,timestamp='2026-09-01T08:00:20+08:00')]
            row=completed_segment(state,fills)
            self.assertEqual(row['netPnlJPY'],28)
            self.assertEqual(row['excludedOpenPositions'],1)
            self.assertEqual(row['excludedEntryCostsJPY'],36)
            self.assertEqual(row['excludedOpenNetPnlJPY'],unrealized-36)

    def test_weighted_daily_and_zero_trades(self):
        zero=dict(trades=0,wins=0,losses=0,grossPnlJPY=0,netPnlJPY=0,costsJPY=0,winningNetJPY=0,losingNetJPY=0,
                  excludedOpenPositions=0,excludedEntryCostsJPY=0,excludedUnrealizedJPY=0,excludedOpenNetPnlJPY=0,rawNetPnlJPY=0)
        one={**zero,'trades':1,'wins':1,'grossPnlJPY':100,'costsJPY':72,'netPnlJPY':28,'winningNetJPY':28,'rawNetPnlJPY':28}
        self.assertIsNone(aggregate_daily([zero])['winRatePercent'])
        self.assertEqual(aggregate_daily([zero,one])['winRatePercent'],100)

    def test_training_denies_evaluation_opt_in_before_process(self):
        with self.assertRaisesRegex(ValueError,'Evaluation-only'):
            NikkeiRlEnv('missing','missing',['x'],config={'allowIncompleteEvaluation':True},training=True)

if __name__=='__main__':unittest.main()
