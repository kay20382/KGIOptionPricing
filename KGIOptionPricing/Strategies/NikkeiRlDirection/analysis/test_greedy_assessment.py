import unittest
import numpy as np
from greedy_assessment import assess_training_episode

class GreedyAssessmentTests(unittest.TestCase):
    def test_development_date_rejected_before_environment_reset(self):
        class Env:
            training=False
            def reset(self,**kw):raise AssertionError('Must reject before replay')
        with self.assertRaises(ValueError):assess_training_episode(None,None,Env(),'20261006',['20260929'])
    def test_only_deterministic_actions_and_frozen_statistics(self):
        class Env:
            training=False
            def reset(self,**kw):return np.zeros(40),{}
            def action_masks(self):return np.ones(5,dtype=bool)
            def step(self,a):return np.zeros(40),0,True,False,{'completedTrades':0,'terminated':True,'truncated':False}
        class Norm:
            def normalize_obs(self,x):return x
        class Model:
            def predict(self,x,action_masks,deterministic):
                if not deterministic:raise AssertionError('Greedy assessment required')
                return np.array([0]),None
        result=assess_training_episode(Model(),Norm(),Env(),'T',['T'])
        self.assertEqual(result['completedTrades'],0)

if __name__=='__main__':unittest.main()
