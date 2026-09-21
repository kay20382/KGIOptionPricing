"""TXO 日盤結算價的價平合成期貨與嚴格價外 IV 計算。"""

from __future__ import annotations

import csv
import math
from dataclasses import dataclass
from datetime import date, timedelta
from pathlib import Path
from statistics import NormalDist
from typing import Iterable

_NORMAL = NormalDist()


@dataclass(frozen=True)
class OptionRow:
    contract_type: str
    expiry_month: str
    expiry_date: date
    strike: float
    kind: str
    settlement: float | None
    oi: int | None = None
    source_file: str | None = None


@dataclass(frozen=True)
class ContractGroup:
    contract_type: str
    expiry_month: str
    expiry_date: date
    rows: tuple[OptionRow, ...]


@dataclass(frozen=True)
class IvResult:
    contract_type: str
    expiry_month: str | None
    expiry_date: date | None
    central_strike: float | None = None
    central_call_settlement: float | None = None
    central_put_settlement: float | None = None
    synthetic_forward: float | None = None
    call_strike: float | None = None
    call_settlement: float | None = None
    put_strike: float | None = None
    put_settlement: float | None = None
    remaining_trading_days: int | None = None
    call_iv: float | None = None
    put_iv: float | None = None
    reason: str | None = None
    source_file: str | None = None


def black76_price(kind: str, forward: float, strike: float, years: float, sigma: float) -> float:
    if kind not in {"Call", "Put"}:
        raise ValueError("kind must be Call or Put")
    if forward <= 0 or strike <= 0 or years <= 0 or sigma <= 0:
        raise ValueError("forward, strike, years and sigma must be positive")
    scale = sigma * math.sqrt(years)
    d1 = (math.log(forward / strike) + 0.5 * sigma * sigma * years) / scale
    d2 = d1 - scale
    if kind == "Call":
        return forward * _NORMAL.cdf(d1) - strike * _NORMAL.cdf(d2)
    return strike * _NORMAL.cdf(-d2) - forward * _NORMAL.cdf(-d1)


def implied_volatility(kind: str, price: float, forward: float, strike: float, years: float) -> float | None:
    if kind not in {"Call", "Put"} or price is None or forward <= 0 or strike <= 0 or years <= 0:
        return None
    intrinsic = max(forward - strike, 0.0) if kind == "Call" else max(strike - forward, 0.0)
    upper_bound = forward if kind == "Call" else strike
    if not (intrinsic < price < upper_bound):
        return None
    low, high = 1e-6, 5.0
    if black76_price(kind, forward, strike, years, high) < price:
        return None
    for _ in range(200):
        middle = (low + high) / 2
        model = black76_price(kind, forward, strike, years, middle)
        if abs(model - price) <= 1e-10:
            return middle
        if model < price:
            low = middle
        else:
            high = middle
    return (low + high) / 2


def _valid(row: OptionRow) -> bool:
    return row.settlement is not None and math.isfinite(row.settlement) and row.settlement > 0


def select_contract_groups(rows: Iterable[OptionRow], trade_date: date) -> dict[str, ContractGroup]:
    eligible = [row for row in rows if row.expiry_date >= trade_date]
    result: dict[str, ContractGroup] = {}
    for contract_type in ("週三週選", "週五週選", "月選"):
        candidates = [row for row in eligible if row.contract_type == contract_type]
        if not candidates:
            continue
        expiry = min(row.expiry_date for row in candidates)
        selected = [row for row in candidates if row.expiry_date == expiry]
        expiry_months = {row.expiry_month for row in selected}
        if len(expiry_months) != 1:
            continue
        result[contract_type] = ContractGroup(contract_type, selected[0].expiry_month, expiry, tuple(selected))
    return result


def _select_central_pair(rows: Iterable[OptionRow]) -> tuple[OptionRow, OptionRow] | None:
    calls = {row.strike: row for row in rows if row.kind == "Call" and _valid(row)}
    puts = {row.strike: row for row in rows if row.kind == "Put" and _valid(row)}
    pairs = [(calls[strike], puts[strike]) for strike in calls.keys() & puts.keys()]
    if not pairs:
        return None
    return min(pairs, key=lambda pair: (abs(pair[0].settlement - pair[1].settlement), pair[0].strike))


def _select_otm_rows(rows: Iterable[OptionRow], forward: float) -> tuple[OptionRow, OptionRow] | None:
    calls = [row for row in rows if row.kind == "Call" and _valid(row) and row.strike > forward]
    puts = [row for row in rows if row.kind == "Put" and _valid(row) and row.strike < forward]
    if not calls or not puts:
        return None
    return min(calls, key=lambda row: row.strike), max(puts, key=lambda row: row.strike)


def remaining_trading_days(trade_date: date, expiry_date: date, holidays: set[date]) -> int:
    total = 0
    current = trade_date + timedelta(days=1)
    while current <= expiry_date:
        if current.weekday() < 5 and current not in holidays:
            total += 1
        current += timedelta(days=1)
    return total


def calculate_contract_iv(rows: Iterable[OptionRow], trade_date: date, holidays: set[date], covered_years: set[int]) -> IvResult:
    rows = tuple(rows)
    if not rows:
        return IvResult("未知", None, None, reason="contract_rows_missing")
    first = rows[0]
    source_file = next((row.source_file for row in rows if row.source_file), None)
    if any(row.expiry_date != first.expiry_date or row.expiry_month != first.expiry_month for row in rows):
        return IvResult(first.contract_type, first.expiry_month, first.expiry_date, reason="mixed_contract_rows")
    if trade_date.year not in covered_years or first.expiry_date.year not in covered_years:
        return IvResult(first.contract_type, first.expiry_month, first.expiry_date, reason="holiday_calendar_year_missing")
    days = remaining_trading_days(trade_date, first.expiry_date, holidays)
    if days <= 0:
        return IvResult(first.contract_type, first.expiry_month, first.expiry_date, remaining_trading_days=days, reason="expiry_or_no_trading_days")
    central = _select_central_pair(rows)
    if central is None:
        return IvResult(first.contract_type, first.expiry_month, first.expiry_date, remaining_trading_days=days, reason="central_pair_missing")
    central_call, central_put = central
    forward = central_call.strike + central_call.settlement - central_put.settlement
    if forward <= 0:
        return IvResult(first.contract_type, first.expiry_month, first.expiry_date, central_call.strike, central_call.settlement, central_put.settlement, forward, remaining_trading_days=days, reason="synthetic_forward_invalid")
    otm = _select_otm_rows(rows, forward)
    if otm is None:
        return IvResult(first.contract_type, first.expiry_month, first.expiry_date, central_call.strike, central_call.settlement, central_put.settlement, forward, remaining_trading_days=days, reason="strict_otm_row_missing")
    call, put = otm
    years = days / 252
    call_iv = implied_volatility("Call", call.settlement, forward, call.strike, years)
    put_iv = implied_volatility("Put", put.settlement, forward, put.strike, years)
    reason = None if call_iv is not None and put_iv is not None else "implied_volatility_not_converged"
    return IvResult(first.contract_type, first.expiry_month, first.expiry_date, central_call.strike, central_call.settlement, central_put.settlement, forward, call.strike, call.settlement, put.strike, put.settlement, days, call_iv, put_iv, reason, source_file)


def load_holiday_calendar(path: Path) -> tuple[set[date], set[int]]:
    required = {"交易日期", "市場", "是否休市", "來源", "更新時間"}
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        if reader.fieldnames is None or set(reader.fieldnames) != required:
            raise ValueError("holiday_calendar_format_invalid")
        holidays: set[date] = set()
        covered_years: set[int] = set()
        for row in reader:
            if row["市場"] != "TAIFEX" or not row["來源"] or not row["更新時間"]:
                raise ValueError("holiday_calendar_source_invalid")
            value = date.fromisoformat(row["交易日期"])
            if row["是否休市"] not in {"true", "false"}:
                raise ValueError("holiday_calendar_format_invalid")
            covered_years.add(value.year)
            if row["是否休市"] == "true":
                holidays.add(value)
    return holidays, covered_years



