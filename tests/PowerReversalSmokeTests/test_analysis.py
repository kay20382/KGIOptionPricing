import copy
import gzip
import importlib.util
import json
import tempfile
import shutil
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("power_analysis", ROOT / "KGIOptionPricing/Strategies/PowerReversal/analysis/analyze_power_reversal.py")
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)


class AnalysisTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.folder = Path((ROOT / "docs/superpowers/evidence/power-reversal/latest-run.txt").read_text())
        cls.loaded = module.load_run(cls.folder)

    def test_insufficient_synthetic_deterministic(self):
        report = module.analyze([self.loaded]); self.assertEqual(report, module.analyze([self.loaded]))
        self.assertEqual("Incomplete", report["runs"][0]["status"]); self.assertIsNone(report["ranking"])
        self.assertEqual(1, report["runs"][0]["metrics"]["completedTrades"])

    def test_adaptive_parameters_and_risk_metrics(self):
        run=copy.deepcopy(self.loaded)
        run['summary'].update(maximumTradeLoss=172,maximumIntradayLoss=300,maximumDrawdown=350)
        metrics=module.analyze([run])['runs'][0]['metrics']
        self.assertEqual(172,metrics['maximumTradeLoss'])
        self.assertEqual(300,metrics['maximumIntradayLoss'])
        parameters=dict(run['manifest']['effectiveParameters'],AdaptiveExitMode='1',SignalMode='1')
        module.validate_parameters(parameters)
        for changes in [dict(SignalMode='0'),dict(AdaptiveMinStopTicks='20',AdaptiveMaxStopTicks='12'),dict(AdaptiveMaxStopTicks='1.5')]:
            with self.assertRaises(module.AnalysisError):module.validate_parameters(parameters|changes)

    def test_adaptive_policy_parameter_guards(self):
        p=dict(self.loaded['manifest']['effectiveParameters'],SignalMode='1',AdaptiveForceMode='1')
        module.validate_parameters(p)
        for changes in [dict(SignalMode='0'),dict(AdaptiveMinSamples='301',AdaptiveLookbackSeconds='300'),dict(AdaptiveHoldingMode='1',AdaptiveMinHoldingSeconds='301',MaxHoldingSeconds='300'),dict(AdaptiveAgreementMode='0.5')]:
            with self.assertRaises(module.AnalysisError):module.validate_parameters(p|changes)

    def test_missing_and_corrupt(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            with self.assertRaises(module.AnalysisError): module.load_run(folder)
            (folder / "run-manifest.json").write_text("{bad")
            with self.assertRaises(module.AnalysisError): module.load_run(folder)
            (folder / "bad.jsonl").write_text('{"x":NaN}')
            with self.assertRaises(module.AnalysisError): module.read(folder / "bad.jsonl", True)

    def test_indicator_exit_parameter_ranges_and_legacy_defaults(self):
        legacy=copy.deepcopy(self.loaded['manifest']['effectiveParameters'])
        module.validate_parameters(legacy)
        for key,value in [('PowerExitMode','3'),('PowerExitMode','0.5'),('TrailingExitMode','2'),('TrailingActivateRisk','0'),('TrailingDistanceRisk','4'),('ExitWeakPowerThreshold','101'),('EntryExecutionMode','2'),('WeightedMidMode','0.5')]:
            with self.subTest(key=key,value=value):
                with self.assertRaises(module.AnalysisError):module.validate_parameters(legacy|{key:value})

    def test_lossless_compressed_decisions_same_report(self):
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp)
            for name in ['run-manifest.json','run-summary.json','executions.csv','order-lifecycle.jsonl','parameter-experiments.jsonl']:
                shutil.copy2(self.folder/name,folder/name)
            original=(self.folder/'strategy-decisions.jsonl').read_bytes()
            (folder/'strategy-decisions.jsonl.gz').write_bytes(gzip.compress(original))
            self.assertEqual(module.analyze([self.loaded]),module.analyze([module.load_run(folder)]))
            (folder/'strategy-decisions.jsonl.gz').write_bytes(b'corrupt gzip')
            with self.assertRaises(module.AnalysisError):module.load_run(folder)

    def test_user_requested_performance_only_retention(self):
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp)
            for name in ['run-manifest.json','run-summary.json']:shutil.copy2(self.folder/name,folder/name)
            manifest=self.loaded['manifest'];summary=self.loaded['summary']
            marker=dict(mode='performance-only',userRequested=True,verifiedBeforeDeletion=True,parameterSha256=manifest['parameterSha256'],inputArchiveSha256=manifest['inputArchive']['sha256'],completedTrades=summary['completedTrades'],netPnl=summary['netPnl'])
            (folder/'retention-status.json').write_text(json.dumps(marker),encoding='utf-8')
            run=module.load_run(folder)
            self.assertIsNone(run['fills']);self.assertIsNone(run['decisions'])
            report=module.analyze([run])['runs'][0]
            self.assertEqual(summary['netPnl'],report['metrics']['netPnl'])
            self.assertTrue(any('逐筆紀錄已依使用者要求刪除' in x for x in report['limitations']))
            marker['netPnl']=summary['netPnl']+1
            (folder/'retention-status.json').write_text(json.dumps(marker),encoding='utf-8')
            with self.assertRaises(module.AnalysisError):module.load_run(folder)

    def test_direction_filter_diagnosis_uses_recorded_counts(self):
        run = copy.deepcopy(self.loaded)
        run['manifest']['effectiveParameters']['RequireOppositeMovement'] = '1'
        run['summary'].setdefault('secondaryReasonCounts', {})['MovementOpposesPower'] = 12
        result = module.analyze([run])['runs'][0]['logic']['movementFilter']
        self.assertTrue(result['required'])
        self.assertEqual(12, result['rejectedObservations'])

    def test_parameter_boundaries(self):
        for key in ("SignalMode", "StopTicks", "TargetTicks", "CooldownSeconds", "RequireOppositeMovement", "RequireAlignedMovement", "PriceRecoveryTicks"):
            fractional = self.loaded["manifest"]["effectiveParameters"].copy(); fractional[key] = "1.5"
            with self.assertRaises(module.AnalysisError): module.validate_parameters(fractional)
        flag = self.loaded["manifest"]["effectiveParameters"].copy(); flag["RequireShortAverageDirection"] = "0.5"
        with self.assertRaises(module.AnalysisError): module.validate_parameters(flag)
        values = self.loaded["manifest"]["effectiveParameters"].copy(); values["ReversalPoints"] = "0"
        with self.assertRaises(module.AnalysisError): module.validate_parameters(values)
        values["ReversalPoints"] = "30"; values["ShortAverageSeconds"] = "5"; values["LongAverageSeconds"] = "1"
        with self.assertRaises(module.AnalysisError): module.validate_parameters(values)
        flags=self.loaded['manifest']['effectiveParameters'].copy()
        flags.update(RequireOppositeMovement='1',RequireAlignedMovement='1')
        with self.assertRaises(module.AnalysisError):module.validate_parameters(flags)
        valid=self.loaded['manifest']['effectiveParameters'].copy()
        valid.update(SignalMode='1',RequireOppositeMovement='1',RequireAlignedMovement='0',PriceRecoveryTicks='2')
        module.validate_parameters(valid)
        valid['RequireOppositeMovement']='0'
        with self.assertRaises(module.AnalysisError):module.validate_parameters(valid)

    def test_aligned_filter_recorded_diagnosis(self):
        run=copy.deepcopy(self.loaded)
        run['manifest']['effectiveParameters']['RequireAlignedMovement']='1'
        run['summary'].setdefault('secondaryReasonCounts',{})['MovementAlignedPower']=17
        result=module.analyze([run])['runs'][0]['logic']['alignedMovementFilter']
        self.assertTrue(result['required'])
        self.assertEqual(17,result['rejectedObservations'])

    def test_recovery_filter_recorded_diagnosis(self):
        run=copy.deepcopy(self.loaded)
        run['manifest']['effectiveParameters']['PriceRecoveryTicks']='2'
        run['summary'].setdefault('secondaryReasonCounts',{})['PriceRecoveryTicks']=23
        result=module.analyze([run])['runs'][0]['logic']['priceRecoveryFilter']
        self.assertEqual(2,result['minimumTicks'])
        self.assertEqual(23,result['rejectedObservations'])

    def test_consensus_parameter_bounds(self):
        for key,high in [('MinJointPower',100),('MaxPowerDifference',200),('MinShortAveragePower',100)]:
            for value in [-1,high+1]:
                values=self.loaded['manifest']['effectiveParameters'].copy();values[key]=str(value)
                with self.assertRaises(module.AnalysisError):module.validate_parameters(values)
            values[key]=str(high);module.validate_parameters(values)

    def test_consensus_recorded_diagnosis(self):
        run=copy.deepcopy(self.loaded)
        run['manifest']['effectiveParameters'].update(MinJointPower='30',MaxPowerDifference='40',MinShortAveragePower='5')
        run['summary'].setdefault('secondaryReasonCounts',{}).update(JointBuyPower=12,PowerDifference=7,**{'225M.SustainedPower':9,'225MC.SustainedPower':11})
        result=module.analyze([run])['runs'][0]['logic']['consensusFilter']
        self.assertEqual(30,result['minimumJointPower'])
        self.assertEqual(40,result['maximumPowerDifference'])
        self.assertEqual(5,result['minimumShortAveragePower'])
        self.assertEqual({'JointBuyPower':12,'PowerDifference':7,'225M.SustainedPower':9,'225MC.SustainedPower':11},result['rejectedObservations'])

    def test_incomparable_costs_and_input(self):
        a = self.loaded["manifest"]; b = copy.deepcopy(a); self.assertTrue(module.comparable(a, b))
        b["commissionPerContract"] = 36; self.assertFalse(module.comparable(a, b))
        b = copy.deepcopy(a); b["inputArchive"]["sha256"] = "other"; self.assertFalse(module.comparable(a, b))

    def test_split_leakage(self):
        splits = [{"kind":"train","start":"2026-10-01T00:00:00+08:00","end":"2026-10-04T00:00:00+08:00"},
                  {"kind":"out-of-sample","start":"2026-10-03T00:00:00+08:00","end":"2026-10-06T00:00:00+08:00"}]
        with self.assertRaises(module.AnalysisError): module.validate_splits(splits)
        splits[1]["start"] = splits[0]["end"]; module.validate_splits(splits)

    def test_manifest_hash_and_type(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp); m = copy.deepcopy(self.loaded["manifest"]); m["strategyType"] = "other"
            (folder / "run-manifest.json").write_text(json.dumps(m))
            with self.assertRaises(module.AnalysisError): module.load_run(folder)
            m["strategyType"] = "PowerReversal"; m["parameterSha256"] = "wrong"
            (folder / "run-manifest.json").write_text(json.dumps(m))
            with self.assertRaises(module.AnalysisError): module.load_run(folder)


if __name__ == "__main__": unittest.main()
