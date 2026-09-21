"""MSSQL adapter for persisted TXO ATM-synthetic / OTM implied volatility."""

from __future__ import annotations

import csv
import sys
import subprocess
from datetime import date
from pathlib import Path

if __package__ in {None, ""}:
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from scripts.txo_otm_iv import IvResult, OptionRow, calculate_contract_iv, load_holiday_calendar, select_contract_groups

SQLCMD = Path(r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE")
DATABASE = "CodexMarketData"
IMPORT_DATASET = "TAIFEX TXO 一般交易時段行情"
METHOD = "最近到期、價平合成期貨、最近價外 Call/Put、結算價、Black-76、r=0、q=0、剩餘交易日/252"


def _sql_literal(value: object | None) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, date):
        return f"N'{value.isoformat()}'"
    if isinstance(value, str):
        return "N'" + value.replace("'", "''") + "'"
    return repr(value)


def _classify_contract(expiry_month: str) -> str | None:
    if len(expiry_month) == 8 and expiry_month[:6].isdigit() and expiry_month[6] == "W" and expiry_month[7].isdigit():
        return "週三週選"
    if len(expiry_month) == 8 and expiry_month[:6].isdigit() and expiry_month[6] == "F" and expiry_month[7].isdigit():
        return "週五週選"
    if len(expiry_month) == 6 and expiry_month.isdigit():
        return "月選"
    return None


def build_option_query(trade_date: date) -> str:
    next_date = date.fromordinal(trade_date.toordinal() + 1)
    return f"""
SET NOCOUNT ON;
SELECT CONVERT(char(10), [交易日期], 23), [到期月份(週別)],
       CONVERT(char(10), [契約到期日], 23), [履約價], [買賣權], [結算價],
       [未沖銷契約量], [_SourceFile]
FROM dbo.TAIFEX_OptionDaily
WHERE [交易日期] >= '{trade_date.isoformat()}'
  AND [交易日期] < '{next_date.isoformat()}'
  AND [契約] = N'TXO'
  AND [一般交易時段成交量] IS NOT NULL
ORDER BY [到期月份(週別)], [履約價], [買賣權];
"""


def build_import_ready_query(trade_date: date) -> str:
    next_date = date.fromordinal(trade_date.toordinal() + 1)
    return f"""SET NOCOUNT ON;
SELECT COUNT_BIG(*)
FROM dbo.ImportRuns
WHERE [TradingDate] >= '{trade_date.isoformat()}'
  AND [TradingDate] < '{next_date.isoformat()}'
  AND [DatasetName] = N'{IMPORT_DATASET}'
  AND [CompletedAt] IS NOT NULL;"""


def _run_sql(query: str) -> str:
    completed = subprocess.run(
        [str(SQLCMD), "-S", ".\\SQLEXPRESS", "-E", "-C", "-d", DATABASE, "-W", "-s", "|", "-h", "-1", "-f", "65001", "-Q", query],
        check=True, capture_output=True, text=True, encoding="utf-8", errors="strict",
    )
    return completed.stdout


def _parse_option_rows(output: str) -> list[OptionRow]:
    rows: list[OptionRow] = []
    for raw in csv.reader((line for line in output.splitlines() if line.strip()), delimiter="|"):
        if len(raw) != 8:
            continue
        _, expiry_month, expiry_text, strike, kind, settlement, oi, source_file = (part.strip() for part in raw)
        contract_type = _classify_contract(expiry_month)
        if contract_type is None or kind not in {"Call", "Put"}:
            continue
        rows.append(OptionRow(
            contract_type=contract_type,
            expiry_month=expiry_month,
            expiry_date=date.fromisoformat(expiry_text),
            strike=float(strike),
            kind=kind,
            settlement=float(settlement) if settlement not in {"", "NULL"} else None,
            oi=int(oi) if oi not in {"", "NULL"} else None,
            source_file=source_file or None,
        ))
    return rows


def load_option_rows(trade_date: date) -> list[OptionRow]:
    return _parse_option_rows(_run_sql(build_option_query(trade_date)))


def ensure_import_ready(trade_date: date) -> bool:
    value = _run_sql(build_import_ready_query(trade_date)).strip()
    return value.isdigit() and int(value) > 0


def build_upsert_sql(trade_date: date, result: IvResult) -> str:
    state = "成功" if result.reason is None else "拒算"
    values = [
        _sql_literal(trade_date), _sql_literal(result.contract_type), _sql_literal(result.expiry_month),
        _sql_literal(result.expiry_date), _sql_literal(result.central_strike),
        _sql_literal(result.central_call_settlement), _sql_literal(result.central_put_settlement),
        _sql_literal(result.synthetic_forward), _sql_literal(result.call_strike),
        _sql_literal(result.call_settlement), _sql_literal(result.put_strike),
        _sql_literal(result.put_settlement), _sql_literal(result.remaining_trading_days),
        _sql_literal(result.call_iv), _sql_literal(result.put_iv), _sql_literal(METHOD),
        _sql_literal(state), _sql_literal(result.reason), _sql_literal(result.source_file),
    ]
    columns = "[交易日期], [合約類型], [到期月份(週別)], [契約到期日], [中央價平履約價], [中央Call結算價], [中央Put結算價], [價平合成期貨], [Call價外履約價], [Call價外結算價], [Put價外履約價], [Put價外結算價], [剩餘交易日], [買權IV], [賣權IV], [計算口徑], [資料狀態], [拒算原因], [來源檔]"
    assignments = ", ".join(f"Target.{column} = Source.{column}" for column in columns.split(", ")[2:]) + ", Target.[寫入時間] = SYSDATETIME()"
    return f"""SET XACT_ABORT ON;
BEGIN TRAN;
MERGE dbo.TAIFEX_AtmOTMIV WITH (HOLDLOCK) AS Target
USING (VALUES ({', '.join(values)})) AS Source ({columns})
ON Target.[交易日期] = Source.[交易日期]
 AND Target.[合約類型] = Source.[合約類型]
WHEN MATCHED THEN UPDATE SET {assignments}
WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({', '.join('Source.' + column for column in columns.split(', '))});
COMMIT TRAN;"""


def run_for_date(trade_date: date, holiday_path: Path) -> list[IvResult]:
    if not ensure_import_ready(trade_date):
        raise RuntimeError("taifex_import_not_ready")
    holidays, covered_years = load_holiday_calendar(holiday_path)
    groups = select_contract_groups(load_option_rows(trade_date), trade_date)
    results = [calculate_contract_iv(group.rows, trade_date, holidays, covered_years) for group in groups.values()]
    for result in results:
        _run_sql(build_upsert_sql(trade_date, result))
    return results


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("trade_date", type=date.fromisoformat)
    parser.add_argument("--holiday-file", type=Path, default=Path("config/twse_taifex_trading_holidays.csv"))
    args = parser.parse_args()
    for calculated in run_for_date(args.trade_date, args.holiday_file):
        print(f"{calculated.contract_type}|{calculated.expiry_date}|{calculated.call_iv}|{calculated.put_iv}|{calculated.reason or '成功'}")



