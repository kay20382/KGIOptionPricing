# MSSQL-only 台股盤後報告流程設計

## 目標

盤後報告、選擇權摘要與交付作業只讀取 `CodexMarketData`，不再以桌面 CSV、舊狀態檔或即時下載檔作為報告數據來源。

## 資料完整性閘門

報告日以台北日期決定。執行前以 `dbo.ImportRuns` 查詢當日完成紀錄，並以半開區間核對每一資料集。核心門檻為 TWSE 收盤行情、TWSE 三大法人、TAIFEX 收盤行情、TAIFEX 法人未平倉、TAIFEX TXO 日盤、TAIFEX VIX 與 TAIFEX Put/Call 比；任一核心資料集缺失即停止產出與寄送，記錄缺少資料集與最後完成時間。

非核心資料包括 OTC 收盤行情、TAIFEX 大額交易人及題材映射。缺失時不可沿用舊值；僅在不影響核心段落正確性時留白並於稽核記錄標記。

## 報告資料契約

- 現貨行情與上市法人：`TWSE_Daily`、`TWSE_InstitutionalFlow`、`TWSE_InstitutionalDetail`。
- 台指期與法人未平倉：`TAIFEX_Daily`、`TAIFEX_InstitutionalOI`。
- 選擇權：`TAIFEX_OptionDaily`、`TAIFEX_VIX`、`TAIFEX_PutCallRatio`、`TAIFEX_AtmOTMIV`。
- 題材：先讀全市場日行情，再唯讀 `E:\Codex台股題材分類\題材列表.csv` 做廣度彙整；新聞僅能使用載入原始頁並經 24 小時時效稽核的證據。

所有查詢使用日期半開區間與資料表主鍵。報告端不得直接向 TWSE、TAIFEX 網頁取行情數字。

## IV 與欄位缺漏

Call／Put IV 只讀 `TAIFEX_AtmOTMIV` 當日三類契約中最近到期、未過期的一筆。若當日 IV 尚未寫入或任一權別為空，該權別維持空白，不得以 VIX、ATM、ITM、舊資料或估算值替代。VIX 與 P/C Ratio 各自採 MSSQL 官方匯入欄位。

## 交付與稽核

定稿文字分別交由 Gmail 與 Telegram 發送，任一通路失敗不得取消另一通路。成功時保存收件人／主旨／時間或 Telegram message ID／時間；失敗時保存去識別錯誤。資訊圖仍由使用者暫停，不生成或傳送。

每次執行保存當日資料完整性檢查、採用資料日期、選擇權最近到期日、新聞決策與交付結果。不得將休市、資料不足或未交付表述為已完成。

## 驗收條件

1. 報告數字可回溯至當日 MSSQL 資料列，且無桌面 CSV 依賴。
2. 核心資料缺失時不產出、不寄送，錯誤指出缺少資料集。
3. 非核心資料缺失不會被舊資料填補。
4. Gmail 與 Telegram 的交付紀錄獨立且可驗證。
