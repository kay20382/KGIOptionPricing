import sys, unittest
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[2]/'scripts/research'))
from depth_vwap_candidates import align_depth_vwap


class DepthVwapTests(unittest.TestCase):
    def book(self, at):
        return dict(Time=at, Product='225MC', Kind='depth_delta', Tick=5,
                    Bid=[100,95,90,85,80], Ask=[105,110,115,120,125],
                    BidQty=[10,10,10,10,10], AskQty=[20,10,10,10,10], Dropped=0)

    def trade(self, at, price, qty):
        return dict(Time=at, Product='225MC', Kind='trade', Last=price, TradeQty=qty, Dropped=0)

    def test_boundary_and_weighted_price(self):
        values,_=align_depth_vwap([self.book(0)]+[dict(Time=t,Product='225M',Kind='depth_delta') for t in [5,10,15,20,25]]+
                                 [self.trade(29,100,1),self.trade(29.5,105,3),
                                  self.book(29.9),self.trade(30,1000,1000)], [30])
        self.assertAlmostEqual(values[0,0],.2-1/3)
        self.assertAlmostEqual(values[0,1],(102.5-103.75)/5)

    def test_empty_window_stale_and_gap_are_missing(self):
        values,_=align_depth_vwap([self.book(0),self.book(29)], [30,33])
        self.assertTrue(np.isnan(values[0,1]))
        self.assertTrue(np.isnan(values[1]).all())
        values,_=align_depth_vwap([self.book(0),self.trade(29,100,1),self.book(29.9)], [30])
        # The 29 second receive gap starts a new observed segment: insufficient full VWAP window.
        self.assertTrue(np.isnan(values[0,1]))

    def test_out_of_order_and_dropped_rejected(self):
        with self.assertRaises(ValueError): align_depth_vwap([self.book(2),self.book(1)],[3])
        b=self.book(0);b['Dropped']=1
        with self.assertRaises(ValueError): align_depth_vwap([b],[3])


if __name__=='__main__': unittest.main()
