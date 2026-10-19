"""Contract tests for the per-strategy research entrypoint."""

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("analyze_mean_reversion_scalper.py")


class AnalysisTests(unittest.TestCase):
    def make_run(self, folder):
        archive = "20260924_nikkei_fixture.jsonl"
        run = folder / "20260924_nikkei_fixture" / "MR-R8"
        run.mkdir(parents=True)
        parameters = {"HorizonSeconds": "600", "F": "0.6", "K": "1.2",
                      "MinimumRangeTicks": "8"}
        normalized = json.dumps(parameters, separators=(",", ":"))
        manifest = {
            "runId": "fixture-MR-R8", "strategyType": "MeanReversionScalper",
            "instanceId": "MR-R8", "mode": "HistoricalReplay",
            "normalizedParameters": normalized,
            "parameterSha256": hashlib.sha256(normalized.encode()).hexdigest().upper(),
            "effectiveParameters": parameters, "commissionPerContract": 36,
            "universe": [{"productId": "225MC", "tickSize": 5,
                          "contractMultiplier": 10}],
            "inputArchive": {"absolutePath": "fixture", "sizeBytes": 100,
                             "lastWriteTimeUtc": "2026-09-24T00:00:00Z"},
            "executionAssumptions": {"fillModel": "strict-cross"},
        }
        (run / "run-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
        (run / "run-summary.json").write_text(json.dumps({"candidateSignals": 1,
            "acceptedSignals": 1, "submittedOrders": 2, "fills": 2,
            "primaryReasonCounts": {"EntryBracket": 1}}), encoding="utf-8")
        (folder / "daily-summary.json").write_text(json.dumps([{
            "File": archive, "MinRTicks": 8, "Status": "complete", "ClosedTradeNetYen": -72,
            "OpenQuantity": 0, "CompletedTrades": 1,
            "FirstEvent": "2026-09-24T08:00:00+08:00",
            "LastEvent": "2026-09-24T14:45:00+08:00"}]), encoding="utf-8")
        (folder / "trades.json").write_text(json.dumps([{
            "File": archive, "MinRTicks": 8, "CycleId": "cycle-1", "NetYen": -72,
            "Regime": "day|range|lowvol", "ExitRole": "StopLoss",
            "EntryTime": "2026-09-24T08:00:00+08:00",
            "ExitTime": "2026-09-24T08:00:10+08:00",
            "EntryPrice": 64000, "ExitPrice": 64000}]), encoding="utf-8")
        return manifest, run

    def call(self, folder, output, mode="all"):
        return subprocess.run([sys.executable, str(SCRIPT), "--runs", str(folder),
                               "--output", str(output), "--mode", mode],
                              text=True, capture_output=True, check=False)

    def test_repeatable_outputs_and_no_input_mutation(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp) / "runs"
            folder.mkdir()
            _, run = self.make_run(folder)
            before = (run / "run-manifest.json").read_bytes()
            first = Path(temp) / "first"
            second = Path(temp) / "second"
            self.assertEqual(self.call(folder, first).returncode, 0)
            self.assertEqual(self.call(folder, second).returncode, 0)
            for name in ("analysis-report.md", "analysis-report.json", "parameter-ranking.csv"):
                self.assertEqual((first / name).read_bytes(), (second / name).read_bytes())
            self.assertEqual(before, (run / "run-manifest.json").read_bytes())
            self.assertFalse((first / "parameter-ranking.csv").read_text(encoding="utf-8-sig").splitlines()[1].split(",")[4])
            logic = Path(temp) / "logic"
            parameters = Path(temp) / "parameters"
            self.assertEqual(self.call(folder, logic, "logic").returncode, 0)
            self.assertEqual(self.call(folder, parameters, "parameters").returncode, 0)
            self.assertFalse((logic / "parameter-ranking.csv").exists())
            self.assertNotIn("| 最小 R | 完整交易", (logic / "analysis-report.md").read_text(encoding="utf-8"))
            self.assertNotIn("| 最小 R | 盤勢", (parameters / "analysis-report.md").read_text(encoding="utf-8"))

    def test_invalid_metadata_stops_without_ranking(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp) / "runs"
            folder.mkdir()
            manifest, run = self.make_run(folder)
            manifest["commissionPerContract"] = 0
            (run / "run-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            output = Path(temp) / "out"
            result = self.call(folder, output)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("商品規格／費用不符", result.stderr)
            self.assertFalse(output.exists())

    def test_missing_run_file_is_explicit(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp) / "runs"
            folder.mkdir()
            _, run = self.make_run(folder)
            (run / "run-summary.json").unlink()
            result = self.call(folder, Path(temp) / "out")
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("缺少執行資訊", result.stderr)


if __name__ == "__main__":
    unittest.main()
