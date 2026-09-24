# WinForms 全畫面 Designer 化設計

## 目的與範圍

將 KGIOptionPricing 的固定 WinForms 介面結構放進對應的 `*.Designer.cs`，使使用者能在 Visual Studio 設計器中微調控制項、欄寬、字型、停靠及間距。維持既有 Dashboard 暗色系與各交易畫面的既有視覺語意；本次是介面程式碼重構，不增加交易功能，也不更改下單、刪改單、策略、行情或資料儲存邏輯。

範圍包含目前全部 13 個 Form：`Form1`、`OptionPricingForm`、`OrderPlacementForm`、`OrderReportForm`、`DealReportForm`、`PositionForm`、`MarginEquityForm`、`InstrumentSelectorForm`、`LightningOrderForm`、`LightningOrderSettingsForm`、`QueueFollowStrategyForm`、`StrategyCenterForm`、`LiveStrategyCenterForm`。前 7 個已有 Designer 檔，但其主檔內若仍建立固定版面，亦須搬移。其餘 6 個新增 Designer 檔。另含共用的 `PeriodicQueueCycleControls`，以及 `LightningOrderForm` 改價與 `OrderReportForm` 改單時以 `new Form` 臨時建立的兩個對話框。

不處理第三方 WinForms 控制項的內部實作、作業系統提供的 `OpenFileDialog`／`SaveFileDialog`，也不把執行期資料列、即時行情數值、委託狀態或策略狀態硬寫成設計時內容。

## 選用方案

選用「Designer 管固定版面、主檔管行為」。另一種純文字搬移作法只是把現有建構程式碼移到 `Designer.cs`，但迴圈、工廠方法與捕捉區域變數的事件通常無法由 Visual Studio 設計器可靠編輯，因此不符合微調目的。全面重新設計畫面則會同時改變交易操作習慣與驗證範圍，也不採用。

每個 Form 為 `partial` 類別，提供可由設計器安全建立的無參數建構入口；`InitializeComponent()` 建立固定控制項與容器、設定設計時屬性，並使用具名欄位而非僅存於建構子的區域變數。需要服務注入的執行期建構子沿用既有簽章，完成 `InitializeComponent()` 後才訂閱業務事件與啟動計時器；無參數入口不得啟動服務或被當作實盤入口。`Designer.cs` 只包含設計器可序列化的控制項初始化、配置、靜態樣式與安全的 UI 事件連結；不得呼叫行情、下單、帳戶或策略服務。

## 控制項邊界

- 固定的 `Panel`、`TableLayoutPanel`、`TabControl`、頁籤、標籤、按鈕、輸入欄、狀態欄，以及固定的 `DataGridView` 欄位與欄寬，放進 Designer。欄位值、行資料、依行情變動的顏色與可用狀態仍在主檔更新。
- 策略中心目前透過方法建立的固定頁籤與參數控制項，改為具名設計器欄位。策略監控資料表的列仍由原本的服務填入；不更動策略執行或停止流程。
- `PeriodicQueueCycleControls` 改為設計器可載入的 `partial UserControl`，內部固定參數控制項由 Designer 建立；既有參數鍵與取值介面保持相容。模擬／實盤模式只控制相關欄位可見性或啟用狀態，不重新產生整組固定 UI。
- 兩個臨時建立的編輯對話框改為各自具名的 Form 與 Designer；維持目前的數值上下限、預設值、確認／取消結果與呼叫端行為。
- `InstrumentSelectorForm` 的商品節點、月份、選擇權履約價和行情梯形表內容，屬執行期資料；其樹、搜尋欄、表格與固定欄位則在 Designer。
- `LightningOrderForm` 的固定五欄、按鈕及視窗最小尺寸在 Designer；價格序列、顯示值、目前成交價標示及滑動置中仍由主檔管理，保留既有黑底、紅價、白量、金色委託語意。

## Dashboard 視覺與安全性

以現有 `Form1.Designer.cs` 的 Dashboard 暗色背景、卡片、字型與間距作為全專案視覺參照；不在這次作整體重新配色。現有閃電下單與風險提示的專用顏色保留。若搬移過程發現固定屬性分散在主檔，先原值搬入 Designer，避免視覺或操作語意漂移。

設計器載入時不能連到交易／行情服務、啟動策略或計時器。不要以實盤委託驗證 UI。事件處理方法仍在主檔；不得為了移動控制項而改變點擊、雙擊、確認、刪單或關閉視窗的既有事件順序。視窗關閉後仍由原本邏輯解除訂閱與釋放資源。

## 實作與驗收

分批處理：先主 Dashboard 與既有 Designer Form，再商品選擇、閃電下單和對話框，最後策略中心及共用參數控制項。每批都檢查 `InitializeComponent()` 呼叫一次、`Dispose` 正常、欄位名稱與事件連結未斷、視窗縮放／DPI／停靠未回歸，並建置及執行相關既有 smoke tests。

完成條件：13 個 Form 和上述共用控制項、兩個編輯對話框都能在 Visual Studio 設計器中開啟並編輯固定版面；主 `.cs` 不再以 `Controls.Add` 或 `new Control` 建立其固定版面；執行期資料繫結和行情／委託更新仍可正常運作；Dashboard 風格與原有交易操作一致。設計器開啟結果須實際驗證，不能只以編譯成功代替。
