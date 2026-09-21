#!/usr/bin/env python3
"""Fetch Taiwan post-market data and write Excel-friendly CSV files.

The script intentionally keeps official data sets separate so that transaction
flows, open interest, day-session data and after-hours data cannot be confused.
It uses only Python's standard library.
"""

from __future__ import annotations

import argparse
import csv
import html
import json
import os
import re
import shutil
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from datetime import date, datetime, time as dt_time, timedelta, timezone
from email.utils import parsedate_to_datetime
from html.parser import HTMLParser
from pathlib import Path
from typing import Iterable, Sequence
from zoneinfo import ZoneInfo

from scripts.read_postmarket_iv import format_iv_csv_row, load_nearest_report_iv


TAIPEI = ZoneInfo("Asia/Taipei")
USER_AGENT = "Mozilla/5.0 (compatible; KGI-PostMarket-Data/1.0)"
DEFAULT_OUTPUT_DIR = Path.home() / "Desktop" / "台股盤後"

TWSE_INSTITUTIONAL = "https://www.twse.com.tw/rwd/zh/fund/BFI82U"
TWSE_TAIEX_HISTORY = "https://www.twse.com.tw/rwd/zh/TAIEX/MI_5MINS_HIST"
TWSE_MARKET_SUMMARY = "https://www.twse.com.tw/rwd/zh/afterTrading/FMTQIK"
TWSE_DAILY_MARKET = "https://www.twse.com.tw/rwd/zh/afterTrading/MI_INDEX"

TAIFEX_FUTURES_DAILY = "https://www.taifex.com.tw/cht/3/futDailyMarketReport"
TAIFEX_FUTURES_INSTITUTIONAL = "https://www.taifex.com.tw/cht/3/futContractsDateExcel"
TAIFEX_OPTIONS_DAILY = "https://www.taifex.com.tw/cht/3/optDailyMarketReport"
TAIFEX_PC_RATIO = "https://www.taifex.com.tw/cht/3/pcRatio"
TAIFEX_VIX_TEMPLATE = (
    "https://www.taifex.com.tw/file/taifex/Dailydownload/vix/"
    "log2data/{year_month}new.txt"
)


class DataError(RuntimeError):
    """Raised when an official data source is missing or internally invalid."""


class TableParser(HTMLParser):
    """Small HTML table parser suitable for the TAIFEX server-rendered tables."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.tables: list[list[list[str]]] = []
        self._table_depth = 0
        self._table: list[list[str]] | None = None
        self._row: list[str] | None = None
        self._cell: list[str] | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        tag = tag.lower()
        if tag == "table":
            self._table_depth += 1
            if self._table_depth == 1:
                self._table = []
        elif self._table_depth == 1 and tag == "tr":
            self._row = []
        elif self._table_depth == 1 and tag in {"td", "th"}:
            self._cell = []
        elif self._cell is not None and tag == "br":
            self._cell.append(" ")

    def handle_data(self, data: str) -> None:
        if self._cell is not None:
            self._cell.append(data)

    def handle_endtag(self, tag: str) -> None:
        tag = tag.lower()
        if self._table_depth == 1 and tag in {"td", "th"} and self._cell is not None:
            value = re.sub(r"\s+", " ", "".join(self._cell)).strip()
            if self._row is not None:
                self._row.append(value)
            self._cell = None
        elif self._table_depth == 1 and tag == "tr" and self._row is not None:
            if any(cell for cell in self._row):
                assert self._table is not None
                self._table.append(self._row)
            self._row = None
        elif tag == "table" and self._table_depth:
            if self._table_depth == 1 and self._table:
                self.tables.append(self._table)
                self._table = None
            self._table_depth -= 1


@dataclass(frozen=True)
class FetchResult:
    url: str
    body: bytes


def http_get(url: str, params: dict[str, str] | None = None, retries: int = 3) -> FetchResult:
    if params:
        url = f"{url}?{urllib.parse.urlencode(params)}"
    request = urllib.request.Request(
        url,
        headers={
            "User-Agent": USER_AGENT,
            "Accept": "application/json,text/html,text/plain,application/xml;q=0.9,*/*;q=0.8",
            "Accept-Language": "zh-TW,zh;q=0.9,en;q=0.5",
        },
    )
    last_error: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(request, timeout=35) as response:
                return FetchResult(response.geturl(), response.read())
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            last_error = exc
            if attempt + 1 < retries:
                time.sleep(1.5 * (attempt + 1))
    raise DataError(f"無法取得 {url}: {last_error}")


def decode_html(body: bytes) -> str:
    # TAIFEX endpoints currently mix UTF-8 and Big5/CP950 responses.
    try:
        return body.decode("utf-8")
    except UnicodeDecodeError:
        return body.decode("cp950", errors="replace")


def parse_tables(body: bytes) -> list[list[list[str]]]:
    parser = TableParser()
    parser.feed(decode_html(body))
    return parser.tables


def parse_json(result: FetchResult, expected_date: date) -> dict:
    try:
        payload = json.loads(result.body.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise DataError(f"官方JSON解析失敗 {result.url}: {exc}") from exc
    if payload.get("stat") != "OK":
        raise DataError(f"官方JSON狀態不是OK {result.url}: {payload.get('stat')}")
    response_date = str(payload.get("date") or "").replace("/", "")
    if response_date and response_date != expected_date.strftime("%Y%m%d"):
        raise DataError(f"資料日期不符 {result.url}: {response_date}")
    return payload


def number(value: object, *, allow_blank: bool = False) -> float | int | None:
    text = html.unescape(str(value or "")).strip().replace(",", "")
    text = re.sub(r"<[^>]*>", "", text).strip()
    is_down = "▼" in text
    text = text.replace("▲", "").replace("▼", "").replace("%", "").strip()
    if text in {"", "-", "--", "－"}:
        if allow_blank:
            return None
        raise DataError(f"必要數值為空: {value!r}")
    try:
        result = float(text)
    except ValueError as exc:
        raise DataError(f"無法解析數值: {value!r}") from exc
    if is_down:
        result = -abs(result)
    return int(result) if result.is_integer() else result


def iso_date(raw: object) -> str:
    text = re.sub(r"\D", "", str(raw or ""))
    if len(text) != 8:
        raise DataError(f"無法解析日期: {raw!r}")
    return f"{text[:4]}-{text[4:6]}-{text[6:8]}"


def taifex_date(day: date) -> str:
    return day.strftime("%Y/%m/%d")


def write_csv(path: Path, rows: Iterable[dict], fieldnames: Sequence[str]) -> int:
    materialized = list(rows)
    if not materialized:
        raise DataError(f"拒絕寫入空白CSV: {path.name}")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(materialized)
    return len(materialized)


def table_with_header(payload: dict, required_field: str) -> dict:
    for table in payload.get("tables", []):
        fields = table.get("fields") or []
        if required_field in fields:
            return table
    raise DataError(f"TWSE回應缺少欄位: {required_field}")


def fetch_twse_institutional(day: date) -> tuple[list[dict], str]:
    result = http_get(TWSE_INSTITUTIONAL, {"date": day.strftime("%Y%m%d"), "response": "json"})
    payload = parse_json(result, day)
    fields = payload.get("fields") or []
    if fields != ["單位名稱", "買進金額", "賣出金額", "買賣差額"]:
        raise DataError(f"證券三大法人欄位異動: {fields}")
    rows = []
    for raw in payload.get("data", []):
        buy, sell, net = (int(number(raw[i])) for i in (1, 2, 3))
        rows.append(
            {
                "日期": day.isoformat(),
                "單位名稱": raw[0],
                "買進金額_元": buy,
                "賣出金額_元": sell,
                "買賣差額_元": net,
                "買賣差額_億元": round(net / 100_000_000, 2),
                "資料來源": result.url,
            }
        )
    if not any("外資及陸資" in row["單位名稱"] for row in rows):
        raise DataError("證券三大法人資料缺少外資列")
    return rows, result.url


def fetch_twse_payload(day: date, endpoint: str, params: dict[str, str]) -> tuple[dict, str]:
    result = http_get(endpoint, params | {"date": day.strftime("%Y%m%d"), "response": "json"})
    return parse_json(result, day), result.url


def roc_to_date(raw: str) -> date:
    parts = [int(part) for part in raw.split("/")]
    return date(parts[0] + 1911, parts[1], parts[2])


def get_previous_trading_day(day: date) -> date:
    candidates: list[date] = []
    for anchor in (day, day.replace(day=1) - timedelta(days=1)):
        payload, _ = fetch_twse_payload(anchor, TWSE_TAIEX_HISTORY, {})
        for row in payload.get("data", []):
            candidate = roc_to_date(row[0])
            if candidate < day:
                candidates.append(candidate)
    if not candidates:
        raise DataError(f"找不到 {day.isoformat()} 的前一交易日")
    return max(candidates)


def fetch_market_summary(day: date) -> tuple[list[dict], str]:
    history, history_url = fetch_twse_payload(day, TWSE_TAIEX_HISTORY, {})
    history_row = next((row for row in history.get("data", []) if roc_to_date(row[0]) == day), None)
    if history_row is None:
        raise DataError("加權指數歷史資料沒有指定日期")

    market, market_url = fetch_twse_payload(day, TWSE_MARKET_SUMMARY, {})
    market_row = next((row for row in market.get("data", []) if roc_to_date(row[0]) == day), None)
    if market_row is None:
        raise DataError("市場成交資訊沒有指定日期")
    row = {
        "日期": day.isoformat(),
        "加權指數開盤": number(history_row[1]),
        "加權指數最高": number(history_row[2]),
        "加權指數最低": number(history_row[3]),
        "加權指數收盤": number(history_row[4]),
        "加權指數漲跌點": number(market_row[5]),
        "市場成交股數": number(market_row[1]),
        "市場成交金額_元": number(market_row[2]),
        "市場成交筆數": number(market_row[3]),
        "資料來源_指數": history_url,
        "資料來源_成交": market_url,
    }
    return [row], f"{history_url} ; {market_url}"


def sign_from_html(value: str) -> int:
    plain = re.sub(r"<[^>]*>", "", value or "").strip()
    if "+" in plain:
        return 1
    if "-" in plain or "−" in plain:
        return -1
    return 0


def fetch_hot_stocks(day: date, limit: int = 50) -> tuple[list[dict], str]:
    payload, source = fetch_twse_payload(day, TWSE_DAILY_MARKET, {"type": "ALLBUT0999"})
    table = table_with_header(payload, "證券代號")
    fields = table["fields"]
    index = {name: fields.index(name) for name in fields}
    rows = []
    for raw in table.get("data", []):
        code = str(raw[index["證券代號"]]).strip()
        if not re.fullmatch(r"[1-9]\d{3}", code):
            continue
        sign = sign_from_html(str(raw[index["漲跌(+/-)"]]))
        change_abs = number(raw[index["漲跌價差"]], allow_blank=True)
        close = number(raw[index["收盤價"]], allow_blank=True)
        if close is None or change_abs is None:
            continue
        rows.append(
            {
                "日期": day.isoformat(),
                "證券代號": code,
                "證券名稱": raw[index["證券名稱"]],
                "成交金額_元": int(number(raw[index["成交金額"]])),
                "成交股數": int(number(raw[index["成交股數"]])),
                "收盤價": close,
                "漲跌價差": sign * abs(float(change_abs)),
                "漲跌幅_%": "",
                "資料來源": source,
            }
        )
    rows.sort(key=lambda item: item["成交金額_元"], reverse=True)
    for rank, row in enumerate(rows[:limit], start=1):
        previous = float(row["收盤價"]) - float(row["漲跌價差"])
        row["成交金額排名"] = rank
        row["漲跌幅_%"] = round(float(row["漲跌價差"]) / previous * 100, 2) if previous else ""
    return rows[:limit], source


def find_table(tables: list[list[list[str]]], required_terms: Sequence[str]) -> list[list[str]]:
    for table in tables:
        joined = " ".join(" ".join(row) for row in table[:8])
        if all(term in joined for term in required_terms):
            return table
    raise DataError(f"找不到官方表格: {', '.join(required_terms)}")


def fetch_futures_daily(day: date) -> tuple[list[dict], str]:
    params = {
        "queryType": "2",
        "marketCode": "0",
        "commodity_id": "TX",
        "queryDate": taifex_date(day),
    }
    result = http_get(TAIFEX_FUTURES_DAILY, params)
    table = find_table(parse_tables(result.body), ["契約", "未沖銷契約量", "一般交易時段"])
    rows = []
    for raw in table:
        if len(raw) < 16 or raw[0] != "TX" or not re.fullmatch(r"\d{6}", raw[1]):
            continue
        rows.append(
            {
                "日期": day.isoformat(),
                "契約": raw[0],
                "到期月份": raw[1],
                "開盤價": number(raw[2], allow_blank=True),
                "最高價": number(raw[3], allow_blank=True),
                "最低價": number(raw[4], allow_blank=True),
                "最後成交價": number(raw[5], allow_blank=True),
                "漲跌點": number(raw[6], allow_blank=True),
                "漲跌幅_%": number(raw[7], allow_blank=True),
                "盤後交易時段成交量": number(raw[8], allow_blank=True),
                "一般交易時段成交量": number(raw[9], allow_blank=True),
                "合計成交量": number(raw[10], allow_blank=True),
                "結算價": number(raw[11], allow_blank=True),
                "未平倉量": number(raw[12], allow_blank=True),
                "最近月": 0,
                "資料來源": result.url,
            }
        )
    if not rows:
        raise DataError("台指期官方行情沒有可用契約")
    rows.sort(key=lambda item: item["到期月份"])
    rows[0]["最近月"] = 1
    if rows[0]["最後成交價"] is None or rows[0]["漲跌點"] is None:
        raise DataError("台指期最近月缺少收盤價或漲跌點")
    return rows, result.url


INSTITUTION_NAMES = {"自營商", "投信", "外資"}


def parse_futures_institutional(result: FetchResult, day: date) -> list[dict]:
    table = find_table(parse_tables(result.body), ["交易口數與契約金額", "未平倉餘額"])
    rows: list[dict] = []
    current_product = ""
    for raw in table:
        if "臺股期貨" in raw:
            current_product = "臺股期貨"
            identity_index = raw.index("自營商") if "自營商" in raw else -1
        elif current_product and raw and raw[0] in INSTITUTION_NAMES:
            identity_index = 0
        else:
            if raw and raw[0].isdigit() and "臺股期貨" not in raw:
                current_product = ""
            continue
        if current_product != "臺股期貨" or identity_index < 0:
            continue
        identity = raw[identity_index]
        values = raw[identity_index + 1 : identity_index + 13]
        if len(values) != 12:
            raise DataError(f"期貨法人欄位數異常: {identity} {len(values)}")
        parsed = [number(value) for value in values]
        rows.append(
            {
                "日期": day.isoformat(),
                "商品": "臺股期貨",
                "身份別": identity,
                "交易多方口數": parsed[0],
                "交易多方金額_千元": parsed[1],
                "交易空方口數": parsed[2],
                "交易空方金額_千元": parsed[3],
                "當日交易多空淨額_口": parsed[4],
                "當日交易多空淨額_千元": parsed[5],
                "未平倉多方口數": parsed[6],
                "未平倉多方金額_千元": parsed[7],
                "未平倉空方口數": parsed[8],
                "未平倉空方金額_千元": parsed[9],
                "未平倉多空淨額_口": parsed[10],
                "未平倉多空淨額_千元": parsed[11],
                "未平倉淨額日增減_口": "",
                "資料來源": result.url,
            }
        )
        if len(rows) == 3:
            break
    identities = {row["身份別"] for row in rows}
    if identities != INSTITUTION_NAMES:
        raise DataError(f"臺股期貨法人身份不完整: {sorted(identities)}")
    return rows


def fetch_futures_institutional(day: date, previous_day: date) -> tuple[list[dict], str]:
    def fetch(target: date) -> FetchResult:
        return http_get(
            TAIFEX_FUTURES_INSTITUTIONAL,
            {
                "queryType": "1",
                "goDay": "",
                "doQuery": "1",
                "dateaddcnt": "",
                "queryDate": taifex_date(target),
                "commodityId": "TXF",
            },
        )

    current_result = fetch(day)
    current = parse_futures_institutional(current_result, day)
    previous = {row["身份別"]: row for row in parse_futures_institutional(fetch(previous_day), previous_day)}
    for row in current:
        prior = previous[row["身份別"]]
        row["未平倉淨額日增減_口"] = int(row["未平倉多空淨額_口"]) - int(prior["未平倉多空淨額_口"])
    return current, current_result.url


OPTION_RIGHTS = {"Call", "Put"}


def parse_option_rows(result: FetchResult, day: date) -> list[dict]:
    table = find_table(parse_tables(result.body), ["到期月份", "履約價", "未沖銷"])
    rows = []
    for raw in table:
        if len(raw) < 16 or raw[0] != "TXO" or raw[4] not in OPTION_RIGHTS:
            continue
        try:
            expiry_date = iso_date(raw[2])
        except DataError:
            continue
        rows.append(
            {
                "日期": day.isoformat(),
                "契約": raw[0],
                "到期月份週別": raw[1],
                "契約到期日": expiry_date,
                "履約價": number(raw[3]),
                "權別": raw[4],
                "開盤價": number(raw[5], allow_blank=True),
                "最高價": number(raw[6], allow_blank=True),
                "最低價": number(raw[7], allow_blank=True),
                "最後成交價": number(raw[8], allow_blank=True),
                "結算價": number(raw[9], allow_blank=True),
                "漲跌價": number(raw[10], allow_blank=True),
                "一般交易時段成交量": number(raw[13], allow_blank=True),
                "未平倉量": number(raw[15], allow_blank=True),
                "未平倉量日增減": "",
                "資料來源": result.url,
            }
        )
    if not rows:
        raise DataError("臺指選擇權官方行情沒有可用資料")
    return rows


def fetch_option_result(day: date) -> FetchResult:
    return http_get(
        TAIFEX_OPTIONS_DAILY,
        {
            "queryType": "2",
            "marketCode": "0",
            "commodity_id": "TXO",
            "queryDate": taifex_date(day),
        },
    )


def fetch_nearest_option_oi(day: date, previous_day: date) -> tuple[list[dict], str, str]:
    current_result = fetch_option_result(day)
    current_all = parse_option_rows(current_result, day)
    expiry_dates = sorted({row["契約到期日"] for row in current_all if row["契約到期日"] >= day.isoformat()})
    if not expiry_dates:
        raise DataError("找不到尚未到期的臺指選擇權契約")
    nearest_expiry = expiry_dates[0]
    current = [row for row in current_all if row["契約到期日"] == nearest_expiry]

    previous_all = parse_option_rows(fetch_option_result(previous_day), previous_day)
    prior_lookup = {
        (row["到期月份週別"], row["履約價"], row["權別"]): row["未平倉量"]
        for row in previous_all
    }
    for row in current:
        key = (row["到期月份週別"], row["履約價"], row["權別"])
        prior_oi = prior_lookup.get(key)
        if row["未平倉量"] is not None and prior_oi is not None:
            row["未平倉量日增減"] = int(row["未平倉量"]) - int(prior_oi)
    if not any(row["權別"] == "Call" and row["未平倉量"] is not None for row in current):
        raise DataError("最近到期契約缺少Call未平倉量")
    if not any(row["權別"] == "Put" and row["未平倉量"] is not None for row in current):
        raise DataError("最近到期契約缺少Put未平倉量")
    current.sort(key=lambda item: (float(item["履約價"]), item["權別"]))
    return current, current_result.url, nearest_expiry


def fetch_pc_ratio(day: date) -> tuple[list[dict], str]:
    result = http_get(
        TAIFEX_PC_RATIO,
        {"queryStartDate": taifex_date(day), "queryEndDate": taifex_date(day)},
    )
    table = find_table(parse_tables(result.body), ["賣權成交量", "買權成交量", "未平倉量比率"])
    for raw in table:
        if len(raw) < 7:
            continue
        try:
            row_day = datetime.strptime(raw[0].strip(), "%Y/%m/%d").date()
        except ValueError:
            continue
        if row_day != day:
            continue
        return [
            {
                "日期": day.isoformat(),
                "賣權成交量": number(raw[1]),
                "買權成交量": number(raw[2]),
                "成交量P_C_Ratio_%": number(raw[3]),
                "賣權未平倉量": number(raw[4]),
                "買權未平倉量": number(raw[5]),
                "未平倉P_C_Ratio_%": number(raw[6]),
                "統計口徑": "臺指選擇權週到期契約與各到期月份契約合併",
                "資料來源": result.url,
            }
        ], result.url
    raise DataError("P/C Ratio官方表格沒有指定日期")


VIX_ROW = re.compile(r"^(\d{8})\s+(\d{8})\s+([0-9.]+)\s+([0-9.]+)\s*$")


def fetch_vix_month(day: date) -> tuple[list[dict], str]:
    url = TAIFEX_VIX_TEMPLATE.format(year_month=day.strftime("%Y%m"))
    result = http_get(url)
    text = result.body.decode("cp950", errors="replace")
    parsed: list[dict] = []
    for line in text.splitlines():
        match = VIX_ROW.match(line.strip())
        if not match:
            continue
        parsed.append(
            {
                "日期": iso_date(match.group(1)),
                "時間": match.group(2),
                "VIX": float(match.group(3)),
                "收盤前1分鐘平均VIX": float(match.group(4)),
            }
        )
    if not parsed:
        raise DataError("VIX官方文字檔沒有可解析資料")
    return parsed, result.url


def fetch_vix(day: date, previous_day: date, nearest_expiry: str) -> tuple[list[dict], str]:
    current_month, source = fetch_vix_month(day)
    previous_month = current_month
    if previous_day.strftime("%Y%m") != day.strftime("%Y%m"):
        previous_month, _ = fetch_vix_month(previous_day)
    current = next((row for row in current_month if row["日期"] == day.isoformat()), None)
    previous = next((row for row in previous_month if row["日期"] == previous_day.isoformat()), None)
    if current is None:
        raise DataError("VIX官方檔缺少指定日期")
    change = round(current["VIX"] - previous["VIX"], 2) if previous else ""
    try:
        iv_result = load_nearest_report_iv(day)
    except Exception as exc:
        raise DataError(f"MSSQL最近到期IV讀取失敗: {exc}") from exc
    row = format_iv_csv_row(iv_result, current["VIX"])
    row.update(
        {
            "日期": day.isoformat(),
            "最近到期日": row["最近到期日"] or nearest_expiry,
            "VIX日變化": change,
            "收盤前1分鐘平均VIX": current["收盤前1分鐘平均VIX"],
            "資料來源": f"{source}; MSSQL dbo.TAIFEX_AtmOTMIV",
        }
    )
    return [row], source


def fetch_news(now_taipei: datetime) -> tuple[list[dict], str]:
    queries = ["台股 盤後 熱門股 when:1d", "台股 盤後 題材股 when:1d"]
    cutoff = now_taipei - timedelta(hours=20)
    rows: list[dict] = []
    seen: set[tuple[str, str]] = set()
    urls: list[str] = []
    for query in queries:
        url = "https://news.google.com/rss/search?" + urllib.parse.urlencode(
            {"q": query, "hl": "zh-TW", "gl": "TW", "ceid": "TW:zh-Hant"}
        )
        result = http_get(url)
        urls.append(result.url)
        try:
            root = ET.fromstring(result.body)
        except ET.ParseError as exc:
            raise DataError(f"新聞RSS解析失敗: {exc}") from exc
        for item in root.findall(".//item"):
            title = (item.findtext("title") or "").strip()
            link = (item.findtext("link") or "").strip()
            source_element = item.find("source")
            publisher = (source_element.text or "").strip() if source_element is not None else ""
            published_raw = (item.findtext("pubDate") or "").strip()
            if not title or not link or not published_raw:
                continue
            published = parsedate_to_datetime(published_raw)
            if published.tzinfo is None:
                published = published.replace(tzinfo=timezone.utc)
            published_taipei = published.astimezone(TAIPEI)
            if not cutoff <= published_taipei <= now_taipei:
                continue
            key = (title, publisher)
            if key in seen:
                continue
            seen.add(key)
            rows.append(
                {
                    "標題": title,
                    "媒體": publisher,
                    "發布時間_台北": published_taipei.isoformat(timespec="seconds"),
                    "距擷取時間_小時": round((now_taipei - published_taipei).total_seconds() / 3600, 2),
                    "RSS連結": link,
                    "搜尋詞": query,
                    "使用限制": "僅供題材背景；行情數字須回官方來源核對",
                }
            )
    rows.sort(key=lambda item: item["發布時間_台北"], reverse=True)
    return rows, " ; ".join(urls)


def output_fields() -> dict[str, list[str]]:
    return {
        "證券三大法人.csv": ["日期", "單位名稱", "買進金額_元", "賣出金額_元", "買賣差額_元", "買賣差額_億元", "資料來源"],
        "市場概況.csv": ["日期", "加權指數開盤", "加權指數最高", "加權指數最低", "加權指數收盤", "加權指數漲跌點", "市場成交股數", "市場成交金額_元", "市場成交筆數", "資料來源_指數", "資料來源_成交"],
        "熱門股候選.csv": ["日期", "成交金額排名", "證券代號", "證券名稱", "成交金額_元", "成交股數", "收盤價", "漲跌價差", "漲跌幅_%", "資料來源"],
        "台指期行情.csv": ["日期", "契約", "到期月份", "開盤價", "最高價", "最低價", "最後成交價", "漲跌點", "漲跌幅_%", "盤後交易時段成交量", "一般交易時段成交量", "合計成交量", "結算價", "未平倉量", "最近月", "資料來源"],
        "期貨三大法人.csv": ["日期", "商品", "身份別", "交易多方口數", "交易多方金額_千元", "交易空方口數", "交易空方金額_千元", "當日交易多空淨額_口", "當日交易多空淨額_千元", "未平倉多方口數", "未平倉多方金額_千元", "未平倉空方口數", "未平倉空方金額_千元", "未平倉多空淨額_口", "未平倉多空淨額_千元", "未平倉淨額日增減_口", "資料來源"],
        "選擇權未平倉.csv": ["日期", "契約", "到期月份週別", "契約到期日", "履約價", "權別", "開盤價", "最高價", "最低價", "最後成交價", "結算價", "漲跌價", "一般交易時段成交量", "未平倉量", "未平倉量日增減", "資料來源"],
        "選擇權PC_Ratio.csv": ["日期", "賣權成交量", "買權成交量", "成交量P_C_Ratio_%", "賣權未平倉量", "買權未平倉量", "未平倉P_C_Ratio_%", "統計口徑", "資料來源"],
        "選擇權波動率.csv": ["日期", "最近到期日", "取樣履約價", "買權IV", "買權IV日變化", "賣權IV", "賣權IV日變化", "VIX", "VIX日變化", "收盤前1分鐘平均VIX", "隱波口徑", "資料狀態", "資料來源"],
        "近20小時新聞.csv": ["標題", "媒體", "發布時間_台北", "距擷取時間_小時", "RSS連結", "搜尋詞", "使用限制"],
    }


def atomic_publish(staging: Path, output_dir: Path, filenames: Iterable[str]) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    for filename in filenames:
        os.replace(staging / filename, output_dir / filename)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="抓取台股盤後官方資料並輸出CSV")
    parser.add_argument("--date", help="交易日期，格式YYYY-MM-DD；預設台北今天")
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR, help="CSV輸出資料夾")
    parser.add_argument("--news-hours", type=float, default=20.0, help="新聞回溯小時數，目前固定支援20")
    parser.add_argument("--skip-news", action="store_true", help="不抓新聞RSS")
    return parser.parse_args(argv)


def run(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    if args.news_hours != 20.0:
        raise DataError("為符合報告規格，--news-hours 必須為20")
    trading_day = date.fromisoformat(args.date) if args.date else datetime.now(TAIPEI).date()
    news_cutoff = (
        datetime.combine(trading_day, dt_time(16, 0), tzinfo=TAIPEI)
        if args.date
        else datetime.now(TAIPEI)
    )
    fetched_at = datetime.now(TAIPEI)
    output_dir = args.output_dir.expanduser().resolve()
    previous_day = get_previous_trading_day(trading_day)
    staging = Path(tempfile.mkdtemp(prefix="postmarket-", dir=str(output_dir.parent if output_dir.parent.exists() else Path.cwd())))
    counts: dict[str, int] = {}
    sources: list[str] = []
    warnings: list[str] = []
    try:
        data_sets: dict[str, list[dict]] = {}
        data_sets["證券三大法人.csv"], source = fetch_twse_institutional(trading_day)
        sources.append(source)
        data_sets["市場概況.csv"], source = fetch_market_summary(trading_day)
        sources.append(source)
        data_sets["熱門股候選.csv"], source = fetch_hot_stocks(trading_day)
        sources.append(source)
        data_sets["台指期行情.csv"], source = fetch_futures_daily(trading_day)
        sources.append(source)
        data_sets["期貨三大法人.csv"], source = fetch_futures_institutional(trading_day, previous_day)
        sources.append(source)
        option_rows, source, nearest_expiry = fetch_nearest_option_oi(trading_day, previous_day)
        data_sets["選擇權未平倉.csv"] = option_rows
        sources.append(source)
        data_sets["選擇權PC_Ratio.csv"], source = fetch_pc_ratio(trading_day)
        sources.append(source)
        data_sets["選擇權波動率.csv"], source = fetch_vix(trading_day, previous_day, nearest_expiry)
        sources.append(source)

        if args.skip_news:
            warnings.append("已依參數略過新聞RSS")
        else:
            try:
                news_rows, source = fetch_news(news_cutoff)
                if news_rows:
                    data_sets["近20小時新聞.csv"] = news_rows
                    sources.append(source)
                else:
                    warnings.append("最近20小時沒有符合時間條件的新聞")
            except DataError as exc:
                warnings.append(str(exc))

        fields = output_fields()
        for filename, rows in data_sets.items():
            counts[filename] = write_csv(staging / filename, rows, fields[filename])

        status_rows = [
            {
                "擷取時間_台北": fetched_at.isoformat(timespec="seconds"),
                "新聞截止時間_台北": news_cutoff.isoformat(timespec="seconds"),
                "交易日期": trading_day.isoformat(),
                "前一交易日": previous_day.isoformat(),
                "狀態": "成功" if not warnings else "成功（有警告）",
                "警告": "；".join(warnings),
                "檔案筆數": "；".join(f"{name}:{count}" for name, count in sorted(counts.items())),
                "來源數": len(set(sources)),
            }
        ]
        status_fields = [
            "擷取時間_台北",
            "新聞截止時間_台北",
            "交易日期",
            "前一交易日",
            "狀態",
            "警告",
            "檔案筆數",
            "來源數",
        ]
        write_csv(staging / "資料擷取狀態.csv", status_rows, status_fields)
        filenames = list(data_sets) + ["資料擷取狀態.csv"]
        atomic_publish(staging, output_dir, filenames)
        print(f"完成：{trading_day.isoformat()}，輸出至 {output_dir}")
        for filename in filenames:
            print(f"  {filename}: {counts.get(filename, 1)} 筆")
        for warning in warnings:
            print(f"警告：{warning}", file=sys.stderr)
        return 0
    finally:
        shutil.rmtree(staging, ignore_errors=True)


if __name__ == "__main__":
    try:
        raise SystemExit(run())
    except (DataError, ValueError) as exc:
        print(f"失敗：{exc}", file=sys.stderr)
        raise SystemExit(1)

