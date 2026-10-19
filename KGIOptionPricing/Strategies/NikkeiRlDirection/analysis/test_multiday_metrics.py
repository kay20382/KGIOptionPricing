import unittest
from performance_metrics import aggregate_round_trips, completed_trade_counts


class MultiDayTests(unittest.TestCase):
    def test_breakeven_round_trip_is_not_counted_as_loss(self):
        counts = completed_trade_counts(dict(completedTrades=3, winRatePercent=100/3,
            netPnlJPY=5, averageWinningNetJPY=10, averageLosingNetJPY=-5))
        self.assertEqual(counts, dict(winningTrades=1, losingTrades=1, breakevenTrades=1))

    def test_weighted_statistics_use_trade_counts(self):
        rows = [dict(completedTrades=2, winningTrades=1, losingTrades=1,
                     grossPnlJPY=122, netPnlJPY=50, costsJPY=72,
                     averageWinningNetJPY=100, averageLosingNetJPY=-50),
                dict(completedTrades=8, winningTrades=2, losingTrades=6,
                     grossPnlJPY=376, netPnlJPY=-200, costsJPY=576,
                     averageWinningNetJPY=200, averageLosingNetJPY=-100)]
        result = aggregate_round_trips(rows)
        self.assertEqual(result['completedTrades'], 10)
        self.assertEqual(result['winRatePercent'], 30)
        self.assertAlmostEqual(result['averageWinningNetJPY'], 500 / 3)
        self.assertAlmostEqual(result['averageLosingNetJPY'], -650 / 7)
        self.assertEqual(result['netPnlJPY'], -150)


if __name__ == '__main__':
    unittest.main()
