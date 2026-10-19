import unittest
import numpy as np
import torch
from interaction_head import SoftInteractionHead,tree_arrays,head_spec

class HeadTests(unittest.TestCase):
    def test_soft_tree_approaches_original_classifier_and_ignores_account_path(self):
        from sklearn.ensemble import HistGradientBoostingClassifier
        rng=np.random.default_rng(22);x=rng.normal(size=(300,2));y=np.where(x[:,0]>.3,1,np.where(x[:,1]<-.3,2,0))
        model=HistGradientBoostingClassifier(max_iter=4,max_depth=2,min_samples_leaf=8,early_stopping=False).fit(x,y)
        arrays=tree_arrays(model,[0,40],np.ones(60)*1e-5)
        spec=head_spec(arrays);head=SoftInteractionHead(spec)
        head.load_arrays(arrays,np.zeros(60),np.ones(60)-1e-8)
        raw=np.zeros((300,60),np.float32);raw[:,[0,40]]=x
        actual=head(torch.tensor(raw)).detach().numpy()
        np.testing.assert_allclose(actual,model.predict_proba(x),atol=1e-5)
        raw[:,30:40]=100
        np.testing.assert_allclose(head(torch.tensor(raw)).detach(),actual,atol=1e-6)
    def test_constant_control_has_uniform_probabilities(self):
        spec=dict(leaves=2,depth=1);head=SoftInteractionHead(spec)
        y=head(torch.randn(4,60))
        np.testing.assert_allclose(y.detach(),np.full((4,3),1/3),atol=1e-6)
    def test_denormalization_reconstructs_raw_market_inputs(self):
        arrays=dict(indices=np.array([[0],[0]]),thresholds=np.zeros((2,1)),signs=np.array([[-1],[1]]),
            valid=np.ones((2,1)),temperatures=np.ones((2,1)),values=np.array([[1,0,0],[0,1,0]]),baseline=np.zeros(3))
        a=SoftInteractionHead(head_spec(arrays));b=SoftInteractionHead(head_spec(arrays))
        a.load_arrays(arrays,np.zeros(60),np.ones(60)-1e-8)
        b.load_arrays(arrays,np.ones(60)*5,np.ones(60)*4-1e-8)
        raw=torch.randn(3,60)
        self.assertTrue(torch.allclose(a(raw),b((raw-5)/2),atol=1e-6))

if __name__=='__main__':unittest.main()
