import math
import unittest
from datetime import datetime, timedelta, timezone
from minute_price_factors import PriceMinuteBar
from minute_smart_money import minute_smart_money


class SmartMoneyTests(unittest.TestCase):
    def setUp(self):
        self.at = datetime(2026, 10, 1, 10, tzinfo=timezone(timedelta(hours=8)))

    def bars(self, prices, volumes):
        return [PriceMinuteBar(self.at-timedelta(minutes=len(prices)-1-i), p*v, v, True,
                               p, p, p, p) for i, (p,v) in enumerate(zip(prices,volumes))]

    def test_equal_score_earlier_bars_first_no_fractional_bar(self):
        # Zero returns tie: first four of 20 current bars fit exactly20%.
        bars = self.bars([100.]*21, [1.]*21)
        self.assertEqual(minute_smart_money(self.at,bars)[58], 0.)

    def test_hand_calculated_price_volume_prefix(self):
        prices = [100.,110.,100.]+[100.]*18
        volumes = [1.,1.,1.]+[10.]*18
        # First two highest S fit20%; then three earliest10-volume bars.
        expected=((110+100+100*30)/32)/((110+100+100*180)/182)-1
        self.assertAlmostEqual(minute_smart_money(self.at,self.bars(prices,volumes))[58],expected)

    def test_empty_prefix_remains_missing(self):
        bars = self.bars([100.,200.]+[200.]*19,[1.,1000.]+[1.]*19)
        self.assertTrue(math.isnan(minute_smart_money(self.at,bars)[58]))

    def test_previous_zero_volume_close_cannot_be_carried_forward(self):
        bars=self.bars([100.]*21,[0.]+[1.]*20)
        self.assertTrue(math.isnan(minute_smart_money(self.at,bars)[58]))

    def test_future_gap_missing_previous_and_timezone(self):
        bars=self.bars([100.]*21,[1.]*21)
        future=PriceMinuteBar(self.at+timedelta(minutes=1), -1,-1,False)
        self.assertEqual(minute_smart_money(self.at,bars+[future])[58],0.)
        self.assertTrue(math.isnan(minute_smart_money(self.at,bars[1:])[58]))
        broken=list(bars);broken[5]=PriceMinuteBar(bars[5].end,100,1,False,100,100,100,100)
        self.assertTrue(math.isnan(minute_smart_money(self.at,broken)[58]))
        with self.assertRaises(ValueError):minute_smart_money(self.at.replace(tzinfo=None),bars)


if __name__=='__main__':unittest.main()
