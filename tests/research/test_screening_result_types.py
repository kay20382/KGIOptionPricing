import json,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts/research'))
from screening_result_types import gate_result


class ScreeningResultTypes(unittest.TestCase):
    def test_positive_but_below_mean_threshold_serializes_false(self):
        r=gate_result([.001,.002],.0001,[1,1])
        self.assertFalse(r['predictionPassed'])
        self.assertFalse(r['retained'])
        self.assertEqual(json.loads(json.dumps(r)),r)

    def test_coverage_and_predictive_gates_both_required(self):
        self.assertTrue(gate_result([.01,.02],.001,[.9,.95])['retained'])
        self.assertFalse(gate_result([.01,.02],.001,[.7,.95])['retained'])
        self.assertFalse(gate_result([-.01,.03],.001,[1,1])['retained'])


if __name__=='__main__': unittest.main()
