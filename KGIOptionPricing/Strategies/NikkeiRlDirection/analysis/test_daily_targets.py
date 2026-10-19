import unittest
from performance_metrics import daily_targets


class DailyTargetTests(unittest.TestCase):
    def test_thresholds_are_strict(self):
        result = daily_targets(dict(completedTrades=100, winRatePercent=33,
                                    grossPnlJPY=0, netPnlJPY=0), partial=False)
        self.assertFalse(result['initialTargetPassed'])
        self.assertFalse(result['advancedTargetPassed'])

    def test_partial_session_cannot_prove_daily_goal(self):
        result = daily_targets(dict(completedTrades=101, winRatePercent=34,
                                    grossPnlJPY=100, netPnlJPY=20), partial=True)
        self.assertFalse(result['initialTargetPassed'])
        self.assertFalse(result['fullDayEligible'])

    def test_positive_gross_can_pass_before_net(self):
        result = daily_targets(dict(completedTrades=101, winRatePercent=34,
                                    grossPnlJPY=100, netPnlJPY=-20), partial=False)
        self.assertTrue(result['initialTargetPassed'])
        self.assertFalse(result['advancedTargetPassed'])


if __name__ == '__main__':
    unittest.main()
