import csv
import json
import tempfile
import unittest
from pathlib import Path

from analyze_periodic_queue_cycle import AnalysisError, run_analysis


class PeriodicQueueCycleAnalysisTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.runs = self.root / "runs"
        self.runs.mkdir()
        self._run("baseline", 120, 20, 10, "split-a")
        self._run("high-profit-high-drawdown", 500, 900, 12, "split-b")
        self._run("stable", 180, 30, 15, "split-c")
        self._experiment("baseline", "high-profit-high-drawdown", "split-b")
        self._experiment("baseline", "stable", "split-c")

    def tearDown(self):
        self.temp.cleanup()

    def _run(self, run_id, net, drawdown, trades, split):
        directory = self.runs / run_id
        directory.mkdir()
        manifest = {
            "runId": run_id, "strategyType": "PeriodicQueueCycle", "codeVersion": "v1",
            "parameterSha256": "parameter-hash", "inputArchive": {"sha256": "archive-hash"},
            "universe": [{"productId": "225MC", "symbol": "NK", "tickSize": 5, "contractMultiplier": 10}],
            "commissionPerContract": 36, "executionAssumptions": {"fill": "strict-cross"}
        }
        summary = {"completedTrades": trades, "netPnl": net, "maximumDrawdown": drawdown,
                   "dataQualityCounts": {}, "adverseSlippageStress": {"0": net, "1": net - 20, "2": net - 40}}
        decisions = [{"decision": "Accept", "primaryReasonCode": "SignalAccepted", "direction": "Up",
                      "features": {"averageObi": 0.4}, "thresholds": [], "dataQualityFlags": []}]
        lifecycle = [{"state": "Submitted", "clientOrderId": run_id + "-a"}, {"state": "Filled", "clientOrderId": run_id + "-a"}]
        (directory / "run-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
        (directory / "run-summary.json").write_text(json.dumps(summary), encoding="utf-8")
        (directory / "strategy-decisions.jsonl").write_text("\n".join(json.dumps(x) for x in decisions) + "\n", encoding="utf-8")
        (directory / "order-lifecycle.jsonl").write_text("\n".join(json.dumps(x) for x in lifecycle) + "\n", encoding="utf-8")
        with (directory / "executions.csv").open("w", newline="", encoding="utf-8") as output:
            writer = csv.DictWriter(output, fieldnames=["clientOrderId", "role", "price"])
            writer.writeheader(); writer.writerow({"clientOrderId": run_id + "-a", "role": "EntryLong", "price": "100"})

    def _experiment(self, baseline, candidate, split):
        record = {"experimentId": candidate, "baselineRunId": baseline, "candidateRunId": candidate,
                  "strategyType": "PeriodicQueueCycle", "codeVersion": "v1", "inputArchiveSha256": "archive-hash",
                  "dataSplitId": split, "productMapping": {"225MC": "NK"},
                  "executionAssumptions": {"fill": "strict-cross"}, "status": "Valid"}
        path = self.runs / baseline / "parameter-experiments.jsonl"
        with path.open("a", encoding="utf-8") as output:
            output.write(json.dumps(record) + "\n")

    def test_failed_guardrail_cannot_rank_first(self):
        report = run_analysis(self.runs, self.root / "output", mode="parameters")
        self.assertEqual(report["parameterRanking"][0]["experimentId"], "stable")
        self.assertNotEqual(report["parameterRanking"][0]["experimentId"], "high-profit-high-drawdown")
        rejected = next(x for x in report["rejectedCandidates"] if x["experimentId"] == "high-profit-high-drawdown")
        self.assertIn("MaximumDrawdown", rejected["reasonCodes"])

    def test_repeat_run_is_deterministic(self):
        first = run_analysis(self.runs, self.root / "one", mode="all")
        second = run_analysis(self.runs, self.root / "two", mode="all")
        self.assertEqual(first, second)

    def test_invalid_manifest_or_insufficient_candidate_fails_closed(self):
        manifest_path = self.runs / "stable" / "run-manifest.json"
        original = manifest_path.read_text(encoding="utf-8")
        manifest_path.write_text("{bad", encoding="utf-8")
        with self.assertRaises(AnalysisError):
            run_analysis(self.runs, self.root / "invalid", mode="all")
        manifest_path.write_text(original, encoding="utf-8")
        self._run("small", 20, 2, 2, "split-d")
        self._experiment("baseline", "small", "split-d")
        report = run_analysis(self.runs / "baseline", self.root / "small-out", mode="parameters")
        self.assertTrue(any(x["experimentId"] == "small" and "InsufficientCompletedTrades" in x["reasonCodes"]
                            for x in report["rejectedCandidates"]))

    def test_wrong_strategy_and_overlapping_split_are_not_accepted(self):
        manifest_path = self.runs / "stable" / "run-manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["strategyType"] = "OtherStrategy"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        with self.assertRaisesRegex(AnalysisError, "WrongStrategyType"):
            run_analysis(self.runs, self.root / "wrong-type", mode="logic")
        manifest["strategyType"] = "PeriodicQueueCycle"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

        self._run("third", 160, 25, 14, "split-c")
        self._experiment("baseline", "third", "split-c")
        report = run_analysis(self.runs, self.root / "overlap", mode="parameters")
        rejected = next(x for x in report["rejectedCandidates"] if x["experimentId"] == "third")
        self.assertIn("OverlappingDataSplit", rejected["reasonCodes"])


if __name__ == "__main__":
    unittest.main()
