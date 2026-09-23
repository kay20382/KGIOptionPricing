# MSSQL-only 台股盤後報告流程 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓台股盤後報告僅以當日 MSSQL 匯入資料產出，並以可稽核資料完整性閘門控制交付。

**Architecture:** 在 `E:\Codex數據收集` 建立唯讀報告資料閘門與查詢層；報告呼叫端只消費結構化快照。完整性由 `dbo.ImportRuns` 驗證，Telegram 與 Gmail 交付由獨立結果紀錄處理。

**Tech Stack:** Python 3.12 standard library、unittest、SQL Server、既有 Windows 整合驗證 sqlcmd。

**Spec:** `E:\PROG\C#\KGIOptionPricing\docs\superpowers\specs\2026-09-23-mssql-postmarket-report-flow-design.md`

## Global Constraints

- 報告行情僅取 `CodexMarketData`，所有日期使用半開區間。
- 核心資料集缺失時禁止產出與寄送；不使用桌面 CSV 或舊資料填補。
- Call／Put IV 只讀 `TAIFEX_AtmOTMIV` 最近到期列，空值不替代。
- 題材分類 CSV 唯讀；新聞須原始頁且在 24 小時內。
- Gmail 與 Telegram 交付結果分別記錄；資訊圖維持暫停。

## Review Focus

- 重複 `ImportRuns`：取同日同資料集最新完成紀錄，不能誤判缺失。
- VIX 僅有盤中列：沒有 `Last 1 min AVG` 時停止核心產出。
- IV 沒寫入：輸出空 Call／Put IV，不能使用 VIX 取代。
- OTC 匯入失敗：不得影響僅使用上市市場的法人與指數正文，但稽核須標示。
- Telegram 失敗：Gmail 流程仍執行，反之亦然。

### Task 1: MSSQL 資料完整性閘門

**Files:**
- Create: `E:\Codex數據收集\postmarket_report_data.py`
- Test: `E:\Codex數據收集\test_postmarket_report_data.py`

**Interfaces:**
- Produces: `check_readiness(trade_date: date, runs: list[ImportRun]) -> Readiness`

- [ ] **Step 1: 寫失敗測試**
```python
def test_requires_all_core_datasets_and_uses_latest_completed_run():
    result = check_readiness(day, duplicate_runs_with_one_missing_core)
    assert not result.ready
    assert 'TAIFEX VIX' in result.missing
```
- [ ] **Step 2: 執行測試**
Run: `python -m unittest test_postmarket_report_data -v`
Expected: import failure.
- [ ] **Step 3: 實作核心資料集與最新完成判斷**
```python
CORE_DATASETS = frozenset({...})
def check_readiness(trade_date, runs): ...
```
- [ ] **Step 4: 執行測試並提交**
Run: `python -m unittest test_postmarket_report_data -v`
Expected: PASS.

### Task 2: MSSQL 報告快照與 IV 契約

**Files:**
- Modify: `E:\Codex數據收集\postmarket_report_data.py`
- Test: `E:\Codex數據收集\test_postmarket_report_data.py`

**Interfaces:**
- Produces: `load_report_snapshot(trade_date: date) -> ReportSnapshot`

- [ ] **Step 1: 寫失敗測試**
```python
def test_snapshot_keeps_missing_call_iv_empty_and_reads_official_vix():
    snapshot = build_snapshot(fixture_rows_without_call_iv)
    assert snapshot.call_iv is None
    assert snapshot.vix == 21.83
```
- [ ] **Step 2: 實作以 sqlcmd 查詢各資料表**
使用 `ImportRuns` 閘門、日期半開區間，讀取 TWSE、TAIFEX、VIX、P/C、TXO 與 `TAIFEX_AtmOTMIV`；查詢失敗回傳明確資料集錯誤。
- [ ] **Step 3: 執行測試並提交**
Run: `python -m unittest test_postmarket_report_data -v`
Expected: PASS.

### Task 3: 報告交付編排與稽核

**Files:**
- Create: `E:\Codex數據收集\postmarket_report_delivery.py`
- Test: `E:\Codex數據收集\test_postmarket_report_delivery.py`
- Modify: `E:\Codex數據收集\scheduled_post_market_update.py`

**Interfaces:**
- Produces: `deliver_report(report: str, snapshot: ReportSnapshot) -> DeliveryResult`

- [ ] **Step 1: 寫失敗測試**
```python
def test_telegram_failure_does_not_skip_gmail():
    result = deliver_with_fake_channels(gmail_ok=True, telegram_error=True)
    assert result.gmail.sent and not result.telegram.sent
```
- [ ] **Step 2: 實作獨立通路結果與稽核檔**
資料就緒後才編寫全文；分別呼叫 Gmail／Telegram，寫入日期、資料集狀態、交付時間與去識別錯誤。
- [ ] **Step 3: 執行全測試並提交**
Run: `python -m unittest discover -v`
Expected: PASS.

## Self-review

規格的資料來源、完整性閘門、IV 空值、題材唯讀與交付獨立性均由三個任務覆蓋；無 TBD 或未定義介面。每項 Review Focus 都由 Task 1 至 Task 3 的測試涵蓋。
