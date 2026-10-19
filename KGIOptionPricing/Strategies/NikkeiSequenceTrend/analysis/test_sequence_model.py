import unittest,torch
from sequence_model import SequenceModel, qualify

class ModelTests(unittest.TestCase):
    def test_shapes_and_no_carried_state(self):
        for kind in ('linear','gru','lstm'):
            m=SequenceModel(kind).eval();x=torch.zeros(2,120,39);a=torch.zeros(2,16)
            with torch.no_grad():p,r=m(x,a);m(torch.ones_like(x),a);p2,r2=m(x,a)
            self.assertEqual(tuple(p.shape),(2,3));self.assertEqual(tuple(r.shape),(2,))
            torch.testing.assert_close(p,p2);torch.testing.assert_close(r,r2)
    def test_strict_thresholds_and_daily_minimum(self):
        self.assertFalse(qualify(10,40,2.1,[100],[10]))
        self.assertFalse(qualify(10,41,2,[100],[10]))
        self.assertFalse(qualify(10,41,2.1,[8000],[10]))
        self.assertTrue(qualify(10,41,2.1,[7999],[10]))
        self.assertFalse(qualify(10,None,None,[0],[10]))

    def test_each_day_cannot_be_replaced_by_period_average(self):
        self.assertFalse(qualify(100,50,2.5,[100,200],[9,11]))
        self.assertTrue(qualify(100,50,2.5,[100,200],[10,10]))
        self.assertFalse(qualify(100,50,2.5,[100,200],[0,20]))
    def test_missing_days_and_mismatched_counts_rejected(self):
        self.assertFalse(qualify(100,50,2.5,[],[]))
        self.assertFalse(qualify(100,50,2.5,[100,200],[20]))

if __name__=='__main__':unittest.main()
