import sys, unittest
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[2]/'scripts/research'))
from depth_activity_candidates import common_price_decreases, align_depth_activity


class DepthActivityTests(unittest.TestCase):
    def book(self, at):
        return dict(Time=at,Product='225MC',Kind='depth_delta',Tick=5,Dropped=0,
                    Bid=[100,95,90,85,80],Ask=[105,110,115,120,125],
                    BidQty=[10]*5,AskQty=[10]*5)

    def test_common_prices_only_not_disappearing_levels(self):
        a=self.book(0);b=self.book(1)
        b['BidQty'][0]=9;b['AskQty'][0]=7
        self.assertEqual(common_price_decreases(a,b),2)
        b['Ask']=[110,115,120,125,130];b['AskQty']=[10]*5
        b['BidQty']=[10]*5
        self.assertEqual(common_price_decreases(a,b),0)

    def test_complete_fixed_grid_and_no_boundary_leak(self):
        events=[self.book(0)]
        events += [dict(Time=t,Product='225MC',Kind='trade',Last=100,TradeQty=1,Dropped=0) for t in [1,2,4]]
        events += [dict(Time=t,Product='225M',Kind='depth_delta') for t in [8,16,24]]
        events += [self.book(27),self.book(29),dict(Time=30,Product='225MC',Kind='trade',Last=100,TradeQty=1,Dropped=0)]
        v,_=align_depth_activity(events,[30])
        self.assertEqual(v[0,0],0)
        self.assertEqual(v[0,1],.2) # two occupied 3-second cells, not three prints

    def test_empty_full_window_zero_and_gap_missing(self):
        events=[self.book(0)]+[dict(Time=t,Product='225M',Kind='depth_delta') for t in [8,16,24]]+[self.book(29)]
        v,_=align_depth_activity(events,[30])
        self.assertEqual(v[0,1],0)
        v,_=align_depth_activity([self.book(0),self.book(29)],[30])
        self.assertTrue(np.isnan(v[0]).all())


if __name__=='__main__': unittest.main()
