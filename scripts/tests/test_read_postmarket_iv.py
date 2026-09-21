from datetime import date
import unittest

from scripts.txo_otm_iv import IvResult
from scripts.read_postmarket_iv import format_iv_csv_row, nearest_report_iv


class ReadPostmarketIvTests(unittest.TestCase):
    def test_uses_earliest_expiry_across_wednesday_friday_and_monthly_rows(self):
        rows = [
            IvResult("月選", "202610", date(2026, 10, 21)),
            IvResult("週五週選", "202609F4", date(2026, 9, 25)),
            IvResult("週三週選", "202609W4", date(2026, 9, 23)),
        ]
        self.assertEqual(nearest_report_iv(date(2026, 9, 22), rows).expiry_date, date(2026, 9, 23))

    def test_preserves_empty_call_iv_without_substitution(self):
        result = IvResult("週三週選", "202609W4", date(2026, 9, 23), call_iv=None, put_iv=0.184)
        row = format_iv_csv_row(result, 21.82)
        self.assertEqual(row["買權IV"], "")
        self.assertEqual(row["賣權IV"], 18.4)
        self.assertEqual(row["VIX"], 21.82)


if __name__ == "__main__":
    unittest.main()
