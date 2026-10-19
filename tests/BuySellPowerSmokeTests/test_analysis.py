import csv
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "KGIOptionPricing/Strategies/BuySellPower/analysis/analyze_buy_sell_power.py"

class AnalysisTests(unittest.TestCase):
    def test_incomplete_excluded_and_parameters_not_mixed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "行情段紀錄.csv"
            fields = ["Product", "Symbol", "SessionId", "Direction", "Points", "IsCompleted", "PowerWindowSeconds", "ShortAverageSeconds", "LongAverageSeconds", "ReversalPoints", "MovementAveragePeriods"]
            with source.open("w", encoding="utf-8-sig", newline="") as f:
                writer = csv.DictWriter(f, fieldnames=fields)
                writer.writeheader()
                for points, complete, reversal in [(12, "true", 5), (14, "true", 5), (999, "false", 5), (100, "true", 7)]:
                    writer.writerow(dict(zip(fields, ["225", "large", "2026-10-06/日盤", "上漲", points, complete, 10, 5, 10, reversal, 2])))
            result = subprocess.run(["python", str(SCRIPT), str(root), "--output", str(root / "report")], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            report = json.loads((root / "report.json").read_text(encoding="utf-8"))
            self.assertEqual(2, len(report["groups"]))
            group = next(g for g in report["groups"] if g["parameters"]["ReversalPoints"] == "5")
            self.assertEqual(2, group["up_samples"])
            self.assertEqual(13.0, group["average_up_points"])
            self.assertEqual(1, group["incomplete_count"])
            self.assertIsNone(group["average_down_points"])
            self.assertEqual(False, report["trading_logic_implemented"])

if __name__ == "__main__":
    unittest.main()
