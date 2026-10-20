import unittest
from analyze_main_strategy import summarize
class GridTests(unittest.TestCase):
 def test_grid_models(self):
  for model in ['grid15-positive-net','grid15-q90-research']:
   with self.subTest(model=model):
    m={'strategyType':'NikkeiRlDirection','effectiveParameters':{'ModelId':model},'universe':[{'productId':'225MC','contractMultiplier':10}]}
    s={'completedTrades':1,'grossPnl':100,'netPnl':28,'winRate':100}
    self.assertEqual(summarize(m,s)['model'],model)
if __name__=='__main__':unittest.main()
