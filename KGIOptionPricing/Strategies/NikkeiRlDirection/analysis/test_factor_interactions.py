import unittest
import numpy as np
from factor_interactions import market_inputs, temporal_folds, block_lower_bound, retained

class InteractionTests(unittest.TestCase):
    def test_market_features_exclude_account_path_and_removed_flow(self):
        x=np.arange(120,dtype=float).reshape(2,60)
        a,indices=market_inputs(x,False)
        self.assertFalse(set(indices)&set(range(30,40)))
        self.assertFalse(set(indices)&set(range(48,54)))
        x[:,30:40]=999
        np.testing.assert_array_equal(a,market_inputs(x,False)[0])
        self.assertEqual(market_inputs(x,True)[0].shape,(2,50))
    def test_fold_never_uses_future_episode(self):
        dates=['20260928','20260929','20260930','20261005']
        folds=temporal_folds(dates)
        self.assertEqual(folds,[(dates[:2],dates[2]),(dates[:3],dates[3])])
        with self.assertRaises(ValueError):temporal_folds(dates[::-1])
        with self.assertRaises(ValueError):temporal_folds(['20260928']*4)
    def test_bootstrap_resamples_time_blocks_and_rejects_no_signal(self):
        blocks=np.repeat(np.arange(10),5)
        self.assertAlmostEqual(block_lower_bound([np.ones(50)*.02]*2,[blocks]*2),.02)
        self.assertFalse(retained([.02,-.001],.001))
        self.assertFalse(retained([.003,.003],.002))
        self.assertFalse(retained([.01,.01],0))
        self.assertTrue(retained([.02,.01],.003))
    def test_nonfinite_input_rejected(self):
        with self.assertRaises(ValueError):market_inputs(np.full((2,60),np.nan),True)

if __name__=='__main__':unittest.main()
