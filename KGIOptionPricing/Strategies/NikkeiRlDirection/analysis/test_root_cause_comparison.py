import unittest
from root_cause_comparison import comparison_contract, compare_state, validate_parity

class ComparisonContractTests(unittest.TestCase):
    def info(self,n,matched=True):return dict(warmstartProvenance=dict(matchedTimestampsSha256='same',matchedOnly=matched,selectedSamples=n),environmentConfig=dict(cooldownSeconds=10,decisionLatencyMs=1,rewardCostWeight=1,observationSize=40))
    def test_paired_arm_cannot_change_observations_or_timestamps(self):
        a=self.info(1754);b=self.info(1754);c=self.info(8863,False);d=self.info(12226,False)
        comparison_contract({'P0':a,'E0':b,'E1':c,'E2':d})
        b['warmstartProvenance']['matchedTimestampsSha256']='different'
        with self.assertRaises(ValueError):comparison_contract({'P0':a,'E0':b,'E1':c,'E2':d})
    def test_status_and_numeric_feedback_identity(self):
        compare_state({'status':'CompleteEpisode','netPnl':28},{'status':'CompleteEpisode','netPnl':28.0})
        with self.assertRaises(ValueError):compare_state({'status':'CompleteEpisode'},{'status':'IncompleteDataGap'})
    def test_parity_evidence_bound_to_current_artifacts(self):
        row=dict(count=1200,allActionsMatch=True,maxProbabilityError=1e-7,modelSha256='model',manifestSha256='manifest',paritySha256='fixture')
        validate_parity(row,'model','manifest','fixture')
        with self.assertRaises(ValueError):validate_parity(row,'other-model','manifest','fixture')

if __name__=='__main__':unittest.main()
