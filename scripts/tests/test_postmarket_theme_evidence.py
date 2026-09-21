import unittest
from scripts.postmarket_theme_evidence import StockMove, Theme, aggregate_theme_breadth

class ThemeEvidenceTests(unittest.TestCase):
    def test_keeps_multiple_secondary_themes_but_counts_stock_once_per_first_theme(self):
        mapping={"2330":[Theme("半導體","晶圓代工"),Theme("半導體","先進製程")],"2454":[Theme("半導體","IC設計")]}
        stocks=[StockMove("2330","台積電",2.0,100),StockMove("2454","聯發科",1.0,50)]
        summary=aggregate_theme_breadth(stocks,mapping).summaries["半導體"]
        self.assertEqual(summary.advancers,2)
    def test_unmapped_stock_is_not_assigned_to_a_theme(self):
        result=aggregate_theme_breadth([StockMove("9999","未分類",1.0,1)],{})
        self.assertEqual(result.unmapped_codes,["9999"])

if __name__ == '__main__': unittest.main()
