import unittest
from constrained_objective import assess_day, rank_candidates

class ConstrainedTests(unittest.TestCase):
    def test_research_leader_does_not_promote_unqualified_model(self):
        result=rank_candidates([self.row(gross=-100)],['20261006'])
        self.assertIsNone(result['selectedCandidate'])
        self.assertEqual(result['highestGrossExpectancyResearchCandidate']['expectedGrossPnlPerTradeJPY'],-2)
        self.assertFalse(result['highestGrossExpectancyResearchCandidate']['qualified'])

    def row(self, date='20261006', trades=50, wins=20, gross=2500, group='A', seed=11, full=True):
        return dict(date=date,group=group,seed=seed,trades=trades,wins=wins,
                    winRatePercent=100*wins/trades if trades else None,grossPnlJPY=gross,
                    netPnlJPY=gross-trades*72,fullDayCoverage=full,extraCostTicksPerSide=0)

    def test_inclusive_thresholds_and_gross_expectation(self):
        r=assess_day(self.row(),True)
        self.assertTrue(r['hardTargetPass'])
        self.assertEqual(r['expectedGrossPnlPerTradeJPY'],50)
        self.assertEqual(r['netPnlJPY'],-1100)

    def test_each_constraint_is_hard(self):
        for row in [self.row(trades=49),self.row(wins=19),self.row(gross=2499),self.row(full=False)]:
            self.assertFalse(assess_day(row,True)['hardTargetPass'])

    def test_zero_trades_undefined_and_not_eligible(self):
        r=assess_day(self.row(trades=0,wins=0,gross=0),True)
        self.assertIsNone(r['expectedGrossPnlPerTradeJPY'])
        self.assertFalse(r['hardTargetPass'])
        self.assertFalse(assess_day(self.row(),False)['hardTargetPass'])

    def test_all_days_required_and_sort_only_qualified(self):
        dates=['20261006','20261007']
        rows=[self.row(d,group=g,gross=p) for g,p in [('A',2500),('B',3000)] for d in dates]
        rows += [self.row(d,group='C',trades=49,wins=40,gross=9999) for d in dates]
        result=rank_candidates(rows,dates)
        self.assertEqual([r['group'] for r in result['qualifiedRanking']],['B','A'])
        self.assertFalse(next(r for r in result['candidates'] if r['group']=='C')['qualified'])
        self.assertFalse(rank_candidates([self.row()],dates)['candidates'][0]['qualified'])

    def test_one_bad_day_cannot_be_averaged_away(self):
        rows=[self.row('20261006',gross=10000),self.row('20261007',gross=-100)]
        self.assertEqual(rank_candidates(rows,['20261006','20261007'])['qualifiedRanking'],[])

    def test_duplicate_date_is_rejected(self):
        with self.assertRaises(ValueError):rank_candidates([self.row(),self.row()],['20261006'])

if __name__=='__main__':unittest.main()
