import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts/research/import_factor_library.py'
spec = importlib.util.spec_from_file_location('factor_import', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class FactorLibraryTests(unittest.TestCase):
    def tables(self):
        return {
            '因子目錄': [{'ID': '01', '因子名稱': '價差'}],
            '定義與資料': [{'ID': '01', '定義／公式（文字）': 'S=(a-b)/tick'}],
            '資料可算性': [{'ID': '01', '可算性分類': '可算'}],
            '用途與限制': [{'ID': '01', '限制與洩漏風險': 'past only'}],
            '中國因子補充': [{'ID': '51', '因子名稱': '新因子'}, {'ID': '條件1', '因子名稱': '需方向'}],
            '中國因子定義': [{'ID': '51', '公式／工程定義': 'sum(p*v)/sum(v)'}, {'ID': '條件1', '公式／工程定義': 'conditional'}],
        }

    def test_preserve_definition_and_proxy_separately(self):
        r = module.build_registry(self.tables(), [{'candidateId': 1, 'status': '既有特徵'}],
                                  [{'candidateId': 1, 'formula': 'different proxy'}], expected_ids={1, 51})
        self.assertEqual([x['candidateId'] for x in r['candidates']], [1, 51])
        self.assertEqual(r['candidates'][0]['sourceDefinition']['定義／公式（文字）'], 'S=(a-b)/tick')
        self.assertEqual(r['candidates'][0]['existingImplementation'][0]['formula'], 'different proxy')
        self.assertEqual(r['candidates'][1]['implementationStatus'], '未實作')
        self.assertFalse(r['candidates'][1]['modelInputEnabled'])
        self.assertEqual(len(r['conditionalCandidates']), 1)
        self.assertEqual(module.select_candidates(r, [51])[0]['candidateId'], 51)

    def test_duplicate_missing_and_unknown_ids_rejected(self):
        t = self.tables()
        t['因子目錄'].append(t['因子目錄'][0])
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            module.build_registry(t, [], [], expected_ids={1, 51})
        with self.assertRaisesRegex(ValueError, 'coverage'):
            module.build_registry(self.tables(), [], [], expected_ids={1, 2, 51})
        with self.assertRaisesRegex(ValueError, 'definition'):
            t = self.tables(); t['中國因子定義'] = []
            module.build_registry(t, [], [], expected_ids={1, 51})

    def test_screened_proxy_not_universally_rejected(self):
        r = module.build_registry(self.tables(), [], [], expected_ids={1, 51})
        r['candidates'][0]['screeningEvidence'] = [{'status': '未通過', 'variant': 'proxy'}]
        self.assertEqual(len(module.select_candidates(r, [1, 51])), 2)
        with self.assertRaisesRegex(ValueError, 'unknown'):
            module.select_candidates(r, [999])

    def test_attach_only_relevant_screening_variants(self):
        r = module.build_registry(self.tables(), [], [], expected_ids={1, 51})
        module.attach_screening(r, {'usage': 'TrainingOnlyDiagnostic', 'configurations': [
            {'candidates': '1+2', 'retained': False, 'meanImprovement': -0.1}]}, 'report.json', 'ABC')
        self.assertEqual(r['candidates'][0]['screeningEvidence'][0]['reportSha256'], 'ABC')
        self.assertEqual(r['candidates'][1]['screeningEvidence'], [])
        self.assertFalse(r['candidates'][0]['screeningEvidence'][0]['definitionParityVerified'])

    def test_new_engineering_screen_has_separate_implementation_status(self):
        r = module.build_registry(self.tables(), [], [], expected_ids={1, 51})
        module.attach_screening(r, {'schema':'nikkei-depth-vwap-screen-v1',
            'usage':'TrainingOnlyDiagnostic','configurations':[{'candidates':'51','retained':False}]}, 'new.json', 'SHA')
        self.assertIn('研究計算已實作',r['candidates'][1]['implementationStatus'])
        self.assertFalse(r['candidates'][1]['modelInputEnabled'])


if __name__ == '__main__':
    unittest.main()
