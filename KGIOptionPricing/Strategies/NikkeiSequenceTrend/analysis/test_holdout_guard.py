import tempfile,unittest
from pathlib import Path
from holdout_guard import claim_holdout
class HoldoutTests(unittest.TestCase):
 def test_shared_exclusive_claim_survives_failure(self):
  with tempfile.TemporaryDirectory() as d:
   b=Path(d);p=claim_holdout(b,'20261009','225MC','run1',{'model':'a'})
   self.assertTrue(p.exists())
   with self.assertRaises(FileExistsError):claim_holdout(b,'20261009','225MC','run2',{'model':'b'})
   with self.assertRaises(FileExistsError):claim_holdout(b,'20261009','225MC','run1',{'model':'a'})
 def test_invalid_date_and_symbol_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   with self.assertRaises(ValueError):claim_holdout(Path(d),'../bad','225MC','run',{})
   with self.assertRaises(ValueError):claim_holdout(Path(d),'20261009','../bad','run',{})