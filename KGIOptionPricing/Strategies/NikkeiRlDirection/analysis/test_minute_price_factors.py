import unittest,math
from datetime import datetime,timedelta,timezone
from minute_price_factors import PriceMinuteBar,minute_price_candidates

class MinutePriceTests(unittest.TestCase):
    def setUp(self):
        self.at=datetime(2026,10,5,9,5,tzinfo=timezone(timedelta(hours=8)))
        self.bars=[PriceMinuteBar(self.at-timedelta(minutes=4-i),1000*(i+1),10*(i+1),True,100,104,98,102+i) for i in range(5)]
        # Keep all OHLC values consistent with each minute's range.
        self.bars=[PriceMinuteBar(b.end,b.amount,b.volume,b.complete,100,110,98,b.close) for b in self.bars]
    def test_amount_and_close_volume_are_separate_definitions(self):
        out=minute_price_candidates(self.at,self.bars)
        expected=sum((2*(b.high-b.low)-abs(b.close-b.open))/(10*b.amount) for b in self.bars)
        self.assertAlmostEqual(out[55],expected)
        ratio=sum(b.close*b.volume for b in self.bars)/sum(b.volume for b in self.bars)/(sum(b.close for b in self.bars)/5)-1
        self.assertAlmostEqual(out[60],ratio)
    def test_future_bar_does_not_affect_closed_window(self):
        baseline=minute_price_candidates(self.at,self.bars)
        future=PriceMinuteBar(self.at+timedelta(minutes=1),1,1,True,1000,2000,1,1500)
        self.assertEqual(baseline,minute_price_candidates(self.at,self.bars+[future]))
    def test_missing_gap_stale_and_invalid_ohlc(self):
        for bars in [self.bars[:-1],self.bars[1:],self.bars+[PriceMinuteBar(self.at,1,1,True,1,2,1,1)]]:
            if len(bars)==6:
                with self.assertRaises(ValueError):minute_price_candidates(self.at,bars)
            else:self.assertTrue(all(math.isnan(v) for v in minute_price_candidates(self.at,bars).values()))
        wrong=PriceMinuteBar(self.bars[0].end,1,1,True,100,90,95,100)
        with self.assertRaises(ValueError):minute_price_candidates(self.at,[wrong]+self.bars[1:])
    def test_observed_zero_trade_bar_not_zero_imputed(self):
        empty=PriceMinuteBar(self.bars[0].end,0,0,True,None,None,None,None)
        out=minute_price_candidates(self.at,[empty]+self.bars[1:])
        self.assertTrue(math.isfinite(out[55]));self.assertTrue(math.isnan(out[60]))
        zero=[PriceMinuteBar(b.end,0,0,True,None,None,None,None) for b in self.bars]
        self.assertTrue(all(math.isnan(v) for v in minute_price_candidates(self.at,zero).values()))
if __name__=='__main__':unittest.main()
