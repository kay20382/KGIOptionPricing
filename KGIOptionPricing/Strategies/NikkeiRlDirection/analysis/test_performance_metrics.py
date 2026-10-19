import unittest
from performance_metrics import episode_metrics


class PerformanceTests(unittest.TestCase):
    def test_gross_and_net_winning_losing_amounts(self):
        row = episode_metrics(dict(status='CompleteEpisode', position=0, netPnl=28,
            costs=72, completedTrades=2, winRate=50, averageWinningPoints=12.8,
            averageLosingPoints=-10, maxDrawdown=100), multiplier=10)
        self.assertEqual(row['grossPnlJPY'], 100)
        self.assertEqual(row['netPnlJPY'], 28)
        self.assertEqual(row['averageWinningNetJPY'], 128)
        self.assertEqual(row['averageLosingNetJPY'], -100)

    def test_missing_average_is_not_zero(self):
        row = episode_metrics(dict(status='CompleteEpisode', position=0, netPnl=0,
            costs=0, completedTrades=0, winRate=None, averageWinningPoints=None,
            averageLosingPoints=None, maxDrawdown=0), multiplier=10)
        self.assertIsNone(row['winRatePercent'])
        self.assertIsNone(row['averageWinningNetJPY'])

    def test_open_position_rejected(self):
        with self.assertRaises(ValueError):
            episode_metrics(dict(status='IncompleteUnclosedPosition', position=1), multiplier=10)


if __name__ == '__main__':
    unittest.main()
