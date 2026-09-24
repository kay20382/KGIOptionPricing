# KGIOptionPricing 程式碼功能分類設計

日期：2026-09-24

## 目的與成功條件

將整個儲存庫的手寫程式碼按功能歸類，使畫面、行情、商品、交易、策略、研究與維運程式容易定位。這是目錄重整，不改類別或命名空間、公開介面、行情重建、下單、策略或使用者介面行為。WinForms 固定畫面仍由各自的 `*.Designer.cs` 管理，維持目前 Dashboard 視覺風格。

成功條件是：所有納入搬移的手寫檔案有明確歸屬；原有未提交修改與未追蹤原始檔原樣保留；主程式與測試的檔案引用有效；既有命令入口、設定與執行輸出路徑不意外改變；驗證結果可區分搬移前已有的失敗與新增失敗。

## 範圍

- 納入：`KGIOptionPricing/` 的手寫 C# 原始碼、`tests/` 的手寫測試、`scripts/` 與儲存庫根目錄的手寫 Python／PowerShell 程式。已按功能分組的測試專案可留在原處。
- 不納入：`bin`、`obj`、`obj_*`、`.testout`、研究或驗證產物、行情／交易資料、日誌、`libs` DLL、設定值、圖示及歷史規格／計畫文件。這些不因本次分類而清除或搬移。
- `KGIOptionPricing.sln`、`KGIOptionPricing.csproj`、`Program.cs`、`config.json`、`log4net.config`、`holidays.txt`、`VICON.ico` 與 `libs/` 留在目前位置；已刪除的 `patcher` 不恢復。
- 不啟動實盤程式、不連接券商或發送委託、不修改外部排程、不推送 Git。

## 目標目錄與原始檔歸屬

```text
KGIOptionPricing/
  Program.cs, KGIOptionPricing.csproj, config.json, log4net.config, holidays.txt, VICON.ico, libs/
  Dashboard/
  MarketData/
  Products/ (含 ProductSpreads/、UI/)
  Options/ (含 UI/)
  Trading/ (含 OrderManagement/、LightningOrder/、UI/)
  Strategies/ (含既有策略目錄、QueueFollow/、UI/)
  StrategyFramework/
  Research/
  Infrastructure/
tests/ (保留各獨立測試專案)
scripts/ (依 research/、reporting/、operations/ 分類；穩定命令入口可留在 scripts/ 根目錄)
```

| 功能 | 從主專案根目錄歸入的檔案或檔案組 |
| --- | --- |
| Dashboard | `Form1.cs`、`Form1.Designer.cs`、`Form1.resx` |
| MarketData | `KGIQuoteService.cs`、`MarketDataService.cs`、`ForeignDepthBook.cs` |
| Products | `SymbolRepository.cs`、`HotMonthSelector.cs`、`PriceTick.cs`、`TradingCalendar.cs`；`InstrumentSelectorForm` 三件組進 `Products/UI/`；既有 `ProductSpreads/` 與 `SpreadConfigEvaluator.cs` 合併於 `Products/ProductSpreads/` |
| Options | `Option.cs`、`OptionChain.cs`、`BlackScholesMath.cs`、`VolatilityCurve.cs`、`IMPVStorageManager.cs`；`OptionPricingForm` 三件組進 `Options/UI/` |
| Trading | `KGITradeService.cs`；`LightningOrderModels.cs` 及閃電下單畫面／設定／改價對話框進 `Trading/LightningOrder/`；既有 `OrderManagement/` 進 `Trading/OrderManagement/`；`OrderPlacementForm`、`OrderReportForm`、`DealReportForm`、`OrderModificationDialog`、`PositionForm`、`MarginEquityForm` 各自的主檔、Designer 與現有 resx 一起進 `Trading/UI/` |
| Strategies | 既有策略實作維持其功能目錄；`QueueFollowAdapters.cs`、`QueueFollowHistory.cs`、`QueueFollowModels.cs`、`QueueFollowPricePlanner.cs`、`QueueFollowStrategyEngine.cs` 收入 `Strategies/QueueFollow/`；`MeanReversionSessionParameterWriter.cs` 收入對應 Mean Reversion 策略目錄；`StrategyCenterForm`、`LiveStrategyCenterForm`、`QueueFollowStrategyForm`、`PeriodicQueueCycleControls` 各自成組進 `Strategies/UI/` |
| Research | 既有 `Research/` 保留，加入 `RiskDataExporter.cs` |
| Infrastructure | `CustomRollingFileAppender.cs` |

`StrategyFramework/` 是多策略共用執行層，保持獨立；不為了目錄對稱而搬入單一策略。每個 WinForms 畫面的 `.cs`、`.Designer.cs`、`.resx`（若存在）必須同批搬到同一目錄；不藉分類修改 UI 欄位、樣式或事件。

## 腳本與測試的相容性

`scripts/` 中研究、盤後報表與維運程式依用途分組；研究設定、SQL 與相應測試隨其功能更新引用。根目錄的 `fetch_taiwan_postmarket.py` 以及已被文件、測試或外部排程直接呼叫的 `scripts/<名稱>` 是穩定命令入口：未確認呼叫端前保留原路徑；其內部支援模組可分類。需要搬移入口時，先證明呼叫端已同步改路徑或保留等效相容入口，不能靜默中斷排程。歷史設計與實作計畫中的舊路徑屬於當時紀錄，不改寫；仍供使用的操作說明需更新。

現有 `tests/` 已按測試專案分組，不為了移動而改專案名稱。`ForeignDepthBookTests.csproj` 有直接 `Compile Include` 到主專案的 `ForeignDepthBook.cs`，必須改為新路徑。其他測試使用主專案 `ProjectReference`，主專案檔留原位，因此參照路徑不改。Python 腳本中的 `__file__`、`sys.path`、同目錄設定檔及測試中的命令路徑要依實際移動逐項核對。

## 搬移規則與失敗處理

1. 先建立來源、目標、Git 狀態與 SHA-256 清單，記錄本次搬移前的建置和測試基準。對每個候選目標做存在性與工作區邊界檢查；遇到同名檔案、來源在作業期間改變或歸屬不明時停止該批，不覆寫、不清理。
2. 按功能分批搬移並保持檔案內容不變。已追蹤與未追蹤檔案都以搬移後雜湊核對；既有使用者修改不得被重設、重建或混入無關提交。只有必要的 `.csproj` 路徑、腳本匯入／路徑及仍有效的操作說明可做文字調整。
3. 每批後檢查專案列舉、Designer 主檔配對、相對路徑與相關測試。若新增失敗能歸因於本批搬移，先修復或回退本批；不得藉此修改交易、行情或策略邏輯。
4. 若基準建置本來失敗，保留原始錯誤與新結果以比較，不宣稱所有測試通過，也不把其他任務的錯誤算成本次成果。

## 驗證與交付

- 對照來源與目的檔案清單／SHA-256；確認 UI 三件組、`.csproj` 直接檔案引用、設定與 DLL 路徑完整。
- 建置主專案，執行所有可執行的 .NET smoke tests、WinForms Designer 版面測試及 Python 單元測試；只做不會送出真實委託的驗證。Designer 設計時載入若無法自動驗證，須明確列為人工檢查項。
- 檢查穩定腳本入口和代表性 CLI `--help`／純讀取測試；核對使用中的操作說明與腳本路徑。
- 交付時列明實際搬移清單、必要路徑修正、測試結果、既有失敗與未驗證事項；其他工作區修改保持原狀，不自行推送。
