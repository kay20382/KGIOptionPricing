# 每日新增行情與因子庫迭代

2026-10-09 18:08 CURRENT：每日根為 C槽 nikkei-rl-daily-backfill-final-20261009；不可變來源為 nikkei-rl-daily-backfill-final-source-frozen-20261009/incremental_daily_backtest.py。catalog及歷史採 nikkei-rl-pruned-earlier-training-20261009/model-catalog.json、actor-performance/objective-report.json。排程ACTIVE及實際來源路徑已獨立核對；43項Python契約通過。預設wall，execution-clock.json釘選，同根禁止切換時間軸，日期結果及完整性索引SHA均核對。

10/1已實際補回70 actor、三成本210列；actor日績效共2004列，全研究111配置2988列，共17個已研究日期，舊模型歷史日期數不同，並非17個完整日。10/1使用獨立monotonic研究時間軸：原系統時間約0.98秒倒退，接收Monotonic與Sequence無倒退，原來源及OriginalTime保留；絕對UTC精度未驗證，不宣稱1ms全流程或新樣本外。最新actor10/1為70筆、淨勝率48.57%、毛損益-5250JPY、淨損益-10290JPY、每筆毛期望-75JPY。指定10/6–10/8仍不變，合格0個、selectedCandidate空值，最高研究quality80仍45.16JPY/筆且交易數未達標。

本次18:08排程實際執行完成，10/9兩個來源檔中行情仍在寫入，品質檢查拒絕，未加入10/9績效，未重訓。10/1來源未變更，既有結果與coverage指紋核對保留。報告：nikkei-rl-daily-backfill-final-20261009/daily-performance.md及daily-performance.csv；補回說明：nikkei-rl-backfill-performance-20261009/backfill-performance.md。以下文字依時間保留為歷史紀錄，舊CURRENT路徑與數量不可覆蓋本段及排程指示。

2026-10-09 CURRENT：12時段pruned-earlier seed22實際131072步續訓、12時段全參與16個完成回饋episode；1032筆Python/C#動作一致、最大機率誤差4.06e-7；暖機推論p99 .0847ms、max1.2638ms、1筆超過1ms，完整流程1ms未驗證。16日三成本48列已完成，70actor及41既有情境共111配置2778日績效列，actor1794列，0合格，selectedCandidate空值，最高研究quality80不變。指定三日71/43/66筆、淨勝率50.70/41.86/53.03%、毛期望-54.23/-100/-22.73JPY；三日毛-9650、淨-22610JPY，未達標。

每日CURRENT根：C槽nikkei-rl-daily-pruned-earlier-20261009；不可變來源nikkei-rl-daily-pruned-earlier-source-frozen-20261009/incremental_daily_backtest.py；catalog與actor歷史報告採nikkei-rl-pruned-earlier-training-20261009/model-catalog.json及actor-performance/objective-report.json。排程ACTIVE、新每日所有指紋與registry保留已獨立核對；空發現遷移不是新增日期回測。新actor訓練日期標示TrainingDateReplay，指定三日仍為已檢視開發。實際10/9原始檔仍498102090bytes、最後更新09:40:25，不能當完整日。完整16日逐日報表nikkei-rl-pruned-earlier-training-20261009/iteration-performance.md。以下依時間保留的狀態是歷史，以本段與排程為準。

2026-10-09 CURRENT：新actor實際131072步訓練及1032筆Python/C#比對已完成，16日三成本48列，69actor及41既有情境共110配置2730列，仍無合格。新actor三日59／57／80筆、淨勝率45.76／47.37／42.50%，毛期望均負。審查發現的cache兼容性已修正：exclude-flow只接受nikkei-rl-cache-pruned-flow-v1、-evaluation、-derived；新runtime為C槽nikkei-rl-cache-isolation-20261009/runtime/ReadinessBridge.dll。162項C#／31項Python契約及真實舊binary拒絕、新binary接受、20份內容相同、三日帳本完全一致驗證通過。

每日新根為C槽nikkei-rl-daily-cache-isolation-20261009，來源nikkei-rl-daily-cache-isolation-source-frozen-20261009/incremental_daily_backtest.py；catalog與歷史採nikkei-rl-pruned-readiness-iteration-v2-20261009/model-catalog.json及actor-performance/objective-report.json。排程ACTIVE、293指紋核對通過，registry保持；空發現目錄遷移不代表新增日回測。12組新停損研究未重訓、不加入69actor池；固定50點16日三成本已完成，指定日309筆、每筆毛期望−43.20JPY，仍未合格。完整逐日報告nikkei-rl-pruned-stop-extended-20261009/iteration-performance.md。最高actor研究起點quality80不變。分鐘因子56／57／59完成5項聚合因果檢查及6項公式檢查，原四訓練時段篩選正在執行，沒有新actor或預測能力結論。以下依時間保留的狀態與來源是歷史，以本段及排程為準。

2026-10-09 新就緒模式16日評估快取已全部驗收，原完整日／缺口分類保留，暫存抽取副本刪除；原四訓練時段市場輸入及成交事件一致。修正版quality80 seed22延續131072步已實際啟動，仍在執行；原四時段、同遮罩、相同風險費用及Greedy訓練回饋不變。尚無新模型績效、1032筆新actor比對或合格结論。新版每日兩模式快取路由13項測試及兩模式三成本實際流程測試通過，但未更新原68actor排程；不可變每日264項指紋未變。摘要 `nikkei-rl-pruned-training-start-20261009/training-start-report.md`，下文重建未完成／尚未啟動的文字屬較早階段。

2026-10-09 原四訓練時段exclude-flow快取重建與核對完成：原輸入SHA及時段起點／長度相同，29,363快照、1,468,150項市場模型輸入誤差≤1e−9，因果成交事件相同，原有效快照均保留；有效快照11,770→27,983，不是新增交易或獲利。使用原標準化行情，C#仍過濾兩商品日盤，不使用大型日經。16日評估快取正在重建，已有9/15及9/16驗收；完整日／缺口分类保持。首次訓練啟動因CLI空評估清單在解析時退出，尚未訓練；修正版保留原控制組評估參數，等16日驗收完成才啟動，輸出改用獨立v2根。摘要 `nikkei-rl-pruned-cache-progress-20261009/cache-progress-report.md`，每日264項不可變指紋未變。沒有新增actor或合格模型。

2026-10-09 Python就緒轉移進度：每日排程先遷移至C槽 `nikkei-rl-daily-source-frozen-20261009/incremental_daily_backtest.py`，每日根改為 `nikkei-rl-daily-immutable-source-20261009`，catalog及歷史仍採原68actor來源。遷移保留同一registry與JSON／CSV績效，264個新每日指紋未變，排程ACTIVE。遷移驗證使用空發現目錄，不是新增日期回測。原每日根保留，不能搭配已修改的工作區Python再執行，舊指紋未覆寫。明確readiness transfer11項及compact6項測試通過；實際quality80原四時段轉移檢查與原三日31筆彙總通過。正在重建原四時段原始行情快取，尚未完成重訓及16日三成本評估，沒有新增候選或合格。摘要 `nikkei-rl-python-readiness-20261009/progress-report.md`；下文舊排程路徑屬歷史，以本段及排程明確來源為準。

2026-10-09 C#快取及模型契約串接完成：新readiness profile須與模型／快取相符，只有新schema及完整48–53遮罩才可使用exclude-flow，舊模型缺欄位仍為all-research。157項檢查通過；指定三日quality80在新版與凍結執行器所有帳本摘要欄位完全相同。未重訓actor的schema相容性測試1000筆動作／機率一致，舊讀取器拒絕新schema。摘要在C槽 `nikkei-rl-readiness-contract-20261009/contract-report.md`。**尚未完成Python轉移、原始行情新模式快取重建、重訓及16日三成本驗收；不是新增候選績效。** 68actor／109配置及無合格結果不變。下段所述「尚未接入快取／模型」是前階段的歷史狀態，現在僅C#契約完成，其餘待做。

2026-10-09 暖機依賴診斷完成：9/17、9/18、9/21原始行情與凍結快取逐值比對共503,940項，零誤差；首個健康快照之後共12,748個時點，僅受模型已停用的六個委託流輸入暖機條件阻擋。C#新增依啟用因子判斷的就緒介面，28項檢查通過，仍保留報價新鮮度、30秒連續價格路徑及事件更新失效保護。**尚未接入快取／模型／訓練／回測，這些時點不等於交易或新增獲利。** 舊實驗180檔與每日264檔凍結輸入重查未變；68個actor及109配置的績效不變，仍無合格。摘要在C槽 `nikkei-rl-readiness-audit-20261009/readiness-report.md`，串接計畫為 `docs/superpowers/plans/2026-10-09-nikkei-active-factor-readiness.md`。附件CSV共19日、102,008,789筆，16日起迄涵蓋日盤但不能證明連續；9/14缺開盤、10/2缺收盤、10/9清單僅到09:40，另9/28、10/5已知內部缺口。保留部分日及缺口標記，不以完整起迄代替逐事件驗證。

2026-10-09 原4對12訓練時段比較完成：最新每日凍結根目錄為 C 槽 `nikkei-rl-daily-compact-twelve-training-20261009`，catalog／actor歷史採 `nikkei-rl-twelve-training-iteration-20261009/model-catalog.json`、`actor-performance/objective-report.json`。68個actor及另41情境共109配置、2,682列日績效；只有新兩actor各完成16日×3成本。控制組與擴充組各131,072步，指定三日159／215筆、每筆毛期望15.72／−32.09JPY，兩者均未過關；quality80仍45.16JPY／筆、三日31筆，未達全部門檻。12個訓練時段均實際參與回饋，指定開發驗證日未進訓練。2,064筆Python／C#動作一致，控制組1/1032筆actor耗時超過1ms，全流程延遲未驗證。新的六個較早完整日9/17、9/18、9/21–9/24來源SHA及C#連續快取皆通過，原始行情不改寫、暫存副本刪除。大量無效時點仍具有完整30秒價格路徑及正常價差／報價年齡，需直接追蹤OFI等暖機依賴，不為交易數放寬健康門檻。完整報告 `nikkei-rl-twelve-training-iteration-20261009/iteration-performance.md`，逐日CSV在同根目錄performance資料夾。以下較早版本路徑與數量屬歷史，以本段及排程明確參數為準。

2026-10-09 新增51–54的工程版本已完成Python計算與增量檢驗：四個訓練episode、11,761個C#可執行15秒標籤、六組單项／合併配置均未通過，未接入RL或增加actor。成交覆蓋率54兩折改善，但平均0.002571低於0.005門檻，180秒區塊90%下界−0.000134。53輕量版與原已完成單項結果逐折一致。彙總報告位於C槽 `nikkei-rl-new-factor-screen-20261009/iteration-report.md`，包含指定三日完整績效欄位；原始因子庫快照保留以核對本輪來源SHA。

2026-10-09 附件因子庫入口為 [因子庫說明](../factor-library/README.md) 及 [機器可讀定義](../factor-library/research-library.json)。共61項候選（原50＋新增11），另列2項條件候選。附件10頁349列原始規格、來源、限制均保存；既有代理與新工程公式／篩選證據分開記錄。51–54僅有研究計算，55–61尚未實作，均未接入60維模型。已核實四個訓練日原始depth更新均保存10檔，現有模型／快取只用前5檔；242,487個可比較TradeQty均與累計量增量相符。10檔是否完整仍須逐日核對，不能把数组長度視為每個價位都有效。

2026-10-09 六episode重訓完成：最新每日排程根目錄為 C 槽 `nikkei-rl-daily-compact-earlier-training-20261009`，catalog與actor歷史報告改用同層 `nikkei-rl-earlier-training-iteration-20261009/model-catalog.json`、`actor-performance/objective-report.json`。共66個actor候選，包含績效重現控制組；另41組敏感度不混入每日catalog。控制組四episode、新組加入9/15及9/16，各延續65,536步；指定三日109／200筆、每筆毛期望−16.97／−34.00JPY，仍無合格、不替代quality80。新兩actor的2,064筆Python／C#動作一致，原控制組336項日績效欄位重現。較早日期仍屬訓練或回溯研究，10/6–10/8原門檻不變；新增完整未來凍結日另逐日驗收。以下較早版本的模型數量、路徑及未完成訓練狀態屬歷史說明，以本段與排程明確參數為準。

2026-10-09 最新凍結版本：每日排程使用 C 槽的 `nikkei-rl-daily-compact-entry-trained-20261009`，搭配同層 `nikkei-rl-entry-trained-20261009/model-catalog.json` 的64個模型及 `actor-performance/objective-report.json`。以下較早版本的31個候選與預設路徑屬歷史說明；目前排程會明確傳入最新 `--output`、`--catalog`、`--historical-report`。

執行器為 `nikkei-rl-entry-mode-20261009/runtime/CompactBridge.dll`，按每個模型manifest的 `environmentConfig.entryPriceMode` 回測；缺欄位維持 `same-side`。新模型 `entry-trained-opposite` 使用 `opposite-touch`，兩者均為嚴格穿價限價單，碰價不成交。64個actor、40組風險敏感度及1組未重訓进場情境共105配置，指定完整日仍無合格。每日catalog只包含64個actor；另41組敏感度不混入模型清單。最高研究起點仍為 `quality80 seed22`，每筆樣本毛期望45.16JPY，三日31筆，未達全部每日門檻。

成交間隔變異係數21、120秒跳躍變異代理34、120秒價格偏態36已用四個訓練episode做兩折增量預測檢查，缺值不補零，候選與基準使用完全相同有效樣本。四組（單獨三項及合併）皆未改善，暫不接入模型；這是預測診斷，不能冒充交易回測或C#部署特徵。摘要位於 C 槽 `nikkei-rl-cadence-screen-v2-20261009/screening-report.md`。

2026-10-09 儲存政策更新：增量回測預設輸出移至 C 槽的 `C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-rl-daily-compact-20261009`。共用 C# 摘要執行器在每步內部核對帳本，只輸出績效、訊號漏斗及耗時彙總，不保存逐筆回饋、決策或成交明細。來源行情保留；抽取的 part 與合併行情副本處理完即刪除，保存 SHA、來源路徑、二進位回測快取、模型及績效報表供重現。舊版 3,414 個明細及兩份同內容副本已依使用者要求清除；舊報告中的稽核步數代表當時已完成的驗證，並非目前仍有明細檔。

摘要執行器已在八天同一模型／基本成本情境與歷史已稽核績效比較，14 項日績效一致，輸出約 12 KB。改版必須建立新的輸出根目錄，不覆寫舊凍結指紋。下文舊版的 E 槽輸出與明細保存說明僅供理解既有歷史版本，新增工作以本段為準。

從工作區根目錄執行：

```powershell
.venv-rl/Scripts/python.exe KGIOptionPricing/Strategies/NikkeiRlDirection/analysis/incremental_daily_backtest.py
```

行情來源預設 `KGIOptionPricing/bin/Debug/每日執行檔/NikkeiResearch`，讀取 `YYYYMMDD_nikkei_*.jsonl`，自動發現晚於原七日報告最後日期的所有新增日期，不需要修改日期清單。不同執行目錄可用 `--recordings <目錄1> <目錄2>` 指定；同一日期重疊檔案會拒絕，不猜測去重。

硬性目標：每個完整驗證日完成交易≥50筆、淨勝率≥40%、每筆期望毛損益≥50JPY。每筆期望毛損益＝當日已完成交易毛損益÷筆數；零交易為無法計算。平均獲利／損失採扣成本後交易。淨損益仍揭露，淨損益大於零不是此次新增的硬性條件。`constrained_objective.py`是單一目標定義；單一模型／種子每個指定完整驗證日均過關後，才依全部驗證交易的毛損益÷總筆數排序。缺日與部分日不能充當合格證據，不合併種子、不用多日平均掩蓋違規。`selectedCandidate`只選合格者；`highestGrossExpectancyResearchCandidate`另列毛期望最高且沒有缺日、總筆數非零的研究起點，可能未過關或仍虧損，不能宣稱可交易。

預設catalog：`docs/superpowers/evidence/nikkei-rl-bestparent-factors-20261009/model-catalog.json`，共31個候選：原21模型、最高起點all seed22的兩種學習率延續訓練各3種子，以及委託簿／委託流／價格組刪除和僅保留3新增因子各seed22。延續由原32,768步再加32,768步，保留原PPO權重、optimizer與正規化；新增遮罩欄位均值0／變異1，沒有重複暖啟動。單種子刪除組是初步診斷，未證明跨種子穩健。catalog依實際ONNX及manifest種子列舉，支援單種子組。三因子為成交到達強度、成交尺寸集中度、已實現變異（50／53／56），只用四個訓練日期、兩個時間順序折與30秒block bootstrap篩選；代理預測檢查不是獲利證明。50候選目前2既有、20新增、17暫緩、11缺資料；可行性與公式列在本輪factor-library。PPO沿用C#淨損益／回撤代理獎勵，最終選擇依硬門檻及每筆毛期望值。`--catalog`可指定其他固定研究池。

設定維持3秒／15秒、1ms延遲、10秒冷卻、無最長持倉、固定／最大150點、36JPY每邊、額外0／1／2跳成本，不用大型225。每日增量工作不自動重訓、不改正式交易設定、不送實盤。

輸出：`docs/superpowers/evidence/nikkei-rl-daily-bestparent`，舊版本保留。`--historical-report`預設讀取本輪`objective-report.json`；`registry.json`記錄日期、來源檔狀態、首次評估時間及版本。`daily-performance.csv/json/md`逐日提供筆數、淨勝率、毛／淨損益、平均淨獲利／損失、每筆期望毛損益、完整日、成本及目標結果。Markdown列基本成本全部日期，CSV含三種成本。`versions`保留來源快照與SHA、快取、成交／逐步回饋、稽核及耗時。補檔以新版本處理，不覆寫舊實驗；只有夜盤追加且日盤內容相同則沿用原結果。

當日日盤15:00前延後；只有啟動紀錄、不足兩商品、來源仍變動、倒序、Dropped、重疊或C#品質拒絕都明確揭露，不用零值虛構績效。缺口按既有C#分段規則處理，末端未完成浮盈、浮虧及進場成本對稱排除，完成虧損保留。完整日不等於所有行情健康。

新加入的舊日期標記 `NewDateRetrospective`；只有晚於此次模型凍結日期的資料標記 `FutureFrozenEvaluation`，首次評估紀錄保留。之後若用於調參／重訓，須在新實驗中將其改列開發／訓練用途，不能繼續聲稱未見過資料。

`frozen-inputs.json`釘住模型、manifest、C#執行器、歷史報告及Python來源。任一內容變更即停止；升級模型或回測程式時，用 `--output <新研究目錄>` 建立新版本，保留舊紀錄。`.update.lock`禁止重複執行；若程序非正常中止留下鎖，先確認沒有執行中的工作，再人工處理，不能同時重跑。

1ms是動作延遲假設，沒有逐筆加入實測actor、特徵、排程／網路耗時。strict-cross按委託價成交，150點不是實際最大損失保證。每個日期／種子／成本的timing與停損跨越統計保存供查核。

2026-10-09 CURRENT：分鐘因子56/57/59原四訓練時段四組增量篩選已完成，全部不通過；原始來源與公式保留，已測版本不接入actor，證據更新至factor-library。新12pruned時段實驗從最高研究quality80 seed22續訓同131072預算；先核對8個較早完整日、再開始訓練。每日排程維持已驗證69actor不可變來源；新實驗未驗證前不加入。舊screen50559 terminal0；新83911來源準備中，不能稱已訓練。所有硬目標不變，仍無合格模型。

2026-10-09更新：55／60五個已完成分鐘工程版本已完成原四訓練時段的單項／合併三組篩選，0/3保留；55、56、57、59、60均不接入模型，原定義與其他家族版本仍保留。證據：C槽 nikkei-rl-minute-price-screen-20261009/screening-report.md。每日69actor排程ACTIVE且描述已核對更新；12時段新actor仍須獨立完成訓練指紋及回測驗證。

2026-10-09 CURRENT補充：12時段actor完整日索引pin審查缺口已做一次RED→GREEN修正；新獨立nikkei-rl-pruned-earlier-coverage-verification-20261009在前／後驗coverageSHA、同日索引一致與原索引一致，實際重播16日三成本48列全欄位相同。原來源及歷史指紋保留，70actor每日來源所有pins仍一致。後續離線回測必須釘選全部segment-index並拒絕變動，不再以未釘選index的runner作新驗收。58因子20分鐘工程版本5項公式+7聚合檢查通過；增量篩選仍執行，未加入模型。

2026-10-09補回進度：58二十分鐘工程版本原四訓練時段篩選未通過，0/1保留，未接入。10/1原wall資料有一次系統時間倒退，整檔3116780筆微型／迷你行情單調時鐘與序號無倒退或缺欄位；獨立monotonic版本以首筆定位重建、不排序、保留OriginalTime，絕對UTC定位精度未驗證。42項契約檢查通過。C槽nikkei-rl-daily-monotonic-backfill-20261009實際70actor三成本回測仍進行，未驗收前不切換排程或宣稱補回完成。
