import unittest, math
import numpy as np
import factor_significance as f

class FactorScreenTests(unittest.TestCase):
    def test_by_correction_covers_whole_family_and_restores_order(self):
        np.testing.assert_allclose(f.by_adjust([.03,.01,.2]),[.0825,.055,.3666666666666667])
    def test_future_values_do_not_change_training_transform(self):
        model=f.fit_univariate(np.array([0.,1.,2.]),np.array([1.,3.,5.]),'linear')
        np.testing.assert_allclose(f.predict_univariate(model,np.array([3.,100.])),[7.,201.])
    def test_bins_use_train_cutpoints_and_empty_bins_train_mean(self):
        model=f.fit_univariate(np.arange(10.),np.arange(10.)*2,'bins')
        self.assertEqual(f.predict_univariate(model,np.array([-999.,999.])).tolist(),[1.,17.])
    def test_missing_and_constant_not_fabricated_as_predictors(self):
        self.assertIsNone(f.fit_univariate(np.ones(4),np.arange(4.),'linear'))
        self.assertTrue(math.isnan(f.predict_univariate(dict(kind='linear',mean=0.,scale=1.,intercept=0.,slope=2.),np.array([math.nan]))[0]))
    def test_time_blocks_preserve_real_gaps(self):
        blocks=f.time_block_means(np.array([0.,3.,1200.,1203.]),np.array([1.,3.,5.,7.]),600)
        np.testing.assert_allclose(blocks,[2.,6.])
    def test_book_formulas_sweep_and_concentration(self):
        row=f.book_factors([100.,95.,90.,85.,80.],[105.,110.,115.,120.,125.],[5]*5,[10]*5)
        self.assertEqual(row.get(2,-1),75.)
        self.assertAlmostEqual(row[3],-1/3)
        self.assertEqual(row[8],1.)
        self.assertEqual(row[51],0.)
    def test_dedup_uses_train_spearman_and_family(self):
        rows=[dict(key='a',family='a'),dict(key='b',family='b'),dict(key='c',family='c')]
        selected=f.deduplicate(rows,{'a':np.arange(20.),'b':np.arange(20.)**3,'c':np.tile([0.,1.],10)},10)
        self.assertEqual([r['key'] for r in selected],['a','c'])
    def test_vcvar_needs120_closed_window100_valid_and_includes_ties(self):
        for count in (99,100,119):self.assertTrue(math.isnan(f.vcvar([dict(complete=True,volume=1.,vwar=-.01)]*count)))
        bars=[dict(complete=True,volume=1.,vwar=x) for x in [-.1]*6+[.01]*94]+[dict(complete=True,volume=0.,vwar=None)]*20
        self.assertAlmostEqual(f.vcvar(bars),-.1)
        self.assertTrue(math.isnan(f.vcvar(bars[:99]+[dict(complete=True,volume=0.,vwar=None)]*21)))

    def test_score_zero_incremental_skill_not_significant(self):
        rows=[dict(times=np.arange(2400.)*3,delta=np.zeros(2400)) for _ in range(5)]
        value=f.block_evidence(rows)
        self.assertEqual(value.get('pValue',-1),1.)
        self.assertEqual(value['meanImprovement'],0.)

    def test_positive_blocks_use_real_day_counts(self):
        rows=[dict(times=np.arange(2400.)*3,delta=np.tile([1.,2.],1200)) for _ in range(5)]
        value=f.block_evidence(rows)
        self.assertEqual(value.get('blocksPerDay',[]),[12]*5)
        self.assertLess(value['pValue'],.05)

    def test_unequal_blocks_cannot_reverse_daily_mse_target(self):
        rows=[]
        for day in range(5):
            times=[];delta=[]
            for block in range(20):
                count=200 if block<5 else 1
                times.extend(block*600+np.arange(count)*3)
                delta.extend([(.01 if day<4 else -.15) if block<5 else 1.]*count)
            rows.append(dict(times=np.asarray(times),delta=np.asarray(delta)))
        expected=np.mean([r['delta'].mean() for r in rows]);result=f.block_evidence(rows)
        self.assertLess(expected,0)
        self.assertAlmostEqual(result['meanImprovement'],expected)
        self.assertGreater(result['pValue'],.5)
        self.assertLessEqual(result['lower95'],0)

    def test_vcvar_invalid_minute_prevents_gap_bridge(self):
        bars=[dict(complete=True,volume=1.,vwar=-.01)]*100+[dict(complete=False,volume=1.,vwar=-.1)]
        self.assertTrue(math.isnan(f.vcvar(bars)))

if __name__=='__main__':unittest.main()
