import sys, unittest
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts/research'))
from book_cost_candidates import book_cost_factors, align_books, source_event_time


class BookCostTests(unittest.TestCase):
    def book(self, at=1, q=10):
        return dict(Kind='depth_delta',Time=at,Product='225MC',Tick=5,Dropped=0,
                    Bid=[100,95,90,85,80],Ask=[105,110,115,120,125],
                    BidQty=[q]*5,AskQty=[q]*5)

    def test_symmetric_slope_and_five_lot_round_trip(self):
        v=book_cost_factors(self.book())
        self.assertAlmostEqual(v[0],np.log1p(sum(np.arange(10,51,10)*(np.arange(5)+.5))/sum(np.arange(10,51,10)**2)))
        self.assertEqual(v[1],0)
        self.assertAlmostEqual(v[2],np.log(2))
        thin=self.book(q=1)
        self.assertAlmostEqual(book_cost_factors(thin)[2],np.log(6))

    def test_missing_or_invalid_book_is_not_zero_feature(self):
        for change in [dict(BidQty=[0]*5),dict(Ask=[100,110,115,120,125]),dict(AskQty=[-1,10,10,10,10]),dict(Bid=[100,101,90,85,80]),dict(Bid=[100,95])]:
            b=self.book(); b.update(change)
            self.assertIsNone(book_cost_factors(b))

    def test_exact_boundary_future_and_other_product_not_consumed(self):
        a=self.book(at=1); future=self.book(at=3,q=1)
        alt=self.book(at=2,q=1); alt['Product']='225M'
        values, stats=align_books(iter([a,alt,future]),[3,5])
        self.assertAlmostEqual(values[0,2],np.log(2))
        self.assertAlmostEqual(values[1,2],np.log(6))
        self.assertEqual(stats['decisions'],2)

    def test_stale_book_and_bad_source_rejected(self):
        values,_=align_books(iter([self.book(at=0)]),[3])
        self.assertTrue(np.isnan(values).all())
        with self.assertRaises(ValueError): align_books(iter([self.book(at=2),self.book(at=1)]),[3])
        b=self.book(); b['Dropped']=1
        with self.assertRaises(ValueError): align_books(iter([b]),[3])

    def test_source_session_scope_and_seventh_digit_match_day_cache(self):
        self.assertIsNone(source_event_time('2026-09-29T16:17:39.4471076+08:00','20260929'))
        self.assertIsNone(source_event_time('2026-09-30T08:00:00+08:00','20260929'))
        self.assertIsNone(source_event_time('2026-09-29T07:44:59.9999999+08:00','20260929'))
        self.assertLess(source_event_time('2026-09-29T08:00:02.9999999+08:00','20260929'),8*3600+3)
        self.assertEqual(source_event_time('2026-09-29T08:00:03+08:00','20260929'),8*3600+3)
        with self.assertRaises(ValueError):source_event_time('2026-09-29T08:00:00','20260929')


if __name__=='__main__': unittest.main()
