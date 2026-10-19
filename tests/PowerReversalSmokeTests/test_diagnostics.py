import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT=Path(__file__).resolve().parents[2]/'KGIOptionPricing/Strategies/PowerReversal/analysis/diagnose_power_reversal.py'
spec=importlib.util.spec_from_file_location('diagnostics',SCRIPT)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class DiagnosticsTests(unittest.TestCase):
    def test_counts_duration_and_dotnet_fraction_precision(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'strategy-decisions.jsonl'
            row=dict(strategyType='PowerReversal',timestamp='2026-10-06T21:24:40.6818700+08:00',primaryReasonCode='DATA_GATE',thresholds=[dict(name='Quantity',observedValue=20,passed=False)])
            second=dict(row,timestamp='2026-10-06T21:24:42.68187+08:00',primaryReasonCode='WAIT_SIGNAL',thresholds=[dict(name='Quantity',observedValue=50,passed=True)])
            path.write_text('\n'.join(map(json.dumps,[row,second])),encoding='utf-8')
            result=module.diagnose(path)
            self.assertEqual(result['rows'],2)
            self.assertEqual(result['gates']['Quantity']['failPercent'],50)
            self.assertEqual(result['reasonSeconds']['DATA_GATE'],2)
            self.assertEqual(result['status'],'DiagnosticOnly')
            path.write_text('\n'.join(map(json.dumps,[second,row])),encoding='utf-8')
            with self.assertRaises(ValueError):module.diagnose(path)

if __name__=='__main__':unittest.main()
