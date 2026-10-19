import copy
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from analyze_trade_flow_trend import AnalysisError, validate_manifest, run_analysis, rank_runs, diagnose


def manifest():
    params = {"Quantity": "1", "FastSeconds": "30", "SlowSeconds": "120", "StopTicks": "6", "ProfitTicks": "50", "SarStart": "0.02", "SarStep": "0.02", "SarMax": "0.20"}
    normalized = json.dumps(params, sort_keys=True, separators=(",", ":"))
    return dict(strategyType="TradeFlowTrend", runId="base", effectiveParameters=params, normalizedParameters=normalized,
                parameterSha256=hashlib.sha256(normalized.encode()).hexdigest().upper(),
                inputArchive={"sha256": "A" * 64}, universe=[{"productId": "225MC", "tickSize": 5, "contractMultiplier": 10}],
                commissionPerContract=36, executionAssumptions={"fillModel": "synthetic strict cross"})


def fixture(folder):
    folder.mkdir(parents=True)
    (folder / "run-manifest.json").write_text(json.dumps(manifest()), encoding="utf-8")
    (folder / "run-summary.json").write_text(json.dumps({"completedTrades": 0, "netPnl": None}), encoding="utf-8")
    for name in ("strategy-decisions.jsonl", "order-lifecycle.jsonl", "parameter-experiments.jsonl"):
        (folder / name).write_text("", encoding="utf-8")
    (folder / "executions.csv").write_text("timestamp,instanceId,price,quantity,commission\n", encoding="utf-8")


class ContractTests(unittest.TestCase):
    def eligible(self, name, archive="A"):
        m = manifest()
        m.update(runId=name, inputArchive={"sha256": archive * 64})
        splits = [{"kind": k, "start": f"2026-09-0{i}T00:00:00+08:00", "end": f"2026-09-0{i+1}T00:00:00+08:00"}
                  for i, k in enumerate(("train", "validation", "test"), 1)]
        return dict(manifest=m, summary=dict(netPnl=100, completedTrades=20, maximumDrawdown=10,
                    adverseSlippageStress={"2": 50}, dataQualityEvaluations=100, dataQualityCounts={}),
                    decisions=[], orders=[], fills=[], experiments=[dict(candidateRunId=name, baselineRunId=name,
                    primaryObjective="NetPnl", status="Valid", dataSplits=splits,
                    guardrailValues=dict(minimumCompletedTrades=10, maximumDrawdown=20, minimumStress2=0),
                    splitResults={k: {"completedTrades": 5} for k in ("train", "validation", "test")})])

    def test_unrelated_groups_never_share_ranking(self):
        rankings, _ = rank_runs([self.eligible("a"), self.eligible("b", "B")])
        self.assertEqual(rankings, [])

    def test_quality_evidence_required(self):
        a, b = self.eligible("a"), self.eligible("b")
        del b["summary"]["dataQualityEvaluations"]
        rankings, excluded = rank_runs([a, b])
        self.assertEqual(rankings, [])
        self.assertTrue(any("品質" in reason for row in excluded for reason in row["reasons"]))

    def test_quality_guardrail_overrides_profit(self):
        a, b, bad = self.eligible("a"), self.eligible("b"), self.eligible("bad")
        bad["summary"].update(netPnl=100000, dataQualityCounts={"UnknownTradeClassification": 1})
        rankings, _ = rank_runs([a, b, bad])
        self.assertEqual([r["runId"] for r in rankings], ["a", "b"])

    def test_dotnet_timestamp_diagnosis(self):
        run = self.eligible("a")
        run["decisions"] = [dict(kind="FeatureSample", timestamp="2026-09-21T08:00:00.1234567+08:00")]
        self.assertIn("日盤", diagnose(run)["sessionReasons"])

    def test_old_ranking_removed_when_current_input_insufficient(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            fixture(root / "run")
            (root / "out").mkdir()
            stale = root / "out" / "parameter-ranking.csv"
            stale.write_text("previous ranking")
            run_analysis(root / "run", root / "out")
            self.assertFalse(stale.exists())

    def test_hash_and_bounds(self):
        m = manifest()
        validate_manifest(m)
        m["effectiveParameters"]["Quantity"] = "2"
        with self.assertRaises(AnalysisError):
            validate_manifest(m)
        m = manifest()
        m["parameterSha256"] = "bad"
        with self.assertRaises(AnalysisError):
            validate_manifest(m)

    def test_missing_input_never_fakes_report(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(AnalysisError):
                run_analysis(Path(tmp) / "missing", Path(tmp) / "out")

    def test_deterministic_insufficient_and_read_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            fixture(root / "runs" / "base")
            original = {p.name: p.read_bytes() for p in (root / "runs" / "base").iterdir()}
            a = run_analysis(root / "runs", root / "out1")
            b = run_analysis(root / "runs", root / "out2")
            self.assertEqual(a, b)
            self.assertEqual(a["rankings"], [])
            self.assertTrue(a["limitations"])
            self.assertFalse((root / "out1" / "parameter-ranking.csv").exists())
            self.assertEqual(original, {p.name: p.read_bytes() for p in (root / "runs" / "base").iterdir()})
            (root / "runs" / "base" / "strategy-decisions.jsonl").write_text("{broken", encoding="utf-8")
            with self.assertRaises(AnalysisError):
                run_analysis(root / "runs", root / "bad")

    def test_overlapping_split_rejected(self):
        from analyze_trade_flow_trend import validate_splits
        with self.assertRaises(AnalysisError):
            validate_splits([{"kind": "train", "start": "2026-09-01T00:00:00+08:00", "end": "2026-09-10T00:00:00+08:00"},
                             {"kind": "test", "start": "2026-09-05T00:00:00+08:00", "end": "2026-09-11T00:00:00+08:00"}])

if __name__ == "__main__":
    unittest.main()
