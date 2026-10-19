import unittest,numpy as np
from execution_alpha import pooled_features,project_signals,tree_predict,export_head,AlphaForecast,masked_loss
from sequence_dataset import build_samples
class AlphaTests(unittest.TestCase):
    def test_pooling_is_causal_and_schema_stable(self):
        x=np.ones((300,45),np.float32);a=build_samples(x,np.arange(300),np.arange(300)*5.,np.ones(300,bool))
        x[200:]=99;b=build_samples(x,np.arange(300),np.arange(300)*5.,np.ones(300,bool))
        p=pooled_features(a,[150]);q=pooled_features(b,[150]);np.testing.assert_equal(p,q)
        self.assertEqual(p.shape,(1,271))
    def test_signed_net_forecast_requires_alignment_and_positive_edge(self):
        p,d=project_signals(np.array([[.5,-2],[.5,2],[-1,2]]),np.array([[.7,.8],[.7,.8],[.7,.8]]),np.array([1,0,-1]),np.array([1,1,-1]))
        self.assertEqual(d.tolist(),[.5,0.,-2.]);self.assertEqual(p[1].tolist(),[0.,1.,0.])
    def test_exported_tree_reproduces_sklearn_head(self):
        from sklearn.ensemble import HistGradientBoostingRegressor
        x=np.arange(100,dtype=float).reshape(-1,1);y=np.where(x[:,0]<50,-5.,10.)
        m=HistGradientBoostingRegressor(max_iter=8,max_leaf_nodes=3,min_samples_leaf=5,early_stopping=False).fit(x,y)
        np.testing.assert_allclose(tree_predict(export_head(m),x),m.predict(x),atol=1e-10)
    def test_neural_heads_have_no_carried_window_state(self):
        import torch
        for kind in ('linear','gru','lstm'):
            model=AlphaForecast(kind).eval();x=torch.zeros(2,120,45);a=torch.zeros(2,16)
            with torch.no_grad():p,n=model(x,a);model(x+1,a);p2,n2=model(x,a)
            self.assertEqual(tuple(p.shape),(2,2));self.assertEqual(tuple(n.shape),(2,2))
            torch.testing.assert_close(p,p2);torch.testing.assert_close(n,n2)
    def test_unfilled_side_cannot_learn_a_fake_zero_loss(self):
        import torch
        logits=torch.zeros(1,2,requires_grad=True);net=torch.ones(1,2,requires_grad=True)
        loss=masked_loss(logits,net,torch.tensor([[2.,0.]]),torch.tensor([[True,False]]));loss.backward()
        self.assertEqual(net.grad[0,1].item(),0);self.assertEqual(logits.grad[0,1].item(),0)
        self.assertNotEqual(net.grad[0,0].item(),0)
if __name__=='__main__':unittest.main()