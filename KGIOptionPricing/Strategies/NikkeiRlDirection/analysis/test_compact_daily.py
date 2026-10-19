import unittest
from unittest.mock import patch
from compact_daily import compact_daily_rows,stop_contract,entry_contract,readiness_contract

class CompactTests(unittest.TestCase):
    def test_pruned_cache_legacy_schema_is_rejected(self):
        config=dict(researchReadinessProfile='exclude-flow',featureProfile='mini-micro-research',decisionIntervalSeconds=3)
        meta=dict(schema='nikkei-rl-research-pruned-flow-v1',observationSize=60,actorExcludedFeatures=list(range(48,54)),environmentConfig=config)
        with self.assertRaisesRegex(ValueError,'cache schema'):readiness_contract(meta,config,dict(config,schema='nikkei-rl-cache-v5-evaluation'))
    def test_readiness_requires_matching_schema_mask_and_cache(self):
        config=dict(researchReadinessProfile='exclude-flow',featureProfile='mini-micro-research',decisionIntervalSeconds=3)
        meta=dict(schema='nikkei-rl-research-pruned-flow-v1',observationSize=60,actorExcludedFeatures=list(range(48,54)),environmentConfig=config)
        cache=dict(config,schema='nikkei-rl-cache-pruned-flow-v1-evaluation')
        readiness_contract(meta,config,cache)
        for bad in [{},dict(config,researchReadinessProfile='unknown')]:
            with self.assertRaises(ValueError):readiness_contract(meta,config,bad)
        for bad in [dict(meta,schema='nikkei-rl-research-v1'),dict(meta,actorExcludedFeatures=list(range(48,53))),dict(meta,actorExcludedFeatures=list(range(48,54))+[53])]:
            with self.assertRaises(ValueError):readiness_contract(bad,config,cache)
        readiness_contract(dict(schema='nikkei-rl-research-v1',environmentConfig={}),{}, {})
    def test_entry_profile_is_explicit_and_cannot_change_cost_or_delay(self):
        self.assertEqual(entry_contract(None),dict(entryPriceMode='same-side'))
        self.assertEqual(entry_contract(dict(entryPriceMode='opposite-touch')),dict(entryPriceMode='opposite-touch'))
        for profile in [dict(entryPriceMode='market'),dict(entryPriceMode='opposite-touch',commission=0),dict(entryPriceMode='opposite-touch',decisionLatencyMs=0)]:
            with self.assertRaises(ValueError):entry_contract(profile)
    def test_trained_entry_mode_requires_matching_model_manifest(self):
        report=dict(summaryOnly=True,sharedLedgerReconciled=True,config=dict(entryPriceMode='opposite-touch'))
        with patch('compact_daily.read',side_effect=[report,{},dict(environmentConfig=dict(entryPriceMode='same-side'))]):
            with self.assertRaisesRegex(ValueError,'trained entry'):compact_daily_rows('R','D','P.onnx',{},'G',22,0,trained_entry_mode='opposite-touch')
    def test_explicit_smaller_stop_keeps_150_point_hard_cap(self):
        cfg=stop_contract(dict(stopMode='fixed',fixedStopPoints=20,maxStopPoints=150))
        self.assertEqual(cfg['fixedStopPoints'],20)
        self.assertEqual(cfg['maxStopPoints'],150)
        self.assertEqual(stop_contract(None)['fixedStopPoints'],150)
    def test_stop_sensitivity_cannot_change_execution_or_remove_cap(self):
        for profile in [dict(stopMode='fixed',fixedStopPoints=20,maxStopPoints=200),dict(stopMode='fixed',fixedStopPoints=7,maxStopPoints=150),
                        dict(stopMode='fixed',fixedStopPoints=20,maxStopPoints=150,commission=0)]:
            with self.assertRaises(ValueError):stop_contract(profile)
    def test_duplicate_episode_is_rejected(self):
        report=dict(summaryOnly=True,sharedLedgerReconciled=True,config=dict(decisionLatencyMs=1,decisionIntervalSeconds=3,
          cooldownSeconds=10,maxHoldingSeconds=0,featureProfile='mini-micro-research',observationSize=60,stopMode='fixed',
          fixedStopPoints=150,maxStopPoints=150,commission=36,tickSize=5,multiplier=10,extraCostTicksPerSide=0),
          modelSha256='HASH',manifestSha256='HASH',datasetSha256='HASH')
        ep=dict(id='20261006-day',date='20261006',path='cache',cacheSha256='HASH',sharedLedgerReconciled=True,
          trades=1,wins=1,losses=0,breakeven=0,grossPnlJPY=100,netPnlJPY=28,costsJPY=72,
          winningNetJPY=28,losingNetJPY=0,excludedOpenPositions=0,excludedEntryCostsJPY=0,
          excludedUnrealizedJPY=0,excludedOpenNetPnlJPY=0,rawNetPnlJPY=28)
        report['results']=[ep,ep]
        with patch('compact_daily.read',side_effect=[report,{'20261006-day':'cache'}]),patch('compact_daily.sha',return_value='HASH'):
            with self.assertRaisesRegex(ValueError,'episodes'):compact_daily_rows('report','dataset','policy.onnx',{'20261006':dict(fullDayCoverage=True)},'test',22,0)

if __name__=='__main__':unittest.main()
