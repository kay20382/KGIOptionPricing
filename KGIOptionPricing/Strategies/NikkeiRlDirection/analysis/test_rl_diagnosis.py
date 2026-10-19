import unittest
from analyze_nikkei_rl_direction import edge_attribution, funnel_row, temporal_probes

class DiagnosisTests(unittest.TestCase):
    def test_negative_gross_is_not_only_a_fee_problem(self):
        row=edge_attribution([dict(grossPnlJPY=-50,costsJPY=72,netPnlJPY=-122)])
        self.assertEqual(row['grossPerTradeJPY'],-50);self.assertFalse(row['grossEdgePositive'])
        self.assertEqual(row['requiredAdditionalGrossPerTradeJPY'],122)
    def test_rejected_actions_are_not_entries(self):
        row=funnel_row(dict(mask=[True,True,True,False,False],proposedAction=1,
            next=dict(effectiveAction=0,fills=[],position=0,pendingEntry=False,overrideReason='INVALID_ACTION')),dict())
        self.assertEqual(row['eligibleFlatDecisions'],1);self.assertEqual(row['entryIntentions'],0)
    def test_temporal_probe_windows_integer_labels_use_int64(self):
        rows=[]
        for episode in ['20260928-day','20260929-day','20261005-day']:
            for action in range(3):
                obs=[float(action)]*40
                rows.append(dict(episode=episode,example=dict(observation=obs,action=action,proxyAction=action)))
        self.assertEqual(len(temporal_probes(rows)),12)

if __name__=='__main__':unittest.main()
