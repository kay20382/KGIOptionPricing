import unittest
import numpy as np
from prediction_fill_benchmark import label_samples, market_columns, quote_markout, daily_metrics, verify_pin, prediction_metrics
from pathlib import Path
from tempfile import TemporaryDirectory

class BenchmarkTests(unittest.TestCase):
    def frames(self):
        return [dict(t=float(i*3),bid=100+i*5,ask=105+i*5,valid=True,
                     gap=False,x=[0.]*60,entryAllowed=True) for i in range(8)]

    def test_exact_future_label_and_causal_feature(self):
        rows=self.frames();rows[0]['x'][0]=7;rows[5]['x'][0]=999
        x,y,indices=label_samples(rows,15)
        self.assertEqual(indices.tolist(),[0,1,2]);self.assertEqual(y.tolist(),[5.,5.,5.])
        self.assertEqual(x[0,0],7)

    def test_gap_and_future_invalid_cannot_be_labels(self):
        rows=self.frames();rows[3]['valid']=False
        self.assertEqual(len(label_samples(rows,15)[1]),0)
        rows=self.frames();rows[3]['t']=10
        self.assertEqual(len(label_samples(rows,15)[1]),0)

    def test_market_inputs_exclude_ledger_and_disabled_flow(self):
        columns=market_columns()
        self.assertFalse(set(columns)&set(range(30,39)))
        self.assertFalse(set(columns)&set(range(48,54)))
        self.assertIn(29,columns);self.assertIn(59,columns)

    def test_labels_need_entry_readiness_and_full_horizon(self):
        rows=self.frames();rows[0]['entryAllowed']=False
        self.assertEqual(label_samples(rows,15)[2].tolist(),[1,2])
        self.assertEqual(len(label_samples(rows,30)[1]),0)

    def test_markout_short_and_measured_snapshot_delay(self):
        rows=self.frames()
        value=quote_markout(rows,1.,100.,-1,3)
        self.assertEqual(value,dict(valueJPY=-125.,delaySeconds=2.))

    def test_markout_rejects_stale_invalid_or_missing_future(self):
        rows=self.frames();rows[2]['valid']=False
        self.assertIsNone(quote_markout(rows,1.,100.,1,3))
        self.assertIsNone(quote_markout(rows,20.,100.,1,3))

    def test_zero_trades_no_fabricated_zero_expectancy(self):
        row=daily_metrics([])
        self.assertIsNone(row['winRatePercent']);self.assertIsNone(row['expectedGrossPnlPerTradeJPY'])

    def test_completed_only_metrics_include_losses(self):
        rows=[dict(trades=2,wins=1,losses=1,grossPnlJPY=100,netPnlJPY=-44,
                   costsJPY=144,winningNetJPY=64,losingNetJPY=-108,excludedOpenPositions=1,
                   excludedEntryCostsJPY=36,excludedUnrealizedJPY=50,excludedOpenNetPnlJPY=14,
                   rawNetPnlJPY=-30)]
        value=daily_metrics(rows)
        self.assertEqual(value['expectedGrossPnlPerTradeJPY'],50)
        self.assertEqual(value['averageLossJPY'],-108)
        self.assertEqual(value['netPnlJPY'],-44)

    def test_pin_detects_mutation(self):
        import hashlib
        with TemporaryDirectory() as d:
            p=Path(d)/'index.json';p.write_text('{}')
            pin={str(p):hashlib.sha256(p.read_bytes()).hexdigest().upper()}
            verify_pin(pin);p.write_text('{"fullDayCoverage":true}')
            with self.assertRaises(ValueError):verify_pin(pin)

    def test_squared_loss_scoring_matches_regression_training(self):
        value=prediction_metrics(np.array([0.,2.,-2.]),np.array([0.,1.,-1.]))
        self.assertAlmostEqual(value.get('mseTicksSquared',-1),2/3)
        self.assertAlmostEqual(value.get('zeroMseTicksSquared',-1),8/3)
        self.assertAlmostEqual(value.get('rSquared',-1),.75)

    def test_direction_accuracy_reports_its_actual_denominator(self):
        value=prediction_metrics(np.array([0.,2.,-2.]),np.array([2.,2.,-2.]))
        self.assertEqual(value.get('signalNonzeroActualCount',-1),2)
        self.assertEqual(value['signalDirectionAccuracyPercent'],100)

    def test_fill_prediction_strength_preserves_missing_actor_scores(self):
        import prediction_fill_benchmark as benchmark
        attempts=[dict(filled=True,predictionTicks=2.),dict(filled=True,predictionTicks=4.),
                  dict(filled=True,predictionTicks=None),dict(filled=False,predictionTicks=-2.),
                  dict(filled=False,predictionTicks=float('nan'))]
        value=getattr(benchmark,'prediction_cohorts',lambda _:{})(attempts)
        self.assertEqual(value.get('filled',{}).get('count',-1),2)
        self.assertEqual(value['filled']['missing'],1)
        self.assertEqual(value['filled']['mean'],3)
        self.assertEqual(value['filled']['p50'],3)
        self.assertEqual(value['unfilled']['count'],1)
        self.assertEqual(value['unfilled']['missing'],1)
        self.assertEqual(value['unfilled']['mean'],-2)
        self.assertEqual(value['unfilled']['absolute']['mean'],2)

if __name__=='__main__':unittest.main()
