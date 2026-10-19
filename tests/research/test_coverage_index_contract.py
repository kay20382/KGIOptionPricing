import json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts/research'))
from coverage_index_contract import snapshot_coverage, verify_coverage

class CoverageContractTests(unittest.TestCase):
    def test_flag_mutation_rejected_after_snapshot(self):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder);index=p/'segment-index.json'
            index.write_text(json.dumps(dict(date='20261002',fullDayCoverage=False)),encoding='utf-8')
            indexes,pins=snapshot_coverage({'20261002-segment01':str(p/'a.br')})
            self.assertFalse(indexes['20261002']['fullDayCoverage']);verify_coverage(pins)
            index.write_text(json.dumps(dict(date='20261002',fullDayCoverage=True)),encoding='utf-8')
            with self.assertRaises(ValueError):verify_coverage(pins)

    def test_disagreeing_segments_and_wrong_date_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder);a=p/'a';b=p/'b';a.mkdir();b.mkdir()
            for path,complete in [(a,True),(b,False)]:
                (path/'segment-index.json').write_text(json.dumps(dict(date='20261002',fullDayCoverage=complete)),encoding='utf-8')
            with self.assertRaises(ValueError):snapshot_coverage({'20261002-segment01':str(a/'a.br'),'20261002-segment03':str(b/'b.br')})
            with self.assertRaises(ValueError):snapshot_coverage({'20261006-segment01':str(a/'a.br')})

if __name__=='__main__':unittest.main()
