import hashlib,json,tempfile,unittest
from pathlib import Path
import analyze_multifactor_mean_r17 as a

class AnalysisTests(unittest.TestCase):
    def parameters(self):return json.loads((Path(__file__).resolve().parents[1]/'profiles/round17-selected.json').read_text(encoding='utf-8'))
    def test_stop_limit_and_thresholds(self):
        p=self.parameters();a.validate_parameters(p)
        for changes in ({'ResearchVolatilityMaxStopTicks':'31'},{'ResearchVolatilityMaxStopTicks':'1.5'},{'ResearchScoreThreshold':'0'},{'ResearchFactorMode':'9'},{'ResearchVolatilityMinStopTicks':'30','ResearchVolatilityMaxStopTicks':'20'}):
            with self.assertRaises(a.AnalysisError):a.validate_parameters(dict(p,**changes))
    def fixture(self,folder):
        p=self.parameters();normalized=json.dumps(p,sort_keys=True,separators=(',',':'))
        manifest=dict(strategyType='MultiFactorMeanR17',schemaVersion=1,runId='synthetic-r17',effectiveParameters=p,normalizedParameters=normalized,parameterSha256=hashlib.sha256(normalized.encode()).hexdigest().upper(),commissionPerContract=36,inputArchive=dict(sha256='synthetic-input'),executionAssumptions=dict(synthetic='true'))
        (folder/'run-manifest.json').write_text(json.dumps(manifest),encoding='utf-8')
        (folder/'run-summary.json').write_text(json.dumps(dict(completedTrades=1,netPnl=28,primaryReasonCounts={'WAIT_FACTOR':1})),encoding='utf-8')
        for f in ('strategy-decisions.jsonl','order-lifecycle.jsonl','parameter-experiments.jsonl'):(folder/f).write_text('',encoding='utf-8')
        (folder/'executions.csv').write_text('timestamp,instanceId,role,price,quantity,commission\n',encoding='utf-8')
        return manifest
    def test_shared_manifest_and_determinism(self):
        with tempfile.TemporaryDirectory() as d:
            folder=Path(d);self.fixture(folder);run=a.load_run(folder)
            x=a.analyze([run]);self.assertEqual(x,a.analyze([run]));self.assertEqual(x['strategyType'],'MultiFactorMeanR17');self.assertEqual(x['runs'][0]['status'],'Incomplete');self.assertIsNone(x['ranking'])
    def test_missing_corrupt_wrong_strategy_and_hash(self):
        with tempfile.TemporaryDirectory() as d:
            folder=Path(d)
            with self.assertRaises(a.AnalysisError):a.load_run(folder)
            manifest=self.fixture(folder)
            for m in (dict(manifest,strategyType='PowerReversal'),dict(manifest,parameterSha256='bad')):
                (folder/'run-manifest.json').write_text(json.dumps(m),encoding='utf-8')
                with self.assertRaises(a.AnalysisError):a.load_run(folder)
            (folder/'run-manifest.json').write_text('{bad',encoding='utf-8')
            with self.assertRaises(a.AnalysisError):a.load_run(folder)
    def test_split_leakage_rejected(self):
        with self.assertRaises(a.AnalysisError):a.base.validate_splits([dict(kind='train',start='2026-09-28T08:00:00+08:00',end='2026-09-28T10:00:00+08:00'),dict(kind='out-of-sample',start='2026-09-28T09:00:00+08:00',end='2026-09-28T11:00:00+08:00')])

if __name__=='__main__':unittest.main()
