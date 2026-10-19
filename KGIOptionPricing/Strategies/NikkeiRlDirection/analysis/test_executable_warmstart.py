import unittest
from executable_warmstart import validate_example, target_for, validate_training_config,validate_label_config

class ExecutableWarmstartTests(unittest.TestCase):
    def test_power_profile_requires_direction_holding_without_timeout(self):
        c=dict(decisionLatencyMs=1,decisionIntervalSeconds=3,cooldownSeconds=10,commission=36,stopTicks=6,maxHoldingSeconds=0,observationSize=40,tickSize=5,multiplier=10,rewardCostWeight=1,allowIncompleteEvaluation=False,featureProfile='mini-micro-power',directionDrivenHolding=True)
        validate_label_config(c,15)
        for key,value in [('maxHoldingSeconds',60),('directionDrivenHolding',False),('featureProfile','unknown')]:
            with self.assertRaises(ValueError):validate_label_config(dict(c,**{key:value}),15)
    def test_training_environment_must_match_label_environment(self):
        label=dict(decisionLatencyMs=1,cooldownSeconds=10,rewardCostWeight=1,observationSize=40,decisionIntervalSeconds=3)
        validate_training_config(label,dict(label))
        for incomplete in [None,{}, {k:v for k,v in label.items() if k!='decisionIntervalSeconds'}]:
            with self.assertRaises(ValueError):validate_training_config(label,incomplete)
        for key,value in [('decisionLatencyMs',100),('cooldownSeconds',20),('rewardCostWeight',0),('decisionIntervalSeconds',5)]:
            with self.subTest(key=key):
                with self.assertRaises(ValueError):validate_training_config(label,dict(label,**{key:value}))
    def row(self):
        obs=[0.0]*40;obs[38]=1;obs[39]=1
        return dict(at='2026-09-29T08:00:00+08:00',observation=obs,action=2,proxyAction=1,
                    long=dict(status='Completed',completed=True,state=dict(completedTrades=1,position=0,completedNetPnl=-72)),
                    short=dict(status='Completed',completed=True,state=dict(completedTrades=1,position=0,completedNetPnl=28)))
    def test_matched_targets_preserve_same_observation(self):
        r=self.row();validate_example(r)
        self.assertEqual(target_for(r,'proxy'),1);self.assertEqual(target_for(r,'executable'),2)
        self.assertEqual(r['observation'][0],0)
    def test_unmatched_or_censored_cannot_train(self):
        r=self.row();r['proxyAction']=None
        self.assertIsNone(target_for(r,'executable'))
        r=self.row();r['long']['status']='Censored'
        with self.assertRaises(ValueError):validate_example(r)
    def test_label_uses_executable_net_not_future_direction(self):
        r=self.row();r['action']=1
        with self.assertRaises(ValueError):validate_example(r)
    def test_future_outcomes_cannot_be_model_inputs(self):
        r=self.row();r['observation'].append(28)
        with self.assertRaises(ValueError):validate_example(r)
    def test_completed_outcomes_do_not_require_future_book_health(self):
        r=self.row();r['proxyAction']=None;validate_example(r)
        self.assertEqual(target_for(r,'executable',matched_only=False),2)
        with self.assertRaises(ValueError):target_for(r,'proxy',matched_only=False)

if __name__=='__main__':unittest.main()
