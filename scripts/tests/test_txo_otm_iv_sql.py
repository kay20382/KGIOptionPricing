from datetime import date
import unittest

from scripts.txo_otm_iv import IvResult
from scripts.calculate_txo_otm_iv import build_option_query, build_upsert_sql


def sample_result():
    return IvResult(
        contract_type="週三週選", expiry_month="202609W3", expiry_date=date(2026, 9, 23),
        central_strike=47_400.0, central_call_settlement=130.0,
        central_put_settlement=110.0, synthetic_forward=47_420.0,
        call_strike=47_450.0, call_settlement=100.0,
        put_strike=47_400.0, put_settlement=90.0,
        remaining_trading_days=1, call_iv=0.18, put_iv=0.19,
    )


class TxoOtmIvSqlTests(unittest.TestCase):
    def test_upsert_targets_only_trade_date_and_contract_type(self):
        sql = build_upsert_sql(date(2026, 9, 22), sample_result())
        self.assertIn("MERGE dbo.TAIFEX_AtmOTMIV", sql)
        self.assertIn("[交易日期]", sql)
        self.assertIn("[合約類型]", sql)
        self.assertIn("2026-09-22", sql)
        self.assertIn("週三週選", sql)

    def test_option_query_uses_required_regular_session_source_fields(self):
        sql = build_option_query(date(2026, 9, 22))
        for required in ("TXO", "一般交易時段", "[結算價]", "[履約價]", "[買賣權]", "[到期月份(週別)]", "[契約到期日]"):
            self.assertIn(required, sql)
        self.assertIn("2026-09-22", sql)
        self.assertIn("2026-09-23", sql)



    def test_cli_can_start_from_project_root(self):
        import subprocess
        import sys
        completed = subprocess.run(
            [sys.executable, "scripts/calculate_txo_otm_iv.py", "--help"],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)


if __name__ == "__main__":
    unittest.main()
