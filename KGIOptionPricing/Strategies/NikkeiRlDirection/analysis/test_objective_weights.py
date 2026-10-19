import unittest
from training_objective import objective_config,constrained_reward

class WeightTests(unittest.TestCase):
    def state(self,trades=0,gross=0,wins=0):
        return dict(completedTrades=trades,completedGrossPnl=gross,winRate=100*wins/trades if trades else None,
          unrealizedPnl=0,drawdownPenalty=0,terminated=False,truncated=False)
    def test_strong_win_penalty_rejects_one_tick_profit_after_cost(self):
        cfg=objective_config('constrained-gross',20,80,4)
        reward,_=constrained_reward(self.state(),self.state(1,50,0),**cfg)
        self.assertAlmostEqual(reward,-11)
        winning,_=constrained_reward(self.state(),self.state(1,100,1),**cfg)
        self.assertEqual(winning,22)
        waiting,_=constrained_reward(self.state(),self.state(),**cfg)
        self.assertEqual(waiting,0)
    def test_default_configuration_preserves_historical_objective(self):
        self.assertEqual(objective_config('constrained-gross',1,6,2),dict(trade_incentive=1,win_weight=6,gross_weight=2))
        self.assertIsNone(objective_config('ledger',1,6,2))
    def test_nonfinite_and_negative_weights_rejected_before_environment(self):
        for weights in [(float('nan'),6,2),(1,float('inf'),2),(1,6,-1)]:
            with self.assertRaises(ValueError):objective_config('constrained-gross',*weights)
        with self.assertRaises(ValueError):objective_config('bad',1,6,2)

if __name__=='__main__':unittest.main()
