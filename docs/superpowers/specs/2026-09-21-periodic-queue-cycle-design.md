# 週期雙邊完成單策略設計

日期：2026-09-21  
穩定策略識別：`PeriodicQueueCycle`  
畫面名稱：`週期雙邊完成單`

## 1. 目的與成功條件

本策略在可設定的觀察週期內，使用健康的十檔委託簿判斷行情方向；通過流動性、價差、波動與報價新鮮度濾網後，同時啟動一張買進完成單與一張賣出完成單。兩張單彼此獨立，不是 OCO。兩邊均成交並回到空手後才算完成一個週期，經冷卻後重新收集資料並判斷下一個週期。

成功條件如下：

- 模擬、歷史重播與受控實盤使用同一個 `ISimulationStrategy`、同一套決策規則與共用帳本。
- 策略只透過 `MarketEvent`、`StrategyRuntimeContext`、共用委託契約及共用績效讀取資料，不直接呼叫 `OrderManager`、`KGITradeService` 或券商 API。
- 可精確辨識每一張完成單的送出、確認、改掛、成交、撤單與拒絕；不得以 `CancelAll` 取代正常的單張改掛生命週期。
- 方向、中性、行情不健康、單邊成交、固定停損、逾時平倉、週期完成與冷卻都有穩定狀態及原因代碼。
- 價格邊界在初掛、每次改掛、撤單重掛及緊急平倉路徑均有明確行為與測試。
- 實盤能力完成，但預設 `ResearchValidated=false`；不得為驗證程式而送出真實委託。

## 2. 已選方案

### 2.1 採用：擴充共用策略框架的單張委託生命週期

在既有 `StrategyFramework` 增加用戶端委託識別、單張撤單意圖及共用生命週期回報。`PeriodicQueueCycle` 使用共用框架送出兩張完成單，完成單的排隊價格計算重用既有 `QueueFollowPricePlanner` 的規則或抽出的純函式核心。

此方案能讓模擬、重播與實盤共用相同策略邏輯，並保留共用帳本、紀錄器、風險閘門及安全關閉規則。

### 2.2 未採用：直接包裝 `QueueFollowStrategyEngine`

既有引擎具備單張委託識別及改掛能力，但直接依賴 `OrderManager`、`KGITradeService` 與自己的歷史紀錄器，會形成平行策略引擎，無法符合共用策略框架契約。

### 2.3 未採用：在新策略內複製完成單邏輯

複製可以減少初期框架修改，但會產生兩套 A/B/C、價格邊界及改掛語意，後續修正容易分歧，因此不採用。

## 3. 架構與元件

### 3.1 策略元件

新增 `KGIOptionPricing/Strategies/PeriodicQueueCycle/`：

- `PeriodicQueueCycleFactory`：解析定義並建立策略。
- `PeriodicQueueCycleStrategy`：方向、濾網、週期及風險狀態機。
- `PeriodicQueueCycleParameters`：以 `InvariantCulture` 解析、限制安全上下限並輸出完整參數摘要。
- `DirectionWindow`：保存有效的一秒樣本，計算淨移動、路徑效率、十檔 OBI 與波動。
- `QueueCycleOrderCoordinator`：管理買賣兩張獨立完成單及每張單的改掛世代。
- `analysis/analyze_periodic_queue_cycle.py`：策略專屬診斷與參數比較入口。

### 3.2 共用框架擴充

目前 `PaperOrderRequest` 沒有策略產生的關聯 ID，`ISimulationStrategy` 只有整批撤單事件，策略也收不到撤單確認。完成單要安全改掛，必須新增下列共用能力：

- `PaperOrderRequest.ClientOrderId`：由策略產生，在同一策略執行個體內唯一。
- `PaperOrder`、`ExecutionEvent` 及紀錄產物保留 `ClientOrderId`。
- 共用 `OrderLifecycleEvent`：包含用戶端 ID、執行器 ID、可取得時的券商 ID、前後狀態、數量、價格、原因與時間。
- 選擇性介面 `IManagedOrderStrategy`，提供單張撤單事件與生命週期回呼；既有策略不必立刻改寫。
- 選擇性執行介面將模擬與實盤的送出、確認、撤單確認、拒絕、首次成交與完全成交轉為相同生命週期事件。
- `StrategyInstanceRunner` 負責路由，不把執行服務或券商物件暴露給策略。

改掛採「要求撤銷舊單 → 收到撤單確認 → 以新 `ClientOrderId` 送出下一世代」；不得在尚未確認撤單時送出替代單。若撤單與成交競速，先以實際成交更新共用帳本，再依最新淨部位決定是否仍可重掛。

### 3.3 既有完成單價格規則

保留 A/B/C 語意：

- A：首次掛單允許的最大前方累計口數。
- B：目前同價前方量加更佳價位量大於此值時，要求改掛。
- C：改掛後允許的最大前方累計口數。

價格只可朝更積極方向移動：買單只可提高，賣單只可降低。所有價格以解析後的 `TickSize` 對齊。兩張完成單各自保存工作中價格、剩餘數量及改掛世代，彼此不是 OCO。

## 4. 輸入資料與健康濾網

方向只使用可交易商品 `225MC` 的健康 Book 事件。每一秒最多加入一筆樣本；同秒後來的健康事件取代該秒較早事件。不健康事件不加入統計，但也不得讓策略無限沿用舊樣本。

每筆有效樣本必須符合：

- 買賣各有十檔有效、正數且價格順序正確的報價；不得交叉盤。
- 最新健康報價年齡不超過 `QuoteFreshnessSeconds=2`。
- 單邊十檔總量至少 `MinSideDepth=827`。
- 單邊前三檔總量至少 `MinNearDepth=165`。
- 較小側十檔量／較大側十檔量至少 `MinDepthRatio=0.35`。
- 買賣價差不超過 `MaxSpreadTicks=2`。
- 觀察窗高低差不超過 `MaxVolatilityTicks=5`。

開始新週期時，當下 Book 必須再次通過全部濾網。缺檔、舊報價或失衡市場只會產生拒絕原因，不會被補零或替換成舊資料。

## 5. 方向判斷

暖機必須先取得完整 `DirectionWindowSeconds=15` 秒健康樣本。

對每個樣本：

```text
MidPrice = (BestBid + BestAsk) / 2
NetMoveTicks = (LatestMid - OldestMid) / TickSize
PathTicks = sum(abs(Mid[i] - Mid[i-1])) / TickSize
TrendEfficiency = abs(NetMoveTicks) / max(PathTicks, 1)

WeightedBid = sum(BidQty[level] / (level + 1))
WeightedAsk = sum(AskQty[level] / (level + 1))
DepthObi = (WeightedBid - WeightedAsk) / (WeightedBid + WeightedAsk)
```

`AverageObi` 是最近 `ObiConfirmSeconds=3` 秒有效樣本的平均。

向上成立必須同時符合：

```text
NetMoveTicks >= +MinMoveTicks
TrendEfficiency >= MinTrendEfficiency
AverageObi >= +MinDepthObi
```

向下成立必須同時符合：

```text
NetMoveTicks <= -MinMoveTicks
TrendEfficiency >= MinTrendEfficiency
AverageObi <= -MinDepthObi
```

預設為 `MinMoveTicks=2`、`MinTrendEfficiency=0.40`、`MinDepthObi=0.10`。三項是 AND 條件，不是多數決。其他情況均為中性，不啟動週期。

週期啟動後凍結方向及當時特徵。兩張單均未成交時，若相反方向完整條件連續成立 `DirectionReverseSeconds=3`，撤銷兩張單，記錄 `SignalReversedBeforeFill`，冷卻後重新暖機；這不是停損。

## 6. 雙邊完成單參數

趨勢方向使用較容易成交的暫定 A/B/C：

```text
TrendInitialAheadA=22
TrendRepriceThresholdB=49
TrendRepriceTargetC=22
```

反方向使用較難成交的暫定 A/B/C：

```text
OppositeInitialAheadA=240
OppositeRepriceThresholdB=312
OppositeRepriceTargetC=22
```

向上時買單採趨勢參數、賣單採反方向參數；向下時相反。兩張單同時產生委託意圖，不因其中一張成交而自動取消另一張。

上述數值來自目前有限健康樣本的暫定統計，只能標記為 `provisional-research-only`；正式實盤前必須以相同封存與執行假設完成重播、樣本外及滑價壓力測試。

## 7. 價格邊界

週期成立時，以當下可交易商品中間價對齊 Tick 後凍結 `CycleAnchorPrice`。自動計算：

```text
BuyPriceLimit  = alignDown(CycleAnchorPrice + MaxChaseTicks * TickSize)
SellPriceLimit = alignUp(CycleAnchorPrice - MaxChaseTicks * TickSize)
```

`MaxChaseTicks` 預設 2，可設定範圍 0 至 20。買單任何初掛或改掛價格不得高於 `BuyPriceLimit`；賣單不得低於 `SellPriceLimit`。達界線後維持原單並記錄 `PriceBoundaryReached`，不得將界線跟著行情移動。

價格邊界只限制完成單追價，不代替停損。固定停損觸發後使用獨立的受控平倉路徑；若平倉因行情或券商拒絕失敗，策略轉為 `Faulted`／風險警示並禁止新週期，不能為遵守完成單界線而保留未受控曝險。

驗收必須覆蓋：買賣初掛、A 選價、B/C 改掛、撤單重掛、Tick 取整、價差僅一跳、邊界恰好命中、越界候選及邊界凍結。

## 8. 週期狀態機

內部階段如下：

1. `WarmingUp`：收集完整方向窗。
2. `Ready`：資料健康但方向尚未成立。
3. `SubmittingPair`：同時產生買賣完成單意圖。
4. `WorkingBoth`：兩張單均在工作。
5. `WorkingOneLeg`：其中一側已成交，另一側繼續工作。
6. `CancellingForReprice`：等待指定舊單撤單確認。
7. `CancellingForExit`：停損、逾時或資料異常，等待剩餘單撤銷。
8. `Flattening`：平掉共用帳本中的淨部位。
9. `CoolingDown`：週期完成或風險復原後等待。
10. `Suspended`、`Stopped`、`Faulted`：對應共用生命週期。

兩邊都成交且共用帳本顯示淨部位為零後，記錄完整交易，進入 `CycleCooldownSeconds=45`。冷卻結束時清除舊方向樣本，重新取得完整 15 秒視窗，不立即沿用舊趨勢。

## 9. 停損與中性風險平倉

### 9.1 固定反向停損

只在單邊成交並形成淨部位後計算。基準是共用帳本實際加權成交均價；判斷使用可立即平倉的一側報價：

```text
LongAdverseTicks  = (AverageFillPrice - BestBid) / TickSize
ShortAdverseTicks = (BestAsk - AverageFillPrice) / TickSize
```

當不利幅度達 `StopLossTicks=6` 時：

1. 停止完成單改掛。
2. 要求撤銷另一張工作中完成單。
3. 等待撤單確認；若撤單競速成交，以最新淨部位為準。
4. 對剩餘淨部位發出 `PaperOrderRole.StopLoss` 平倉意圖。
5. 只有委託為零且部位為零後進入 `StopLossCooldownSeconds=60`。

停損價格在成交後凍結，不隨滾動方向或 OBI 移動。

### 9.2 單邊成交逾時

若單邊成交後超過 `MaxOneLegSeconds=30`，另一張仍未完全成交，依相同撤單確認流程平掉淨部位，但角色使用 `Flatten`，原因為 `OneLegTimeout`。時間退出不得計入固定停損。

### 9.3 資料健康退出

持倉後若 Book 不健康，不用不健康價格觸發固定停損。立即停止改掛並撤銷工作單；等待 `MarketDataRecoverySeconds=2`。期限內恢復則以健康報價重新評估；逾時仍未恢復，以 `Flatten` 執行中性風險平倉、進入 `ShockCooldownSeconds=45` 並暫停新週期。

### 9.4 單日虧損

沿用 `LiveStrategyService` 的每策略單日虧損限制。觸發時禁止新週期、撤銷策略所屬委託並依既有受控實盤流程處理部位；不得由本策略另建損益計算器。

## 10. 參數及安全範圍

| 參數 | 預設 | 安全範圍 |
|---|---:|---:|
| `DirectionWindowSeconds` | 15 | 2–300 秒 |
| `MinMoveTicks` | 2 | 0.5–50 ticks |
| `MinTrendEfficiency` | 0.40 | 0–1 |
| `MinDepthObi` | 0.10 | 0–1 |
| `ObiConfirmSeconds` | 3 | 1–30 秒，且不得大於方向窗 |
| `DirectionReverseSeconds` | 3 | 1–30 秒 |
| `MinSideDepth` | 827 | 1–1,000,000 |
| `MinNearDepth` | 165 | 1–1,000,000 |
| `MinDepthRatio` | 0.35 | 0–1 |
| `MaxSpreadTicks` | 2 | 1–20 ticks |
| `MaxVolatilityTicks` | 5 | 1–100 ticks |
| `QuoteFreshnessSeconds` | 2 | 0.1–30 秒 |
| `MaxChaseTicks` | 2 | 0–20 ticks |
| `StopLossTicks` | 6 | 1–100 ticks |
| `MaxOneLegSeconds` | 30 | 1–300 秒 |
| `CycleCooldownSeconds` | 45 | 0–3,600 秒 |
| `StopLossCooldownSeconds` | 60 | 0–3,600 秒 |
| `ShockCooldownSeconds` | 45 | 0–3,600 秒 |
| `MarketDataRecoverySeconds` | 2 | 0.1–30 秒 |
| `CommissionPerContract` | JPY 36 | 非負數 |
| `ResearchValidated` | false | boolean |

A/B/C 六項參數皆可設定；A、C 不得小於零，B 必須大於零，且各組 C 必須小於 B。實盤數量固定一口。

## 11. 暫停、停止與錯誤處理

- 空手暫停：停止訊號並撤銷尚未成交的兩張單。
- 持倉暫停：禁止新週期，但仍接收行情、成交及生命週期事件，保留停損與平倉能力。
- 關閉：停止訊號、撤銷本策略的工作單、等待確認，提示部位；依既有受控流程授權平倉。只有券商委託為零且部位為零時才成為 `Stopped`。
- 任一新單、撤單或平倉拒絕：保留可觀察狀態與原因；若仍有部位，升級風險警示並禁止新週期。
- 重複、順序錯誤或未知世代生命週期事件：不可改變正確的新世代狀態，並記錄穩定錯誤代碼。

## 12. 模擬、重播與實盤

- 模擬及重播沿用嚴格穿價成交；僅觸價不成交，成交價為委託價。
- 實盤由 `KgiLiveStrategyExecutionService` 處理券商委託，不由策略直接呼叫券商。
- 同時註冊 `StrategySimulationService` 與 `LiveStrategyService`。
- `StrategyCenterForm` 與紅色 `LiveStrategyCenterForm` 各新增專屬頁籤，包含固定寬度參數、啟動／暫停／繼續／關閉／刪除、狀態、雙邊單生命週期、成交紀錄及共用績效欄位。
- 實盤啟動必須符合明確確認、登入、熱門月份、`225MC` 白名單、一口限制、單日虧損限制及 `ResearchValidated=true`。
- 程式驗證只使用模擬、重播及受控假執行器，不送真實委託。

## 13. 可觀測性

擴充共用 `SimulationRunRecorder`，不得沿用 `JsonLineQueueFollowHistorySink` 作為此策略的權威紀錄器。必須輸出並關聯：

- `run-manifest.json`：完整有效參數、商品規格、輸入封存、版本與執行假設。
- `strategy-decisions.jsonl`：方向特徵、健康門檻、通過結果、主要及次要拒絕原因、週期 ID。
- `order-lifecycle.jsonl`：週期 ID、ClientOrderId、執行器／券商 ID、改掛世代、送出／確認／成交／撤單／拒絕時間。
- `executions.csv`：共用成交與角色。
- `run-summary.json`：訊號漏斗、方向／健康拒絕分布、雙邊與單邊成交、停損、逾時、MFE、MAE、持有時間、成本後績效及 0／1／2 跳反向滑價。
- `parameter-experiments.jsonl`：基準與候選、資料切分、搜尋空間、護欄及淘汰原因。

主要原因代碼至少包含：`WarmupIncomplete`、`NeutralDirection`、`StaleQuote`、`InvalidBook`、`InsufficientSideDepth`、`InsufficientNearDepth`、`DepthImbalance`、`SpreadTooWide`、`VolatilityTooHigh`、`SignalAccepted`、`SignalReversedBeforeFill`、`PriceBoundaryReached`、`OneLegTimeout`、`FixedStopLoss`、`MarketDataRecoveryTimeout`、`CoolingDown` 及 `DailyLossLocked`。

## 14. 策略分析入口

`analyze_periodic_queue_cycle.py` 支援 `logic`、`parameters`、`all`，並產生繁體中文 `analysis-report.md` 與機器可讀 `analysis-report.json`；兩組以上有效候選時產生 `parameter-ranking.csv`。

第一輪候選至少比較：

- `StopLossTicks`：4、6、8、10。
- `MaxOneLegSeconds`：15、30、45、60。
- `DirectionWindowSeconds`：10、15、20、30。
- `MinDepthObi`：0.05、0.10、0.15、0.20。
- `MaxChaseTicks`：1、2、3。

候選必須使用相同輸入封存、商品對應、費用與成交假設，分開報告訓練、驗證及樣本外結果。最低交易數、資料品質、最大回撤、成本後結果及 0／1／2 跳滑價是先決護欄；不得因交易筆數增加就選用較寬鬆參數。

## 15. 測試與驗收

### 15.1 單元與整合測試

- 所有參數的預設、解析、文化格式、邊界與交叉限制。
- 健康十檔、一秒取樣、過期／交叉／缺檔報價及完整暖機。
- 向上、向下、中性、低效率與 OBI 不一致。
- 向上／向下時趨勢與反方向 A/B/C 正確映射。
- 兩張單同時工作且非 OCO；任一側成交不會自動取消另一側。
- 單張撤單確認後才重掛；撤單成交競速、延遲舊世代事件及拒絕。
- 價格邊界的所有路徑與凍結語意。
- 兩邊完成、單邊逾時、固定停損、資料異常平倉及各自正確角色。
- 暫停時保護仍有效；安全關閉必須委託與部位皆為零。
- `225MC` TickSize=5、乘數=10、每口每次成交手續費預設 JPY 36。
- 共用帳本的完整交易、勝率、平均獲利／虧損點數與使用者定義獲利因子。

### 15.2 重播與研究驗收

- 相同封存、參數及假設輸出具決定性。
- 先排除資料／生命週期缺陷，再比較策略期望值。
- 基準與候選輸出完整訊號漏斗、拒絕原因、成交率、停損／逾時分布、淨損益、回撤及滑價壓力測試。
- Python 分析入口測試缺檔、損壞資料、不可比較資訊清單、資料切分洩漏、樣本不足與排名決定性。

### 15.3 建置與實盤邊界

- 執行聚焦冒煙測試及 Debug／Release 建置，區分既有警告與新增問題。
- 實盤程式碼及頁籤可建立、可受控啟動、可撤單及安全關閉。
- 沒有券商回報證據前，只能宣稱實盤能力已實作，不能宣稱真實委託已驗證。

## 16. 已知假設

- 目前方向及流動性數值來自有限的健康樣本，屬研究起始值，不是最佳化或實盤核准結果。
- `225MC` 是唯一可交易商品；`225`、`225M` 可保留為未來唯讀參考商品，但本版方向不依賴跨商品訊號。
- 一個策略執行個體同時只允許一個週期，數量固定一口，不處理加碼。
- 完成單追價邊界與風險平倉是不同控制；緊急平倉不得受完成單追價邊界阻擋。
