# 225MC 強化學習回測與 C# 模型

這是離線研究工具，不會連接或送出券商委託。Python 只訓練策略，共用 C# paper-strict-cross 執行器／PaperPortfolioLedger 是成交與績效權威。

## 組件

- `NikkeiRlReplayBridge`：讀取已封存資料、因果 3／5 秒特徵快取、Gymnasium reset/step、動作遮罩、共用執行／帳本與逐步 reward。
- `NikkeiRlModel`：**獨立 C# 推論函式庫**，不依賴主程式或交易 API。模型為 40 維輸入、5 個動作的 ONNX actor，已內嵌訓練 normalizer。
- `KGIOptionPricing/Strategies/NikkeiRlDirection/analysis/`：MaskablePPO 訓練、完整逐步回饋與時間延遲／成本情境。

本機已有 Release 主程式 DLL；bridge 引用它以免複製帳本或拉入 UI 的大型套件。全新 checkout 先建置主程式 Release。此 DLL 的版本／hash 必須與回測實驗清單一致。正式接入主程式前仍需新的策略工廠、UI 與生命週期驗收；本工具不代表已完成該整合。

## 建置與資料

```powershell
dotnet restore tests/NikkeiRlDirectionSmokeTests/NikkeiRlDirectionSmokeTests.csproj --packages .rl-ort-packages --source .rl-nuget -p:NuGetAudit=false -p:RestoreFallbackFolders=C:/Users/user/.nuget/packages
dotnet build tests/NikkeiRlDirectionSmokeTests/NikkeiRlDirectionSmokeTests.csproj --no-restore -c Release -p:UseSharedCompilation=false
dotnet tests/NikkeiRlDirectionSmokeTests/bin/Release/net10.0-windows/NikkeiRlDirectionSmokeTests.dll
dotnet tools/NikkeiRlReplayBridge/bin/Release/net10.0-windows/NikkeiRlReplayBridge.dll cache INPUT.jsonl OUTPUT.br day
```

本環境 NuGet TLS 有認證問題，使用工作區 `.rl-nuget` 小型來源及既有快取作 fallback。`native/onnxruntime.dll` 是官方 Microsoft.ML.OnnxRuntime 1.23.2 套件的 Windows x64 DLL；不需要其他平台 runtime。一般開發機可正常 restore 官方來源，並使用同版本 native runtime。

Cache schema 為 nikkei-rl-cache-v2，manifest 記錄原始檔／cache SHA-256、品質、原始行情缺口及特徵計算 p99；原始資料不修改。已存在輸出不覆寫。每盤決策時點只使用已收到資料，60 秒暖機；跨盤／換約不可拼接。

dataset.json 是 episode ID → cache 絕對路徑的 JSON 字典。訓練／驗證以交易日分組，日夜盤同日不能跨切分。第一輪僅使用兩個已研究日期的**日盤**，不是 pristine holdout。

```powershell
.\.venv-rl\Scripts\python.exe KGIOptionPricing/Strategies/NikkeiRlDirection/analysis/train_rl_direction.py --bridge tools/NikkeiRlReplayBridge/bin/Release/net10.0-windows/NikkeiRlReplayBridge.dll --dataset docs/superpowers/evidence/nikkei-rl-20261007/dataset-postgap.json --train 20260928-postgap-day --validation 20260929-day --output YOUR_NEW_OUTPUT --training-latency-ms 0 --steps 20480 --seeds 11 22 33 44 55 --latencies 0 100 250 --costs 0 1 2
```

PPO 在目前策略的重播軌跡上更新；舊策略回饋不直接作新 PPO buffer。訓練遇到缺口或未完整 episode 立即停止，不以 bootstrapping 略過。驗證保留未平倉結果並標 Incomplete。

## C# 使用

其他 C# 程式可引用 `tools/NikkeiRlModel/NikkeiRlModel.csproj`，或發行目錄中的 `NikkeiRlModel.dll`，連同 managed ONNX runtime、System.Numerics.Tensors 與 Windows x64 native runtime。模型 manifest 提供完整 hash／輸入／動作契約。

```csharp
using NikkeiRl;
using var model = new RlPolicyPredictor("policy-seed11.onnx", "policy-seed11.manifest.json");
// 必須使用同一 schema 的 40 維因果狀態與固定風控產生的合法動作遮罩。
RlPolicyPrediction result = model.Predict(observation, actionMask);
int action = result.Action;
double inferenceMilliseconds = result.InferenceMs;
```

輸入順序：225MC、225M、225 各 10 項（5/15/30/60 秒價格變化、價差、OBI1/5、報價年齡、log(1+5秒成交量)、5秒成交量加權 uptick/downtick），價格變化與價差皆以 5 指數點作單位；再加部位、持有秒數/60、未實現/淨損益/回撤跳值、冷卻剩餘秒數/30、進場待處理、平倉待處理、資料品質、距固定盤別收盤小時數。不能使用指標預測未來資料，也不能把 policy probability 當勝率。

```powershell
dotnet tools/NikkeiRlReplayBridge/bin/Release/net10.0-windows/NikkeiRlReplayBridge.dll benchmark MODEL.onnx MODEL.manifest.json parity-seed11.json latency-seed11.json
```

Benchmark 比對至少 1000 個真實 observation 的 Python／C# 動作及機率，並量測 cold load、warm p50／p95／p99。推論計時包含 embedded normalization、ONNX、遮罩／softmax，**不包含行情接收、資料排程、特徵更新與券商／網路時間**。Cache 特徵計算時間另記；兩者的 p99 相加只可作保守預算，不是端到端 p99 實測。

## 判斷耗時與成交誤差

每 5 秒決策是策略節奏，不是模型推論耗時。回測的 DecisionLatencyMs 移動完整動作可送出時間；撤單也受延遲，延遲平倉期間保留原停損。成交只使用送出之後行情，因此可觀察漏成交、進出場變化與損益差異。

每邊 0/1/2 跳是**額外成本情境**，透過共用執行費用一次記帳；不是完整 queue／price-slippage 模型。基礎嚴格穿價模型在委託價成交，市價在後續成交價成交，尚不模擬排隊、市場衝擊與真實網路抖動。100/250 ms 延遲是情境假設，不能冒充量測值。

收盤管理按事先固定日盤 14:45／夜盤 05:00（台北時間），在收盤前 310 秒，也就是連續交易結束前 10 秒（14:39:50／04:54:50），禁止進場並送出平倉；本次正確版本結果存於 `run-regularclose`，資料 EOF 不預告給 actor；檔案提前結束不會虛構平倉。假日／特殊收盤需後續納入正式交易日曆。

## 回饋產物與限制

每個 seed：PPO checkpoint、normalizer、ONNX／manifest、1200 個 parity 快照。每個驗證情境：gzip JSONL 保存完整 observation、mask、動作、下一狀態、reward 分量、成交與成本。摘要分開 shaped reward 與共用帳本淨結果。

首輪設定為 9 月 28 日中斷後的連續區段訓練、9 月 29 日開發驗證，每 seed 20480 步。9 月 28 日 10:00:40／45 有原始事件中斷，訓練保留 10:01:45 之後區段，等待完整 60 秒特徵視窗；衍生 cache 記錄來源 hash、切分時間與排除原因。10 月 1 日有 109 筆時間倒序，整日排除。衍生 cache 的特徵計算耗時為未知值。模型產生可用檔案不代表策略有優勢；結果不符合 20 日／200 筆、全新樣本外、多 seed、回撤預算與實盤授權門檻，始終 deploymentEligible=false。本輪未建立 UI 或自動模型換版。



## 固定 1 ms 與績效報告

回測 RlConfig 的交易動作延遲預設為 1 ms。凍結模型評估可指定 --latency-ms 1；實測推論時間只供比較，不再決定此設定。既有模型原訓練延遲為 0 ms，上方命令明確保留此條件；新訓練 CLI 的 --training-latency-ms 預設為 1。

run-regularclose/performance-1ms.json 與 CSV 提供勝率、毛損益、成本、淨損益、完整進出次數、扣成本後平均獲利／損失，以及完整參數；research-report.md 以此 1 ms 表格為主。measured-latency-backtest.json 檔名為相容舊版保留，其內容已改為使用者明確固定的 1 ms 情境。

## 每日目標與模型改進

每日門檻為完整進出交易 >100 筆、淨獲利交易勝率 >33%，初步毛損益 >0，進階另須淨損益 >0；部分日盤不能作達標證據。報告逐日判斷，不用多日平均取代每日門檻。

`nikkei-rl-improvement-20261008` 證據目錄保存三種候選、各三個 seed、各 102400 PPO 步的跨日實驗。A 增加訓練日期與步數；B 另以 `--actor-excluded-features 33 34` 遮罩累積淨損益／回撤，actor 和 critic 使用相同輸入，ONNX 內嵌遮罩；C 以 `--reward-cost-weight 0` 訓練毛權益差 reward，淨權益回撤懲罰仍在。C 的真實帳本與報告仍扣每邊 36 JPY，不能將訓練 reward 當作淨損益。

凍結評估入口會還原模型的遮罩與 reward 設定，舊模型缺省為無遮罩、成本權重 1。`report_rl_improvement.py` 產生日別四項績效、平均獲利／損失、參數與門檻旗標，並稽核回饋帳本及 C# parity。本輪 18 組候選日別結果皆未同時通過初步門檻，模型不予採用；10/6、10/7 是已檢視的開發驗證日期。

## 方向預訓練後再學習交易回饋

`--direction-warmstart-epochs 20` 先以訓練日 wait-only 軌跡預訓練 actor。起點 40 維因果 observation 作輸入，30 秒後第 2 維回報（最後成交價差，以 5 點為單位）作標籤；超過正負 2 跳者標為多／空，其餘等待。起點到終點均須資料有效，進場合法且不在收盤緩衝。標籤是方向代理，不是限價單可成交獲利；critic 與完整進出由 PPO 的真實 C# 回饋學習。

direction-training.npz／JSON 保留訓練輸入、類別分布及來源 hash；normalizer 僅擬合訓練輸入。`environmentConfig` 保存冷卻、延遲及 reward 設定，凍結入口從 manifest 還原。C# observation 順序、normalization 與 action mask 契約不變；冷卻 observation 仍以剩餘秒數除以固定 30 秒，即使候選冷卻設定為 5 秒。

```powershell
.\.venv-rl\Scripts\python.exe KGIOptionPricing/Strategies/NikkeiRlDirection/analysis/train_rl_direction.py --bridge tools/NikkeiRlReplayBridge/bin/Release/net10.0-windows/NikkeiRlReplayBridge.dll --dataset docs/superpowers/evidence/nikkei-rl-warmstart-20261008/dataset.json --train 20260928-postgap-day 20260929-day 20261005-postgap-day --validation 20261006-day 20261007-day --output YOUR_NEW_OUTPUT --steps 102400 --seeds 11 22 33 --latencies 1 --costs 0 --training-latency-ms 1 --reward-cost-weight 0 --direction-warmstart-epochs 20 --cooldown-seconds 30
```

D 使用冷卻 30 秒，E 唯一差異為 `--cooldown-seconds 5`。`report_rl_warmstart.py` 與上一輪 C 比較相同完整日盤，另外按真實成交拆出停損／一般平倉損益，完整核對已平倉帳本；成果保存在 nikkei-rl-warmstart-20261008 證據目錄。

使用者後續固定冷卻時間為 10 秒：新的 RlConfig 與訓練 CLI 預設皆為 10 秒，後續特徵實驗保持此設定。歷史 D／E 的 30／5 秒設定及原報告保留；缺少 environmentConfig 的更舊模型明確還原原本 30 秒，避免默默改變 frozen 回測。冷卻 observation 的固定 /30 正規化契約保持相容。



## 因果 50 維特徵與固定 10 秒冷卻

新模型 manifest 使用 `nikkei-rl-v2`／`observationSize=50`；原 `nikkei-rl-v1` 40 維仍可載入。ONNX 已內嵌訓練 normalizer 與特徵遮罩。C# `RlPolicyPredictor` 可在同一 consumer 選擇兩種 schema，但必須輸入 manifest 對應長度；不能把 50 維直接交給舊模型。

`RlFeatureAccumulator` 以 receive time／sequence 處理三商品同到期月報價及成交，產出 8 維市場特徵；環境另外加入持倉至當下 MFE／MAE 2 維。前 40 維保持相容。報價分類成交量要求完整五檔、價差不超過 10 點且報價年齡不超過 2 秒，未知方向不硬分類。所有新模型冷卻 10 秒、交易延遲 1 ms。

`cache-v3 VERIFIED_PARENT_CACHE OUTPUT` 重新讀原 raw 資料，不從舊 30 維補造 OFI。原 frame／30 維／trade 全等且品質通過後，manifest 才標記 `legacyParityVerified=true`；serve 拒絕沒有成功標記或 parent SHA 的 v3。新資料不覆寫舊 cache。

完整逐組消融位於 `docs/superpowers/evidence/nikkei-rl-features-20261008`：B0 原 40 維；B1 新增 10 維全部遮罩；F1 OFI、F2 領先殘差、F3 成交量／波動／價差、F4 MFE／MAE。新版五組位於 `verified/`，根目錄同名初版實驗與 `cache/` 已中止、僅保留溯源。`report_rl_features.py --root ...` 只接受完整 18 模型／36 日別結果並核對三方會計，不把跨日合計視為每日達標。

benchmark 報告提供 actor p99／最大值及超過 1 ms 的次數；不含行情特徵、網路與 broker，固定 1 ms 回測不是端到端時延保證。凍結跨日評估可使用 `evaluate_multiday_rl_direction.py --latency-ms 1`，額外敏感度另設 output 目錄與明確延遲，不更改主回測設定。


## 論文特徵淘汰與新 48 維契約

新候選 `nikkei-rl-v3` 輸入 48 維：原 40 維＋第二至第五檔 MLOFI 4 維＋五檔平均 bid/ask 深度及 5 秒變化率 4 維。上一輪未通過研究門檻的 10 個增量不在新輸入中，舊40／50維模型仍保留凍結相容。`PaperFeatureAccumulator` 在有效且新鮮的已收到報價上計算；超過2秒後恢復時不計跨失效期間新flow。

`cache-v4 VERIFIED_V2_PARENT OUTPUT` 重建 raw 並逐一核對舊30維、frames、trades；成功 parity 標記才可serve。市場paper輸入置於 PaperExtras，舊MarketExtras空。manifest 另外提供原有效frames中，當下新深度報價健康與5秒歷史anchor可用的計數；zero值可能是無變化或缺少anchor，不能用非零比率替代可用率。

論文來源為 https://arxiv.org/abs/1907.06230 equations9-12 與 https://arxiv.org/abs/1705.03233 Table4 u4/u6；深度正規化、五檔與有限差分為本專案移植調整，沒有採用原論文命中率或偽造真實撤單事件。C0為同48維全遮罩控制，P1只開MLOFI，P2只開平均深度/導數。

`report_rl_features.py --suite papers --root docs/superpowers/evidence/nikkei-rl-papers-20261008` 核對12模型／24日別結果、SHA-bound parity與三方帳本。`report_feature_latency.py --suite papers` 對P1凍結1ms同結果及另10ms敏感度作正式稽核，主報告沒有此證據會拒絕完成。新實驗只用cache-verified與models目錄；cache/及models-superseded只作溯源。

## 預測能力篩選與56維契約

`nikkei-rl-v4` 接受56維：原40維、paper8維、`PredictiveFeatureTransform.Compute(market30)`8維。C#重播與Python訓練使用相同順序；既有40/48/50維模型仍使用原契約。新特徵由當下市場快照計算，不改寫原cache-v4。

`predictive_pruning.py --bridge BRIDGE --dataset DATASET --output FRESH_OUTPUT` 只在訓練日期內做兩折walk-forward、5s/30s方向探針，輸出每特徵log-loss與block-bootstrap診斷、固定遮罩。輸入feature9歷史名稱為uptickVolumeRatio5s，但實際為signed-volume ratio，交互項直接使用[-1,1]值。方向標籤不是成交報酬。

刪除以正規化前遮罩實作，actor、critic與ONNX一致；十個部位/風控狀態保留。56維交換資料仍完整，歷史證據不刪除。這是探索性的操作性篩選，不能斷言未通過者在所有模型或時距都無預測能力。

`report_predictive_pruning.py --root docs/superpowers/evidence/nikkei-rl-pruning-20261008` 強制核對預先凍結的C0/R/N遮罩、12模型24主日別、48額外成本日別、N及N5凍結1/10ms的24日別、全部feedback/fills/ledger、cache/raw/source/runtime/model SHA和1200筆C#parity。缺少延遲或成本證據不算完成。

## 3秒判斷／15秒預測（2026-10-08）

新train_rl_direction.py預設`--decision-interval-seconds 3 --prediction-horizon-seconds 15`，交易延遲仍1ms、冷卻10秒。歷史5秒模型manifest未包含decisionIntervalSeconds時，還原5秒；不可把舊ONNX直接換頻率。重跑舊訓練需明確指定`--decision-interval-seconds 5 --prediction-horizon-seconds 30`。

`cache INPUT OUTPUT day 3`重建3秒快取；`cache-evaluation INPUT OUTPUT_DIRECTORY 3`重建並分段七日驗證。舊CLI省略最後參數仍為5秒以保留相容性。特徵期間保持5/15/30/60秒，新3秒快取按原始成交時間、嚴格早於邊界計算報酬，避免5秒窗口退到6秒。

`training-segment VERIFIED_EVALUATION_SEGMENT OUTPUT`只接受通過source/cache hash與資料門檻、連續且到收盤的片段；仍禁止資料中斷episode投入PPO。`counterfactual-labels-3s15s TRAIN_DATASET OUTPUT_JSONL TRAIN_EPISODES...`產生15秒平倉意圖的可執行淨損益標籤。資料／模型／標籤manifest記錄頻率與期間；不一致拒絕執行。15秒是預測／標籤期間，PPO持倉硬上限仍60秒。

PPO gamma=0.99^(3/5)、GAE=0.95^(3/5)，維持原5秒的時間衰減。每模型102400步的總市場回放時間與舊5秒不同；不能把結果差異全歸因於頻率。`report_three_second.py --root docs/superpowers/evidence/nikkei-rl-3s15s-20261008`提供凍結C#七日重播、成本壓力、3600筆ONNX parity、逐筆帳本稽核與每日報表。
