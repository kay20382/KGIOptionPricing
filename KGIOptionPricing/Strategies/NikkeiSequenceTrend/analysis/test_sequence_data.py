import unittest
import numpy as np
from datetime import datetime,timezone
from sequence_dataset import build_samples, aggregate_book
from session_context import context_features

class DataTests(unittest.TestCase):
    def test_future_health_cannot_change_trading_eligibility(self):
        x=np.zeros((300,39),np.float32);v=np.ones(300,bool)
        a=build_samples(x,np.arange(300),np.arange(300)*5.,v)
        v[155]=False;b=build_samples(x,np.arange(300),np.arange(300)*5.,v)
        self.assertIn(150,a.inference_indices);self.assertIn(150,b.inference_indices)
        self.assertIn(299,a.inference_indices)
        self.assertNotIn(150,b.indices)
    def test_recorder_seven_digit_timestamp(self):
        from sequence_dataset import parse_timestamp
        at=parse_timestamp('2026-09-15T07:20:00.1704984+08:00')
        self.assertEqual(at.hour,7);self.assertEqual(at.microsecond,170498)
    def test_recorder_variable_precision_timestamp(self):
        from sequence_dataset import parse_timestamp
        for fraction in ('9','90','900','9008','90084','900840','9008400'):
            at=parse_timestamp('2026-09-15T07:20:00.'+fraction+'+08:00')
            self.assertEqual(at.utcoffset().total_seconds(),28800)
            self.assertEqual(at.microsecond,int(fraction[:6].ljust(6,'0')))
    def test_future_changes_do_not_change_input(self):
        x=np.zeros((300,39),np.float32);mid=np.arange(300,dtype=float);times=np.arange(300)*5.
        a=build_samples(x,mid,times,np.ones(300,bool))
        x[200:]=999
        b=build_samples(x,mid,times,np.ones(300,bool))
        self.assertEqual(a.indices[0],119)
        np.testing.assert_array_equal(a.window(150)[0],b.window(150)[0])
    def test_gap_cannot_be_a_training_window(self):
        x=np.zeros((300,39),np.float32);v=np.ones(300,bool);v[130]=False
        d=build_samples(x,np.arange(300),np.arange(300)*5.,v)
        self.assertFalse(any(124<=i<=249 for i in d.indices))
    def test_neutral_boundary(self):
        x=np.zeros((200,39),np.float32)
        d=build_samples(x,np.arange(200)*5/3,np.arange(200)*5.,np.ones(200,bool))
        self.assertTrue(all(d.classes==1)) # 30s move=10points exactly2ticks
    def test_depth_is_not_missing_as_zero(self):
        with self.assertRaises(ValueError):aggregate_book([1],[2],[1],[1],5)
    def test_dst_changes_us_open(self):
        summer=context_features(datetime.fromisoformat('2026-10-14T21:35:00+08:00'),{})
        winter=context_features(datetime.fromisoformat('2026-12-14T22:35:00+08:00'),{})
        self.assertEqual(len(summer),19);self.assertEqual(summer[5],1);self.assertEqual(winter[5],1)
        self.assertEqual(context_features(datetime.fromisoformat('2026-12-14T21:35:00+08:00'),{})[5],0)
    def test_missing_calendar_is_unknown(self):
        self.assertEqual(context_features(datetime.fromisoformat('2026-10-14T20:30:00+08:00'),{})[-1],0)

if __name__=='__main__':unittest.main()
