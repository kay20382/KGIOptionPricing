import unittest
from analyze_main_strategy import summarize

class Tests(unittest.TestCase):
    def fixture(self):
        return {'strategyType':'NikkeiRlDirection','effectiveParameters':{'ModelId':'twelve-training-control-seed22'},'universe':[{'productId':'225MC','contractMultiplier':10}]}, {'completedTrades':2,'grossPnl':200,'netPnl':56,'winRate':50,'averageWinningPoints':12.8,'averageLosingPoints':-7.2}
    def test_complete(self):
        m,s=self.fixture();r=summarize(m,s);self.assertEqual(r['expectedGrossPnlPerTradeJPY'],100);self.assertEqual(r['averageNetWinJPY'],128);self.assertFalse(r['qualified'])
    def test_earlier(self):
        m,s=self.fixture();m["effectiveParameters"]["ModelId"]="pruned-earlier-seed22"
        r=summarize(m,s);self.assertEqual(r["model"],"pruned-earlier-seed22")
    def test_empty(self):
        m,s=self.fixture();s['completedTrades']=0;self.assertIsNone(summarize(m,s)['expectedGrossPnlPerTradeJPY'])
    def test_wrong(self):
        m,s=self.fixture();m['strategyType']='other'
        with self.assertRaises(ValueError):summarize(m,s)
    def test_missing(self):
        m,s=self.fixture();del s['netPnl']
        with self.assertRaises(ValueError):summarize(m,s)
    def test_deterministic(self):
        m,s=self.fixture();self.assertEqual(summarize(m,s),summarize(m,s))
if __name__=='__main__':unittest.main()
