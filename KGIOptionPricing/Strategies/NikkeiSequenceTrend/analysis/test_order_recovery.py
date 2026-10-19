import unittest, tempfile, json
from pathlib import Path
from datetime import datetime,timedelta
import numpy as np
from sequence_dataset import aggregate_source,build_samples

class OrderRecoveryTests(unittest.TestCase):
    def source(self,path,defect=False,big=False):
        start=datetime.fromisoformat('2026-09-15T07:45:00+08:00');seq=0;rows=[]
        for i in range(330):
            stamp=start+timedelta(seconds=i*5-.5)
            for product in ('225MC','225M'):
                seq+=1
                rows.append(dict(Kind='depth',Time=stamp.isoformat(),Product=product,Sequence=seq,Dropped=0,Bid=[1000,995,990,985,980],Ask=[1005,1010,1015,1020,1025],BidQty=[10]*5,AskQty=[10]*5))
            seq+=1;rows.append(dict(Kind='trade',Time=stamp.isoformat(),Product='225MC',Sequence=seq,Dropped=0,Last=1005,TradeQty=1,Symbol='MC'))
            if defect and i==150:
                bad=dict(rows[-1]);bad['Time']=(stamp-timedelta(seconds=3 if big else .001)).isoformat();bad['Sequence']=seq-1;rows.append(bad)
        path.write_text('\n'.join(json.dumps(r) for r in rows)+'\n')
    def test_strict_mode_still_rejects_order_defect(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'raw';self.source(p,True)
            with self.assertRaises(ValueError):aggregate_source(p,Path(d)/'cache')
    def test_quarantine_keeps_future_and_excludes_affected_windows(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'raw';self.source(p,True)
            m=aggregate_source(p,Path(d)/'cache',ordering_policy='quarantine',features_v2=True)
            self.assertEqual(m['ordering'],1);self.assertEqual(m['orderDefects'][0]['line'],454)
            with np.load(m['sessions'][0]['cache']) as data:
                samples=build_samples(data['x'],data['mid'],data['times'],data['valid'])
                health=data['valid'].copy()
                self.assertEqual(data['x'].shape[1],45)
                self.assertEqual(data['x'][140,39],1)
            self.assertIn(140,samples.inference_indices)
            self.assertFalse(any(151<=i<=270 for i in samples.inference_indices))
            self.assertIn(310,samples.inference_indices)
            self.assertFalse(health[151])
    def test_large_clock_jump_is_not_recovered(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'raw';self.source(p,True,True)
            with self.assertRaises(ValueError):aggregate_source(p,Path(d)/'cache',ordering_policy='quarantine',features_v2=True)

    def test_zero_quantity_cannot_create_a_paper_fill_event(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'raw';self.source(p)
            rows=[json.loads(line) for line in p.read_text().splitlines()]
            real=False
            for row in rows:
                if row['Kind']=='trade':
                    if not real and row['Time']>'2026-09-15T07:45:00':real=True
                    else:row['TradeQty']=0
            p.write_text('\n'.join(json.dumps(row) for row in rows)+'\n')
            m=aggregate_source(p,Path(d)/'cache')
            with np.load(m['sessions'][0]['cache']) as data:
                self.assertEqual(len(data['trades']),1)
    def test_future_gap_cannot_rewrite_an_already_due_frame(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'raw';self.source(p)
            baseline=aggregate_source(p,Path(d)/'baseline',features_v2=True)
            rows=[json.loads(line) for line in p.read_text().splitlines()]
            start=datetime.fromisoformat('2026-09-15T07:45:00+08:00')
            rows=[r for r in rows if not start+timedelta(seconds=649)<=datetime.fromisoformat(r['Time'])<start+timedelta(seconds=674)]
            p.write_text('\n'.join(json.dumps(r) for r in rows)+'\n')
            gap=aggregate_source(p,Path(d)/'gap',features_v2=True)
            with np.load(baseline['sessions'][0]['cache']) as a,np.load(gap['sessions'][0]['cache']) as b:
                i=np.where(a['times']==start.timestamp()+645)[0][0]
                self.assertTrue(a['valid'][i]);self.assertTrue(b['valid'][i])
                np.testing.assert_array_equal(a['x'][i],b['x'][i])
if __name__=='__main__':unittest.main()