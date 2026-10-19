import unittest
import copy
import numpy as np
from collect_factor_panel import calibrate,CALCULABLE

def panel():
    n=260
    rows=[dict(t=3.*i,bid=100.+5*(i%7),ask=105.+5*(i%7),valid=True,gap=False,x=[0.]*60,entryAllowed=True) for i in range(n)]
    values=np.zeros((n,len(CALCULABLE)));values[:,CALCULABLE.index(1)]=1.;values[:,CALCULABLE.index(3)]=.1
    base=np.zeros((n,30));base[:,11]=np.sin(np.arange(n)/3)
    return dict(rows=rows,values=values,base=base)

class CalibrationTests(unittest.TestCase):
    def test_holdout_extremes_do_not_change_training_calibration(self):
        a={'train':panel(),'eval':panel()};b=copy.deepcopy(a)
        b['eval']['base'][:]=1e9
        for r in b['eval']['rows']:r['bid']*=100;r['ask']*=100
        self.assertEqual(calibrate(a,['train']),calibrate(b,['train']))
        np.testing.assert_allclose(a['train']['values'],b['train']['values'],equal_nan=True)
    def test_missing_previous_frame_cannot_supply_lagged_factor(self):
        p=panel();p['rows'][20]['valid']=False
        calibrate({'train':p},['train'])
        self.assertTrue(np.isnan(p['values'][21,CALCULABLE.index(39)]))

if __name__=='__main__':unittest.main()
