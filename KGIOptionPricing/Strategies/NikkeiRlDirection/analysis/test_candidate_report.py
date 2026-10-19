import unittest
from analyze_candidate_features import group_comparison, decision_funnel

class ReportTests(unittest.TestCase):
    def test_signal_funnel_distinguishes_health_holding_and_intents(self):
        flat=[0.0]*60;flat[38]=1
        held=flat.copy();held[30]=1;held[38]=0
        rows=[dict(observation=flat,mask=[True,True,True,False,False],proposedAction=1,next=dict(effectiveAction=1,overrideReason='',fills=[dict(role=0)])),
            dict(observation=held,mask=[True,False,False,True,False],proposedAction=3,next=dict(effectiveAction=3,overrideReason='REVERSE_SIGNAL',fills=[dict(role=4)]))]
        result=decision_funnel(rows)
        self.assertEqual(result['decisions'],2)
        self.assertEqual(result['healthyDecisions'],1)
        self.assertEqual(result['eligibleFlatDecisions'],1)
        self.assertEqual(result['entryIntents'],1)
        self.assertEqual(result['entryFills'],1)
        self.assertEqual(result['exitRoleCounts'],{'4':1})
    def test_paired_seed_comparison_and_no_partial_target(self):
        rows=[dict(group=g,seed=s,date=d,extraCostTicksPerSide=0,targetEligible=e,initialTarget=False,advancedTarget=False,
            trades=10,wins=4,losses=6,grossPnlJPY=n+720,netPnlJPY=n,costsJPY=720,winningNetJPY=400,losingNetJPY=n-400,
            excludedOpenPositions=0,excludedEntryCostsJPY=0,excludedUnrealizedJPY=0,excludedOpenNetPnlJPY=0,rawNetPnlJPY=n)
            for g,n in [('baseline',-100),('book',-50)] for s in [11,22,33] for d,e in [('20261006',True),('20261007',True),('20261002',False)]]
        comparison,ranking=group_comparison(rows)
        book=[x for x in comparison if x['group']=='book']
        self.assertTrue(all(x['trades']==20 and x['netDeltaVsBaselineJPY']==100 for x in book))
        self.assertEqual(next(x for x in ranking if x['group']=='book')['improvedSeeds'],3)
        with self.assertRaises(ValueError):group_comparison([x for x in rows if not(x['group']=='baseline' and x['seed']==11)])

if __name__=='__main__':unittest.main()
