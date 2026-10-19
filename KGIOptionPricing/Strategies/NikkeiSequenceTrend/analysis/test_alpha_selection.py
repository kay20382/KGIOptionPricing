import unittest,hashlib,tempfile
from pathlib import Path
from unittest.mock import patch
from datetime import datetime
from fit_execution_alpha import coverage,eligible,screen_eligible,score_policy
from train_sequence_trend import totals
class SelectionTests(unittest.TestCase):
    def day(self,n=10,full=True,open_quantity=0):
        return dict(session='day',completedTrades=n,winningTrades=6,netPnl=400,winningNet=600,losingNet=-200,maximumIntradayLoss=100,openQuantity=open_quantity,fullSession=full,fullRiskReplay=True)
    def score(self,rows):return dict(daily=rows,metrics=totals(rows))
    def test_each_day_minimum_and_no_partial_or_open_position_pass(self):
        self.assertTrue(eligible(self.score([self.day(),self.day()]),2))
        self.assertFalse(eligible(self.score([self.day(9),self.day(11)]),2))
        self.assertFalse(eligible(self.score([self.day(),self.day(full=False)]),2))
        self.assertFalse(eligible(self.score([self.day(),self.day(open_quantity=1)]),2))
        self.assertFalse(eligible(self.score([self.day()]),2))
    def test_full_session_bounds_are_not_replaced_by_loose_span(self):
        def session(first,last,kind):return dict(id='2026-10-06-'+kind,first=datetime.fromisoformat(first).timestamp(),last=datetime.fromisoformat(last).timestamp())
        self.assertTrue(coverage(session('2026-10-06T07:45:00+08:00','2026-10-06T14:39:55+08:00','day')))
        self.assertFalse(coverage(session('2026-10-06T07:50:00+08:00','2026-10-06T14:39:55+08:00','day')))
        self.assertTrue(coverage(session('2026-10-06T16:00:00+08:00','2026-10-07T04:54:55+08:00','night')))
    def test_sampled_risk_can_screen_but_cannot_qualify(self):
        rows=[self.day(),self.day()]
        for row in rows:row['fullRiskReplay']=False
        self.assertTrue(screen_eligible(self.score(rows),2))
        self.assertFalse(eligible(self.score(rows),2))
    def test_full_risk_source_must_match_aggregation_sha(self):
        with tempfile.TemporaryDirectory() as temp:
            source=Path(temp)/'raw';source.write_bytes(b'original recorded events')
            stat=source.stat();sha=hashlib.sha256(source.read_bytes()).hexdigest()
            d=dict(id='2026-10-06-day',source=str(source),sourceBytes=stat.st_size,sourceMtimeNs=stat.st_mtime_ns,sourceSha256=sha)
            row=self.day();row['rawSourceSha256']='wrong'
            policy={'threshold':.5}
            with patch('fit_execution_alpha.replay',return_value=row):
                with self.assertRaisesRegex(ValueError,'SHA differs'):score_policy(Path(temp),{},[d],{},policy,full_risk=True)
                row['rawSourceSha256']=sha
                self.assertTrue(eligible(score_policy(Path(temp),{},[d],{},policy,full_risk=True),1))
if __name__=='__main__':unittest.main()