import unittest,types
import numpy as np
import torch
from reward_conditioning import configure_reward_normalization

class Policy(torch.nn.Module):
    def __init__(self):
        super().__init__();self.action_net=torch.nn.Linear(3,5);self.value_net=torch.nn.Linear(3,1)
        self.mlp_extractor=torch.nn.Module();self.mlp_extractor.value_net=torch.nn.Sequential(torch.nn.Linear(3,3),torch.nn.Tanh())
        self.optimizer=torch.optim.Adam(self.parameters())
    def value(self,x):return self.value_net(self.mlp_extractor.value_net(x))

class ConditioningTests(unittest.TestCase):
    def test_reward_normalization_rescales_critic_without_changing_actor_or_observations(self):
        p=Policy();x=torch.randn(4,3);(p.action_net(x).sum()+p.value(x).sum()).backward();p.optimizer.step()
        actor=p.action_net(x).detach().clone();value=p.value(x).detach().clone()
        obs=types.SimpleNamespace(mean=np.array([1.,2.,3.]),var=np.array([2.,3.,4.]))
        n=types.SimpleNamespace(ret_rms=types.SimpleNamespace(var=np.array(400.)),epsilon=1e-8,norm_reward=False,gamma=.99,obs_rms=obs,clip_reward=10)
        info=configure_reward_normalization(n,p,True,.994,parent_normalized=False)
        self.assertTrue(torch.equal(p.action_net(x),actor))
        self.assertTrue(torch.allclose(p.value(x),value/20))
        np.testing.assert_array_equal(n.obs_rms.mean,[1,2,3]);np.testing.assert_array_equal(n.obs_rms.var,[2,3,4])
        self.assertTrue(n.norm_reward);self.assertEqual(n.gamma,.994);self.assertTrue(info['criticRescaled'])
        self.assertIn(p.action_net.weight,p.optimizer.state);self.assertNotIn(p.value_net.weight,p.optimizer.state)
    def test_already_normalized_parent_is_not_rescaled_twice(self):
        p=Policy();x=torch.randn(4,3);old=p.value(x).detach().clone()
        n=types.SimpleNamespace(ret_rms=types.SimpleNamespace(var=np.array(400.)),epsilon=1e-8,norm_reward=True,gamma=.994,clip_reward=10)
        info=configure_reward_normalization(n,p,True,.994,parent_normalized=True)
        self.assertFalse(info['criticRescaled']);self.assertTrue(torch.equal(p.value(x),old))
    def test_default_raw_reward_path_keeps_critic_and_discount_unchanged(self):
        p=Policy();x=torch.randn(4,3);old=p.value(x).detach().clone()
        n=types.SimpleNamespace(ret_rms=types.SimpleNamespace(var=np.array(400.)),epsilon=1e-8,norm_reward=False,gamma=.99,clip_reward=10)
        configure_reward_normalization(n,p,False,.994,parent_normalized=False)
        self.assertTrue(torch.equal(p.value(x),old));self.assertEqual(n.gamma,.99)

if __name__=='__main__':unittest.main()
