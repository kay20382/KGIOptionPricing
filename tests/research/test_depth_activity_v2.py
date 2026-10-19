import sys,unittest
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts/research'))
import depth_activity_candidates as old
import depth_activity_candidates_v2 as new


class DepthActivityParity(unittest.TestCase):
    def test_optimized_validation_preserves_windows_and_gaps(self):
        events=[]
        for i in range(100):
            t=i*0.5
            events.append(dict(Time=t,Product='225MC',Kind='depth_delta',Tick=5,
                Bid=[100,95,90,85,80],Ask=[105,110,115,120,125],
                BidQty=[10+i%3,10,10,10,10],AskQty=[10+i%5,10,10,10,10]))
            if i%3==0: events.append(dict(Time=t,Product='225MC',Kind='trade',Last=100,TradeQty=1))
        events.append(dict(Time=65,Product='225MC',Kind='depth_delta',Tick=5,
            Bid=[100,95,90,85,80],Ask=[105,110,115,120,125],BidQty=[10]*5,AskQty=[10]*5))
        a,sa=old.align_depth_activity(events,list(range(3,70,3)))
        b,sb=new.align_depth_activity(events,list(range(3,70,3)))
        np.testing.assert_allclose(a,b,rtol=0,atol=0,equal_nan=True)
        np.testing.assert_allclose(sa.pop('legacyParityVectors'),sb.pop('legacyParityVectors'),rtol=0,atol=0,equal_nan=True)
        self.assertEqual(sa,sb)


if __name__=='__main__': unittest.main()
