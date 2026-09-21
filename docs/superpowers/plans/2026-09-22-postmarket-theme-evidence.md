# 台股盤後題材與新聞證據 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將全市場 MSSQL 個股漲跌映射至既有台股題材分類，並僅保留近 24 小時、可驗證原文時間的新聞證據。

**Architecture:** 題材映射解析、盤勢彙總與新聞時效驗證分為純函式；命令列程序只讀 MSSQL 與 `E:\Codex台股題材分類\題材列表.csv`，輸出可稽核候選資料，且不寫回題材專案。

**Tech Stack:** Python 3.12 standard library、`unittest`、sqlcmd、CSV、urllib。

**Spec:** `docs/superpowers/specs/2026-09-21-txo-atm-iv-design.md`

## Global Constraints

- 所有行情數字只來自 MSSQL；題材 CSV 只讀。
- 新聞僅作解釋；原文發布／實質更新時間須在執行前 24 小時內。
- 排除論壇、社群、匿名、無時間、重刊與 AI 彙整頁。
- 每個題材最多兩檔代表股，且不得由單一股票獨自成為主題。

## Review Focus

- 同一股票被多個二階題材映射：保留全部映射但不得在一階統計重複計數。
- 未映射股票：顯示未分類，不可臆測歸類。
- 新聞時間恰為 24 小時邊界：納入；超過一秒即排除。
- 網頁只顯示更新時間：使用實質更新時間並保存它。
- 合作傳聞：沒有公司公告時必須標為未確認。

---

### Task 1: 題材映射與市場廣度核心

**Files:**
- Create: `scripts/postmarket_theme_evidence.py`
- Test: `scripts/tests/test_postmarket_theme_evidence.py`

**Interfaces:**
- Produces: `load_theme_mapping(path: Path) -> dict[str, list[Theme]]`
- Produces: `aggregate_theme_breadth(stocks, mapping) -> list[ThemeSummary]`

- [ ] **Step 1: Write failing mapping and de-duplication tests**
```python
def test_keeps_multiple_secondary_themes_but_counts_stock_once_per_first_theme():
    summaries = aggregate_theme_breadth(stocks, mapping)
    assert summaries['半導體'].advancers == 2

def test_unmapped_stock_is_not_assigned_to_a_theme():
    assert aggregate_theme_breadth([stock('9999')], mapping).unmapped_codes == ['9999']
```
- [ ] **Step 2: Run test and verify red**
Run: `python -m unittest scripts.tests.test_postmarket_theme_evidence -v`
Expected: `ModuleNotFoundError`.
- [ ] **Step 3: Implement CSV parser and deterministic ranking**
Parse `對應股票` code prefixes; aggregate up/down counts, equal-weight average percentage change and total turnover. Rank by breadth first, then turnover, then absolute average change; require at least two mapped movers.
- [ ] **Step 4: Run focused tests and commit**
Run: `python -m unittest scripts.tests.test_postmarket_theme_evidence -v`
Expected: PASS.
```bash
git add scripts/postmarket_theme_evidence.py scripts/tests/test_postmarket_theme_evidence.py
git commit -m "feat: aggregate postmarket themes from market data"
```

### Task 2: 24-hour original-news validation

**Files:**
- Modify: `scripts/postmarket_theme_evidence.py`
- Test: `scripts/tests/test_postmarket_news_validation.py`

**Interfaces:**
- Produces: `validate_news_item(item, now_taipei) -> NewsDecision`
- Produces: `filter_eligible_news(items, now_taipei) -> list[NewsDecision]`

- [ ] **Step 1: Write failing boundary tests**
```python
def test_accepts_exactly_24_hours_old_original_article():
    assert validate_news_item(item_at(now - timedelta(hours=24)), now).eligible

def test_rejects_article_older_than_24_hours_or_without_timestamp():
    assert not validate_news_item(item_at(now - timedelta(hours=24, seconds=1)), now).eligible
    assert not validate_news_item(item_without_time(), now).eligible
```
- [ ] **Step 2: Run and verify red**
Run: `python -m unittest scripts.tests.test_postmarket_news_validation -v`
Expected: missing function failure.
- [ ] **Step 3: Implement source and timestamp gates**
Accept only loaded original-page metadata with title, publisher, canonical URL, timestamp and `is_original=True`; write a rejection reason for each excluded item. Flag non-official cooperation claims as `未確認合作傳聞`.
- [ ] **Step 4: Run all theme tests and commit**
Run: `python -m unittest discover -s scripts/tests -v`
Expected: PASS.
```bash
git add scripts/postmarket_theme_evidence.py scripts/tests/test_postmarket_news_validation.py
git commit -m "feat: validate postmarket news freshness"
```

### Task 3: MSSQL reader and audit output

**Files:**
- Modify: `scripts/postmarket_theme_evidence.py`
- Test: `scripts/tests/test_postmarket_theme_sql.py`
- Create: `outputs/README.md`

**Interfaces:**
- Produces: `load_market_stocks(trade_date: date) -> list[StockMove]`
- Produces: `write_theme_audit(path: Path, summaries, news) -> None`

- [ ] **Step 1: Write failing query-scope and audit-output tests**
```python
def test_market_query_reads_twse_and_otc_with_half_open_dates():
    sql = build_market_query(date(2026, 9, 22))
    assert 'dbo.TWSE_Daily' in sql and 'dbo.OTC_Daily' in sql
    assert "< '2026-09-23'" in sql

def test_audit_contains_source_time_url_and_theme_decision(tmp_path):
    write_theme_audit(tmp_path/'audit.csv', summaries, news)
    assert 'source_url' in (tmp_path/'audit.csv').read_text(encoding='utf-8')
```
- [ ] **Step 2: Run and verify red**
Run: `python -m unittest scripts.tests.test_postmarket_theme_sql -v`
Expected: missing reader/output functions.
- [ ] **Step 3: Implement read-only SQL and versioned audit output**
Use `sqlcmd` with half-open dates, calculate percentage from each market's official signed change fields, and write audit CSV with trade date, source row count, theme, stock, decision, title, publisher, timestamp and URL. Never create files inside `E:\Codex台股題材分類`.
- [ ] **Step 4: Run full Python suite and commit**
Run: `python -m unittest discover -s scripts/tests -v`
Expected: PASS.
```bash
git add scripts/postmarket_theme_evidence.py scripts/tests/test_postmarket_theme_sql.py outputs/README.md
git commit -m "feat: audit postmarket theme evidence"
```
