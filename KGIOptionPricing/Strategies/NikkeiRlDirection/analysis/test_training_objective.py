import unittest
from training_objective import constrained_reward

class ObjectiveTests(unittest.TestCase):
    def state(self,trades=0,gross=0,wins=0,unrealized=0):
        return dict(completedTrades=trades,completedGrossPnl=gross,winRate=100*wins/trades if trades else None,
                    unrealizedPnl=unrealized,drawdownPenalty=0,terminated=False,truncated=False)
    def test_open_profit_cannot_count_as_completed_goal(self):
        reward,terms=constrained_reward(self.state(),self.state(unrealized=100),trade_incentive=1,potential_weight=0)
        self.assertEqual(reward,0);self.assertEqual(terms['completedTradesDelta'],0)
    def test_loss_and_net_win_constraints_use_shared_completed_state(self):
        reward,terms=constrained_reward(self.state(),self.state(1,-50,0),trade_incentive=1,potential_weight=0)
        self.assertEqual(terms['grossConstraint'],-4)
        self.assertAlmostEqual(terms['winConstraint'],-2.4)
        self.assertAlmostEqual(reward,-6.4)
        reward,terms=constrained_reward(self.state(),self.state(1,150,1),trade_incentive=1,potential_weight=0)
        self.assertEqual(reward,4)
    def test_frequency_incentive_stops_at_daily_target(self):
        _,terms=constrained_reward(self.state(50,5000,25),self.state(51,5150,26),trade_incentive=3,potential_weight=0)
        self.assertEqual(terms['frequencyIncentive'],0)
    def test_terminal_potential_removed_and_inputs_unchanged(self):
        before=self.state(unrealized=100);after=dict(self.state(unrealized=200),terminated=True)
        _,terms=constrained_reward(before,after,potential_weight=.1)
        self.assertEqual(terms['potential'],-.2);self.assertEqual(after['unrealizedPnl'],200)
    def test_invalid_shared_counts_rejected(self):
        with self.assertRaises(ValueError):constrained_reward(self.state(1,100,1),self.state())

if __name__=='__main__':unittest.main()
