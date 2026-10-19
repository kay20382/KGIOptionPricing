import unittest
import numpy as np
import torch
from dual_horizon_model import DualForecast,dual_loss,dual_signals,value_future_mask
from sequence_dataset import build_samples
class DualTests(unittest.TestCase):
    def test_value_mse_moves_toward_positive_mean_even_if_median_negative(self):
        net=torch.zeros(11,2,requires_grad=True);p=torch.zeros(11,2,requires_grad=True);f=torch.zeros(11,requires_grad=True)
        y=torch.tensor([[-1.,-1.]]*6+[[10.,10.]]*5);closed=torch.ones(11,2,dtype=torch.bool)
        loss=dual_loss(f,p,net,torch.zeros(11),y,closed);loss.backward()
        self.assertLess(float(net.grad.sum()),0)
    def test_unfilled_value_has_no_gradient_but_forecast_remains_trainable(self):
        net=torch.ones(3,2,requires_grad=True);p=torch.ones(3,2,requires_grad=True);f=torch.ones(3,requires_grad=True)
        loss=dual_loss(f,p,net,torch.zeros(3),torch.zeros(3,2),torch.zeros(3,2,dtype=torch.bool));loss.backward()
        self.assertEqual(float(net.grad.abs().sum()),0);self.assertEqual(float(p.grad.abs().sum()),0);self.assertGreater(float(f.grad.abs().sum()),0)
    def test_thirty_second_direction_and_real_value_are_separate(self):
        p=np.full((3,2),.8);n=np.array([[1.,1.],[-1.,1.],[1.,1.]])
        probabilities,delta=dual_signals(np.array([.2,.2,-.2]),n,p,np.ones(3),np.ones(3))
        np.testing.assert_array_equal(delta,[1.,0.,0.]);self.assertTrue(np.all(probabilities[:,2]==.8))
    def test_recurrent_windows_are_independent(self):
        torch.set_num_threads(2)
        for kind in ('linear','gru','lstm'):
            model=DualForecast(kind).eval();x=torch.randn(2,120,45);a=torch.randn(2,16)
            with torch.no_grad():batch=model(x,a);one=model(x[1:2],a[1:2])
            self.assertEqual([v.shape for v in batch],[torch.Size([2]),torch.Size([2,2]),torch.Size([2,2])])
            for all_rows,row in zip(batch,one):torch.testing.assert_close(all_rows[1:2],row)
    def test_long_future_label_quality_does_not_filter_inference(self):
        valid=np.ones(300,dtype=bool);valid[139]=False
        s=build_samples(np.zeros((300,45)),np.ones(300)*10000,np.arange(300)*5,valid)
        self.assertIn(119,s.inference_indices);self.assertIn(119,s.indices)
        self.assertFalse(value_future_mask(s,np.array([119]))[0])
if __name__=='__main__':unittest.main()