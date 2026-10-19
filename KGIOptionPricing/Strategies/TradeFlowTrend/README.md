# 內外盤順勢交易（TradeFlowTrend）

交易商品為 **225MC**，參考 **225M、225**。模擬策略中心與實盤策略中心均有「內外盤順勢交易」頁籤；本次驗證僅使用合成資料、封存行情與替身，未送實盤委託。

## 操作

1. 先在模擬策略中心開啟本頁籤，設定實例名稱、每口單邊費用與參數。
2. 「啟動」使用即時行情模擬；「歷史重播」選既有封存檔。實盤頁沿用既有帳號登入與逐次啟動確認。
3. 選取實例後，三張表、原因、有效參數及成交清單均只顯示該實例。缺值為 `--`，未滿歷史的 Avg／Max／Min 為暫估。
4. 「暫停」取消進場意圖，持倉保護仍運作。「關閉」撤進場單並依本策略 IOC／ROD 規則收尾，實際部位及未決委託歸零後才完成。等待成交期間需繼續接收報價與回報。
5. 「儲存參數組／載入參數組」支援 JSON；旁邊 `.history.jsonl` 保存前後值。執行中的參數固定，改值只影響下一次啟動，不會熱修改現有部位。

**目前必要條件：** 重連或已有歷史 checkpoint 的實例，因券商完整工作單查詢尚缺可信完成契約，會停在待對帳狀態；本版不提供自動恢復入口，不可用刪除紀錄或改實例名稱替代對帳。

## 三張觀察表

每秒顯示一次一致截面，三張表分別為 30／60／180 秒，欄位固定為 225、225M、225MC：

- Up：成交價 ≥ **成交前**有效 Ask1；Dn：成交價 ≤ 成交前有效 Bid1；有效價差中間成交為 Zero。
- Up／Zero／Dn 以各分類成交量除以有效分類總量，單位 `%`。缺報價、過期、鎖住或交叉報價的成交為 Unknown，不混入分母；未知比例另列，以全部成交量為分母。
- Real＝Up%−Dn%，採移動窗格，單位「百分點」。沒有有效成交時為缺值。
- 每 30／60／180 秒結算一筆固定區間 Ri，分別保留 60／30／10 格。Avg 為有效 Ri 的算術平均，Max／Min 使用同批格子；空格不以更舊資料補足。
- 自然時間邊界採左閉右開；啟動時的首個不完整區間不算有效 Ri。預設完整暖機約 30 分鐘，缺成交格或重連可能更久。

## 進出場

多單預設：225M 快線 > 慢線，且 225M、225 的 Real 均 > 0 並 > 各自 Avg。空單反向；相等不進場。採樣條件成立便送單，買掛 Bid1、賣掛 Ask1，買單只上追、賣單只下追。行情移動不是成交證明。

任一條件觸發便鎖定出場：固定停損、A 固定停利、B SAR、C 趨勢反轉、盤末或共用風控。C 只看均線與兩個參考商品的反向 Real，不要求 Avg 歷史。B 在可平倉浮盈達門檻後保持啟用，使用 225MC 真實成交聚合的已完成 K 線；有效 SAR 線穿越或方向反轉都會出場。SAR 過期時不使用舊線，固定停損與收尾仍有效。

| 價差 | 空單買回 | 多單賣出 |
|---|---|---|
| 小於 2 tick | Bid1 + 10 tick，限價 IOC | Ask1 − 10 tick，限價 IOC |
| 大於等於 2 tick | Bid1 + 1 tick，ROD | Ask1 − 1 tick，ROD |

ROD 需切換時先撤單，等終結回報的最終累計成交量與已交付成交一致，再依帳本剩餘口數下單。拒單、未知狀態或水位不齊會阻擋補單。同步平倉完成後，不使用同一回呼的訊號立即重開。

## 可調參數

參數名稱與設定值可在畫面直接編輯；未知名稱、無效範圍或快線不小於慢線會拒絕啟動。

| 參數 | 預設 | 意義／限制 |
|---|---|---|
| Quantity | 1 | 1～100 口；仍受共用風控限制 |
| AverageKind | Sma | Sma／Ema；EMA 以 N 筆 SMA 初始化 |
| FastSeconds／SlowSeconds | 30／120 | 1 秒採樣，週期各 1～3600 且快 < 慢 |
| History30／History60／History180 | 60／30／10 | 固定 Ri 格數，各 1～3600 |
| EntryWindowSeconds／ExitWindowSeconds | 30／30 | 30、60 或 180 |
| EntryDirectionThreshold | 0 | 方向門檻，0～100 百分點 |
| EntryAverageMargin | 0 | Real 與 Avg 差距，0～200 百分點 |
| ExitDirectionThreshold | 0 | C 反向門檻，0～100 百分點 |
| StopTicks／ProfitTicks | 6／50 | 固定停損／A 停利，各 1～10000 |
| SarActivationTicks／SarBarSeconds | 8／5 | 啟用浮盈／K 線秒數（1～300 秒） |
| SarStart／SarStep／SarMax | .02／.02／.20 | 正值、Start 與 Step 不大於 Max，Max ≤ 1 |
| SpreadSwitchTicks／IocOffsetTicks／RodOffsetTicks | 2／10／1 | ROD 偏移 > 0 且小於界線；IOC 偏移不少於界線 |
| DayEnabled／NightEnabled | True／True | 日夜盤分開啟閉 |
| DayStartTime／DayStopTime | 07:50／14:25 | 台灣時間；日盤收盤前 20 分鐘停止新倉並平倉 |
| NightStartTime／NightStopTime | 16:10／04:40 | 台灣時間、明確跨日；夜盤收盤前 20 分鐘收尾 |
| MaxBookAgeMs／MaxCrossBookSkewMs | 2000／2000 | 報價年齡／三商品時間差，各 100～60000 毫秒 |
| MaxTradeAgeSeconds | 5 | 秒採樣可使用的最近成交年齡，1～60 秒 |
| SarMaxAgeBars | 2 | SAR 最多保留 1～20 根週期的年齡 |
| RequireFullHistory | True | 新倉要求選用表完整有效 Ri 歷史 |

## 時段判斷

只以台灣時間的日盤／夜盤啟閉時間判斷是否允許新倉，**不檢核星期、假日或交易日曆**。因此設定時段內若有有效行情且其他進場條件成立，週末或休市日也可能產生送單意圖；實盤仍由券商決定是否接受。到各盤別設定的停止時間後，禁止新倉並持續平倉處理。

## 紀錄與分析

使用共用 `SimulationRunRecorder`，保留 `run-manifest.json`、`run-summary.json`、`strategy-decisions.jsonl`（秒特徵及固定 Ri）、`strategy-events.jsonl`、`order-lifecycle.jsonl`、`executions.csv`、`parameter-experiments.jsonl`。實盤紀錄在執行目錄的 `StrategyLiveRuns`，行情在 `StrategyLiveArchive`；未取得的指紋／交易所序號等欄位明確列為缺失。

勝率、平均獲利點、平均虧損點與使用者獲利因子來自共用帳本的成本後完整交易。獲利因子＝平均獲利點 ÷ 平均虧損點絕對值；不是總獲利除以總虧損。無完整樣本或分母不存在時為 `--`。

在工作區根目錄執行：

```powershell
dotnet run --project tests/TradeFlowTrendSmokeTests
python -m unittest discover -s KGIOptionPricing/Strategies/TradeFlowTrend/analysis
python KGIOptionPricing/Strategies/TradeFlowTrend/analysis/analyze_trade_flow_trend.py --runs <執行紀錄根目錄> --output <獨立分析目錄> --mode all
```

分析只讀取紀錄，不自行回測或修改正式參數。支援 logic／parameters／all，產出 JSON 與 Markdown；僅在至少兩個候選來源、商品規格、費用、執行假設、資料切分、事前目標及護欄一致且通過品質檢查時排名。共用實驗紀錄可存 `DataSplits` 與 `SplitResults`；缺少訓練／驗證／樣本外結果便不排名。重跑若不符條件會移除該分析目錄中的舊排名 CSV。

模擬 IOC 只消耗一次已知第一檔量，以限價作保守成交價；同一份行情不足以重複提供流動性，剩餘量等待新 Book。ROD 沿用共用嚴格穿價模型。這些結果不能代表真實排隊、網路延遲或滑價。

本次交付證據、限制及審查修正見 `docs/superpowers/evidence/2026-09-27-trade-flow-trend.md`。
