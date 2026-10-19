import unittest
from unittest.mock import patch
from dual_feedback import DualConstraintFeedback

def terminal(n,g,w):
    return dict(completedTrades=n,completedGrossPnl=g,completedNetPnl=g-72*n,
        winRate=100*w/n if n else None,terminated=True,truncated=False)

class DualTests(unittest.TestCase):
    def controller(self):return DualConstraintFeedback(dict(trade_incentive=20,win_weight=80,gross_weight=4),rates=(10,20,2))
    def test_no_trades_raise_only_frequency_weight(self):
        c=self.controller();record=c.update('T',terminal(0,0,0))
        self.assertEqual(c.config(),dict(trade_incentive=30,win_weight=80,gross_weight=4))
        self.assertIsNone(record['netWinRatePercent']);self.assertIsNone(record['grossExpectancyJPY'])
    def test_bad_quality_raises_penalties_despite_sufficient_trades(self):
        c=self.controller();c.update('T',terminal(100,0,20))
        self.assertEqual(c.config(),dict(trade_incentive=10,win_weight=84,gross_weight=6))
    def test_all_targets_pass_reduce_weights_with_bounds(self):
        c=self.controller();c.update('T',terminal(100,10000,60))
        self.assertEqual(c.config(),dict(trade_incentive=10,win_weight=76,gross_weight=2))
        for _ in range(100):c.update('T',terminal(100,10000,60))
        self.assertEqual(c.config(),dict(trade_incentive=0,win_weight=0,gross_weight=0))
    def test_unfinished_data_cannot_update_feedback(self):
        c=self.controller();before=c.config()
        for change in [dict(terminated=False),dict(truncated=True)]:
            with self.assertRaises(ValueError):c.update('T',dict(terminal(0,0,0),**change))
        self.assertEqual(c.config(),before)
    def test_invalid_rates_and_nonfinite_ledger_rejected(self):
        with self.assertRaises(ValueError):DualConstraintFeedback(dict(trade_incentive=20,win_weight=80,gross_weight=4),rates=(float('nan'),20,2))
        with self.assertRaises(ValueError):self.controller().update('T',terminal(1,float('nan'),0))
    def test_feedback_is_training_only_before_starting_process(self):
        from rl_env import NikkeiRlEnv
        with patch('rl_env.subprocess.Popen') as p:
            with self.assertRaises(ValueError):NikkeiRlEnv('B','D',['T'],training=False,training_objective=dict(trade_incentive=20,win_weight=80,gross_weight=4),dual_feedback=self.controller())
            p.assert_not_called()
    def test_weights_change_only_after_terminal_and_apply_at_next_reset(self):
        import numpy as np
        from rl_env import NikkeiRlEnv
        def state(done):
            return dict(terminal(0,0,0),terminated=done,reward=0,unrealizedPnl=0,drawdownPenalty=0,
                status='Running',observation=np.zeros(40).tolist(),mask=[True]*5)
        c=self.controller()
        with patch('rl_env.subprocess.Popen'),patch.object(NikkeiRlEnv,'request',side_effect=[state(False),state(False),state(True),state(False)]):
            env=NikkeiRlEnv('B','D',['T1','T2'],training=True,training_objective=c.config(),dual_feedback=c,cycle_training_episodes=True)
            env.reset();self.assertEqual(env.episode,'T1');env.step(0)
            self.assertEqual(c.history,[]);self.assertEqual(env.training_objective['trade_incentive'],20)
            env.step(0);self.assertEqual(len(c.history),1)
            self.assertEqual(env.training_objective['trade_incentive'],20)
            env.reset();self.assertEqual(env.episode,'T2');self.assertEqual(env.training_objective['trade_incentive'],30)
    def test_greedy_assessment_updates_feedback_instead_of_sampled_trade_count(self):
        import numpy as np
        from rl_env import NikkeiRlEnv
        c=self.controller()
        start=dict(terminal(0,0,0),terminated=False,reward=0,unrealizedPnl=0,drawdownPenalty=0,status='Running',observation=np.zeros(40).tolist(),mask=[True]*5)
        sampled=dict(start,**terminal(100,0,20))
        assessment=lambda ep:terminal(0,0,0)
        with patch('rl_env.subprocess.Popen'),patch.object(NikkeiRlEnv,'request',side_effect=[start,sampled]):
            env=NikkeiRlEnv('B','D',['T'],training=True,training_objective=c.config(),dual_feedback=c,dual_assessment=assessment)
            env.reset();env.step(0)
        self.assertEqual(c.config()['trade_incentive'],30)
        self.assertEqual(c.history[0]['completedTrades'],0)
        self.assertEqual(c.history[0]['sampledTrainingEpisode']['completedTrades'],100)

if __name__=='__main__':unittest.main()
