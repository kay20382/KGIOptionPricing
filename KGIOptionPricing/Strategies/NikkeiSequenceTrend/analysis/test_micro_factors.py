import unittest
from micro_factors import MicroFrame
class MicroFactorTests(unittest.TestCase):
    def test_known_buy_sell_and_unknown_are_not_conflated(self):
        m=MicroFrame();m.trade(105,4,100,105,.5);m.trade(100,2,100,105,.5);m.trade(102,2,100,105,.5)
        factors=m.drain();self.assertAlmostEqual(factors[0],.25);self.assertAlmostEqual(factors[1],0)
        self.assertEqual(m.drain()[0],0)
    def test_stale_or_future_quote_cannot_classify_trade(self):
        m=MicroFrame();m.trade(105,4,100,105,3);m.trade(100,4,100,105,-.1)
        self.assertEqual(m.drain()[0],0)
    def test_depth_change_is_relative_to_preceding_frame(self):
        m=MicroFrame();m.book(10,10);self.assertEqual(m.drain()[2],0)
        m.book(20,5);self.assertAlmostEqual(m.drain()[2],.75)
        self.assertEqual(m.drain()[2],0)
    def test_reset_cannot_carry_prior_session_flow(self):
        m=MicroFrame();m.trade(105,4,100,105,.5);m.reset();self.assertEqual(m.drain(),[0.,0.,0.])
if __name__=='__main__':unittest.main()