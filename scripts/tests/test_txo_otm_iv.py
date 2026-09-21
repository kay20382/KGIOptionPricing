from datetime import date
import unittest

from scripts.txo_otm_iv import (
    OptionRow,
    black76_price,
    calculate_contract_iv,
    remaining_trading_days,
)


def make_rows(volatility=0.24):
    trade_date = date(2026, 9, 22)
    expiry = date(2026, 9, 25)
    forward = 20_000.0
    years = 3 / 252
    rows = []
    for strike in (19_900.0, 20_000.0, 20_100.0):
        for kind in ("Call", "Put"):
            rows.append(
                OptionRow(
                    contract_type="週三週選",
                    expiry_month="202609W3",
                    expiry_date=expiry,
                    strike=strike,
                    kind=kind,
                    settlement=black76_price(kind, forward, strike, years, volatility),
                    oi=100,
                )
            )
    return rows


class TxoOtmIvTests(unittest.TestCase):
    def test_uses_strictly_otm_strikes_after_central_synthetic_future(self):
        result = calculate_contract_iv(make_rows(), date(2026, 9, 22), set(), {2026})
        self.assertEqual(result.call_strike, 20_100.0)
        self.assertEqual(result.put_strike, 19_900.0)

    def test_recovers_known_call_and_put_volatility(self):
        result = calculate_contract_iv(make_rows(0.24), date(2026, 9, 22), set(), {2026})
        self.assertAlmostEqual(result.call_iv, 0.24, places=6)
        self.assertAlmostEqual(result.put_iv, 0.24, places=6)

    def test_counts_expiry_day_but_excludes_weekend_and_csv_holiday(self):
        self.assertEqual(
            remaining_trading_days(date(2026, 9, 22), date(2026, 9, 25), {date(2026, 9, 24)}),
            2,
        )

    def test_rejects_year_missing_from_holiday_file(self):
        result = calculate_contract_iv(make_rows(), date(2027, 1, 2), set(), {2026})
        self.assertEqual(result.reason, "holiday_calendar_year_missing")


if __name__ == "__main__":
    unittest.main()
