import sys,unittest,math
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts/research'))
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'KGIOptionPricing/Strategies/NikkeiRlDirection/analysis'))
from minute_candidate_alignment import align_minute_candidates

class MinuteAlignmentTests(unittest.TestCase):
    def test_smart_money_keeps_twenty_closed_returns_and_excludes_boundary(self):
        events=[dict(Time=float(t),Product='225MC',Kind='trade',Last=100,TradeQty=1,TotalQty=t+1,Dropped=0) for t in range(1261)]
        z,stats=align_minute_candidates(events,[1260],include_smart_money=True)
        self.assertEqual(z.shape,(1,1));self.assertEqual(z[0,0],0.)
        self.assertEqual(stats['closedMinutes'],21)
        events[1260]['Last']=10000
        other,_=align_minute_candidates(events,[1260],include_smart_money=True)
        np.testing.assert_allclose(z,other)
    def events(self):
        return [dict(Time=float(t),Product='225MC',Kind='trade',Last=100,TradeQty=(t//60+1),TotalQty=None,Dropped=0) for t in range(0,360)]
    def test_complete_minutes_use_volume_and_amount(self):
        z,stats=align_minute_candidates(self.events(),[300])
        weights=np.arange(1,6)/15
        self.assertAlmostEqual(z[0,0],-sum(weights*np.log(weights)))
        self.assertAlmostEqual(z[0,1],math.log(5))
        self.assertAlmostEqual(z[0,2],1)
        self.assertEqual(stats['invalidTrades'],0)
    def test_boundary_future_trade_cannot_change_closed_bars(self):
        events=self.events();z,_=align_minute_candidates(events,[300])
        events[300]=dict(events[300],Last=100000,TradeQty=100000)
        events[320]=dict(events[320],Last=50000,TradeQty=100000)
        other,_=align_minute_candidates(events,[300])
        np.testing.assert_allclose(z,other)
    def test_gap_and_initial_partial_minute_are_missing(self):
        events=[e for e in self.events() if not 120<=e['Time']<=150]
        z,stats=align_minute_candidates(events,[300]);self.assertTrue(np.isnan(z).all());self.assertGreater(stats['receiveGapResets'],0)
        z,_=align_minute_candidates(self.events()[1:],[300]);self.assertTrue(np.isnan(z).all())
    def test_order_drop_and_invalid_trade(self):
        with self.assertRaises(ValueError):align_minute_candidates(list(reversed(self.events())),[300])
        events=self.events();events[20]['Dropped']=1
        with self.assertRaises(ValueError):align_minute_candidates(events,[300])
        events=self.events();events[20]['TradeQty']=-1
        z,stats=align_minute_candidates(events,[300]);self.assertTrue(np.isnan(z).all());self.assertEqual(stats['invalidTrades'],1)
    def test_stale_source_end_cannot_complete_bar(self):
        z,_=align_minute_candidates(self.events()[:280],[300]);self.assertTrue(np.isnan(z).all())
    def test_price_factors_use_actual_minute_ohlc_and_true_amount(self):
        events=self.events()
        for event in events:event['Last']=100+2*(int(event['Time'])//60)+.2*(int(event['Time'])%60)
        z,_=align_minute_candidates(events,[300],include_prices=True)
        amounts=[sum((100+2*i+.2*j)*(i+1) for j in range(60)) for i in range(5)]
        self.assertAlmostEqual(z[0,0],sum(11.8/(10*a) for a in amounts))
        closes=[111.8+2*i for i in range(5)];volumes=[60*(i+1) for i in range(5)]
        expected=sum(c*v for c,v in zip(closes,volumes))/sum(volumes)/(sum(closes)/5)-1
        self.assertAlmostEqual(z[0,1],expected)
        events[300]['Last']=999999
        other,_=align_minute_candidates(events,[300],include_prices=True)
        np.testing.assert_allclose(z,other)
if __name__=='__main__':unittest.main()
