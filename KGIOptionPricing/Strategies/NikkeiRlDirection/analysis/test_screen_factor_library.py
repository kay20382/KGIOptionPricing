import hashlib,json,tempfile,unittest
from pathlib import Path
from screen_factor_library import screen

class FactorScreenBoundaryTests(unittest.TestCase):
    def test_validation_date_in_manifest_rejected_before_fitting(self):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder)/'labels.jsonl';p.write_text('')
            Path(str(p)+'.manifest.json').write_text(json.dumps(dict(labelSha256=hashlib.sha256(b'').hexdigest().upper(),
                 trainingEpisodes=['20260928-day','20260929-day','20260930-day','20261006-day'])))
            with self.assertRaisesRegex(ValueError,'Training-only'):screen(p,Path(folder)/'selection.json')

    def test_validation_row_in_label_stream_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder)/'labels.jsonl';raw=json.dumps(dict(episode='20261006-day',example={}))+'\n';p.write_text(raw)
            Path(str(p)+'.manifest.json').write_text(json.dumps(dict(labelSha256=hashlib.sha256(p.read_bytes()).hexdigest().upper(),
                 trainingEpisodes=['20260928-day','20260929-day','20260930-day','20261005-day'])))
            with self.assertRaisesRegex(ValueError,'Validation'):screen(p,Path(folder)/'selection.json')

if __name__=='__main__':unittest.main()
