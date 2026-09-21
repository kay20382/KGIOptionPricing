"""Read report-facing TXO OTM IV rows from MSSQL without substituting values."""

from __future__ import annotations

import csv
import subprocess
from datetime import date
from pathlib import Path
from typing import Iterable

from scripts.txo_otm_iv import IvResult

SQLCMD = Path(r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE")
DATABASE = "CodexMarketData"
METHOD = "最近到期、價平合成期貨、最近價外 Call/Put、結算價、Black-76、r=0、q=0、剩餘交易日/252"


def nearest_report_iv(trade_date: date, rows: Iterable[IvResult]) -> IvResult | None:
    eligible = [row for row in rows if row.expiry_date is not None and row.expiry_date >= trade_date]
    return min(eligible, key=lambda row: (row.expiry_date, row.contract_type)) if eligible else None


def format_iv_csv_row(result: IvResult | None, official_vix: float) -> dict[str, object]:
    if result is None:
        return {
            "最近到期日": "", "取樣履約價": "", "買權IV": "", "買權IV日變化": "",
            "賣權IV": "", "賣權IV日變化": "", "VIX": official_vix, "VIX日變化": "",
            "隱波口徑": METHOD, "資料狀態": "最近到期IV資料未就緒",
            "資料來源": "MSSQL dbo.TAIFEX_AtmOTMIV",
        }
    return {
        "最近到期日": result.expiry_date.isoformat() if result.expiry_date else "",
        "取樣履約價": result.central_strike if result.central_strike is not None else "",
        "買權IV": round(result.call_iv * 100, 4) if result.call_iv is not None else "",
        "買權IV日變化": "",
        "賣權IV": round(result.put_iv * 100, 4) if result.put_iv is not None else "",
        "賣權IV日變化": "",
        "VIX": official_vix,
        "VIX日變化": "",
        "隱波口徑": METHOD,
        "資料狀態": "成功" if result.reason is None else f"拒算：{result.reason}",
        "資料來源": "MSSQL dbo.TAIFEX_AtmOTMIV",
    }


def _run_sql(query: str) -> str:
    completed = subprocess.run(
        [str(SQLCMD), "-S", ".\\SQLEXPRESS", "-E", "-C", "-d", DATABASE, "-W", "-s", "|", "-h", "-1", "-f", "65001", "-Q", query],
        check=True, capture_output=True, text=True, encoding="utf-8", errors="strict",
    )
    return completed.stdout


def load_iv_results(trade_date: date) -> list[IvResult]:
    next_date = date.fromordinal(trade_date.toordinal() + 1)
    query = f"""SET NOCOUNT ON;
SELECT [合約類型],[到期月份(週別)],CONVERT(char(10),[契約到期日],23),
 [中央價平履約價],[中央Call結算價],[中央Put結算價],[價平合成期貨],
 [Call價外履約價],[Call價外結算價],[Put價外履約價],[Put價外結算價],
 [剩餘交易日],[買權IV],[賣權IV],[拒算原因],[來源檔]
FROM dbo.TAIFEX_AtmOTMIV
WHERE [交易日期] >= '{trade_date.isoformat()}' AND [交易日期] < '{next_date.isoformat()}';"""
    results: list[IvResult] = []
    for raw in csv.reader((line for line in _run_sql(query).splitlines() if line.strip()), delimiter="|"):
        if len(raw) != 16:
            continue
        values = [value.strip() for value in raw]
        nullable_float = lambda value: float(value) if value not in {"", "NULL"} else None
        nullable_int = lambda value: int(value) if value not in {"", "NULL"} else None
        results.append(IvResult(
            values[0], values[1] or None, date.fromisoformat(values[2]) if values[2] else None,
            nullable_float(values[3]), nullable_float(values[4]), nullable_float(values[5]),
            nullable_float(values[6]), nullable_float(values[7]), nullable_float(values[8]),
            nullable_float(values[9]), nullable_float(values[10]), nullable_int(values[11]),
            nullable_float(values[12]), nullable_float(values[13]), values[14] or None, values[15] or None,
        ))
    return results


def load_nearest_report_iv(trade_date: date) -> IvResult | None:
    return nearest_report_iv(trade_date, load_iv_results(trade_date))
