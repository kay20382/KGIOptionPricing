import math
import unittest
from datetime import datetime,timedelta,timezone
from minute_factor_calculations import MinuteBar,minute_candidates

class MinuteFactorTests(unittest.TestCase):
    def setUp(self):
        self.at=datetime(2026,10,6,10,5,tzinfo=timezone(timedelta(hours=8)))
    def bars(self,amounts,volumes=None):
        return [MinuteBar(self.at-timedelta(minutes=4-i),a,(volumes or amounts)[i],True) for i,a in enumerate(amounts)]
    def test_uniform_turnover_entropy_and_known_constant_volume(self):
        result=minute_candidates(self.at,self.bars([1]*5))
        self.assertAlmostEqual(result[56],math.log(5))
        self.assertEqual(result[57],0)
        self.assertTrue(math.isnan(result[59]))
    def test_increasing_and_alternating_turnover_autocorrelation(self):
        self.assertAlmostEqual(minute_candidates(self.at,self.bars([1,2,3,4,5]))[59],1)
        self.assertAlmostEqual(minute_candidates(self.at,self.bars([1,2,1,2,1]))[59],-1)
    def test_future_minute_does_not_change_current_factor(self):
        bars=self.bars([1,2,3,4,5]);before=minute_candidates(self.at,bars)
        bars.append(MinuteBar(self.at+timedelta(minutes=1),999999,999999,True))
        self.assertEqual(before,minute_candidates(self.at,bars))
    def test_incomplete_missing_or_stale_minute_never_zero_filled(self):
        bars=self.bars([1,2,3,4,5])
        for bad in [bars[:-1],bars[1:],bars[:-1]+[MinuteBar(self.at,5,5,False)]]:
            self.assertTrue(all(math.isnan(v) for v in minute_candidates(self.at,bad).values()))
        stale=[MinuteBar(b.end-timedelta(minutes=1),b.amount,b.volume,True) for b in bars]
        self.assertTrue(all(math.isnan(v) for v in minute_candidates(self.at,stale).values()))
    def test_known_zero_activity_is_not_unknown_data(self):
        result=minute_candidates(self.at,self.bars([0]*5))
        self.assertTrue(math.isnan(result[56]));self.assertEqual(result[57],0)
        self.assertTrue(math.isnan(result[59]))
    def test_negative_or_nonfinite_amount_and_duplicate_end_rejected(self):
        bars=self.bars([1,2,3,4,5])
        for bad in [bars[:-1]+[MinuteBar(self.at,-1,5,True)],bars[:-1]+[MinuteBar(self.at,float('nan'),5,True)],bars+[bars[-1]]]:
            with self.assertRaises(ValueError):minute_candidates(self.at,bad)

if __name__=='__main__':unittest.main()
