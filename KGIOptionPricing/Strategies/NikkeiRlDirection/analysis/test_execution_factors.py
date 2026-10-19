import unittest
import torch
from execution_factors import targets_from_example,ExecutionFactorHead,copy_augmented_linear

class FactorTests(unittest.TestCase):
    def test_entry_time_is_capped_at_prediction_horizon(self):
        side=lambda at:dict(state=dict(completedGrossPnl=-100),fills=[dict(role=0,timestamp=at)])
        row=dict(at='2026-09-28T08:00:00+08:00',long=side('2026-09-28T08:00:03+08:00'),short=side('2026-09-28T08:00:18+08:00'))
        target=targets_from_example(row)
        self.assertEqual(target[:4],[1,0,.2,1])
        self.assertAlmostEqual(target[4],torch.log(torch.tensor(3.)).item(),places=6)
    def test_unfilled_has_wait_cap_and_no_adverse_label(self):
        row=dict(at='2026-09-28T08:00:00+08:00',long=dict(fills=[],state=dict(completedGrossPnl=0)),short=dict(fills=[],state=dict(completedGrossPnl=0)))
        self.assertEqual(targets_from_example(row),[0,0,1,1,0,0,0,0])
    def test_dotnet_submicrosecond_after_horizon_is_censored(self):
        side=dict(state=dict(completedGrossPnl=100),fills=[dict(role=0,timestamp='2026-09-28T08:00:15.0000001+08:00')])
        row=dict(at='2026-09-28T08:00:00+08:00',long=side,short=side)
        self.assertEqual(targets_from_example(row)[:2],[0,0])
    def test_factors_ignore_position_and_account_path_features(self):
        head=ExecutionFactorHead();x=torch.zeros(2,60);x[1,30:40]=100
        self.assertTrue(torch.equal(head(x)[0],head(x)[1]))
    def test_zero_padded_policy_preserves_parent_output(self):
        old=torch.nn.Linear(60,64);new=torch.nn.Linear(66,64)
        copy_augmented_linear(old,new)
        x=torch.randn(4,60);extra=torch.randn(4,6)
        self.assertTrue(torch.allclose(old(x),new(torch.cat([x,extra],dim=1)),atol=1e-6))
    def test_head_outputs_have_probability_wait_and_cost_bounds(self):
        y=ExecutionFactorHead()(torch.randn(7,60))
        self.assertEqual(y.shape,(7,8));self.assertTrue(torch.all(y[:,:4]>=0));self.assertTrue(torch.all(y[:,:4]<=1));self.assertTrue(torch.all(y[:,4:]>=0))

if __name__=='__main__':unittest.main()
