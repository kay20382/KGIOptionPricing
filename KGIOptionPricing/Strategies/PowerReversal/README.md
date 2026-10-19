# 買賣力道反轉交易

2026-10-08 拒單關閉修正：最新發布為releases/PowerReversal-20261008-reject-close，根目錄「啟動買賣力道最新版.cmd」指向此版。券商明確回報Rejected且累計與已交付成交皆為0、沒有協定異常時，解除晚到成交隔離；仍保留委託識別資料供晚到或矛盾回報處理。零部位且無其他工作／隔離委託的策略可立即關閉，主程式可正常退出。撤單、送單結果不明、未交付成交及協定異常仍保留關閉保護。本次不變更策略參數、進出場與績效假設，沒有送出真實委託。

2026-10-08 加權中價混合進場：新實例EntryExecutionMode=1、WeightedMidMode=0。比較同月份225與225MC的第一檔加權中價，預設公式（買價×賣量＋賣價×買量）／總量；WeightedMidMode=1可改同側權重。2026-10-08 更新方向委託規則：微型高於大型時，買進掛微型買一、賣出市價；微型低於或等於大型時，買進市價、賣出掛微型賣一。掛單送真正BrokerLimit ROD。原54項trail1參數與1R／0.5R出場保留。

只有報價、數量、跳動點、月份、新鮮度與時間差都有效才可新進場；完整委託量缺漏的成交不會冒充新委託簿。最新無效委託簿會阻擋，不能偷偷沿用上一筆良好報價。只用第一檔，不是完整Stoikov micro-price模型。

固定發單當時的第一檔價格，10秒未成交撤單，確認後冷卻20秒再重新評估；等待撤單時不重掛、不改為市價追單。拒單、市價逾時、暫停與關閉仍沿用停止新倉處理。晚到成交會保護平倉，若存在較新掛單先要求撤銷；發生兩筆成交競態時按實際共用部位逐口收尾，保留每單一口限制，直到部位與委託清空。

UI同時顯示大型與微型加權中價、發單時差值、選定委託方式及資料原因。原停損、停利、時間風控及1R／0.5R移動出場仍走市價保護。舊54項設定缺少新欄位時EntryExecutionMode=0，載入舊設定不會自動啟用新進場方式。

先前發布版releases/PowerReversal-20261008-weighted-mid保留。研究證據見docs/superpowers/evidence/power-weighted-entry-20261008/；新進場規則尚無五日／樣本外獲利證據。下方trail1五日績效是舊進場方式的歷史結果，不是本版績效。

## 先前trail1發布

2026-10-08 使用者指定採用「1R啟動／0.5R回撤」。模擬及實盤新實例的BestCandidateProfile設定TrailingExitMode=1、TrailingActivateRisk=1、TrailingDistanceRisk=0.5；其餘參數與五日研究的trail1方案完全一致。發布版為releases/PowerReversal-20261008-trail1；根目錄「啟動買賣力道最新版.cmd」指向此版。舊實例不會自動套用，需依既有流程正常收尾後重啟程式並新建實例。Parse的舊參數相容預設仍關閉移動出場，載入舊設定時以實際參數摘要為準。

五日回溯比較為1238筆來回、淨勝率43.94%、淨損益−75086日圓；依使用者選擇發布，不宣稱此方案優於基準或已獲利。以下「先前出場研究階段」的未發布敘述是本次發布前狀態。

## 先前出場研究階段

2026-10-08 出場研究設定：UI仍以BestCandidateProfile建立新實例，原49項參數保留，新增5項向後相容設定。PowerExitMode預設0（微型BuyPower與短均反向），1表示小型與微型皆反向，2表示依持倉方向正規化的聯合力道<=ExitWeakPowerThreshold且聯合BaisPower反向。後兩者須有兩商品新鮮資料，均連續ExitConfirmSeconds才出場。ExitWeakPowerThreshold預設10百分點。

TrailingExitMode預設0；設1後，可執行買一／賣一浮盈達TrailingActivateRisk倍初始風險時啟動，從最佳可執行價格回撤TrailingDistanceRisk倍出場。初始風險為實際成交均價至原固定停損距離，預設啟動1R、回撤0.5R，向上取整跳數，只收緊不放寬。固定停損、停利及時間／收盤風控保留；移動出場使用Flatten角色與TRAIL_EXIT原因，不保證扣費保本。暫停仍保護持倉，等待出場回報時不重複送單。

這輪只增加來源程式研究設定並以六組固定方案比較五個既有交易日。凍結執行檔與「啟動買賣力道最新版.cmd」保留，新增設定尚未套用至已發布版本。比較與可重現腳本見`docs/superpowers/evidence/power-exits-20261008/`。滿500段歷史的快照重用未變更JSON，避免高頻重複序列化；區段新增或時段／暖機重置仍立即更新。

依使用者要求刪除研究回測逐筆紀錄後，分析入口可透過`retention-status.json`讀取僅保留的共用績效與參數，核對參數／輸入雜湊及績效是否匹配。此模式明確標示明細無法重新稽核且維持Incomplete；未提供此標記的缺檔仍會報錯，不以零或虛構成交補齊。

跨商品共識濾網：`MinJointPower`為225M與225MC BuyPower的等權平均下限；`MaxPowerDifference`為兩者絕對差的百分點上限；`MinShortAveragePower`為兩商品各自短期時間加權平均的強度下限。平均與短均依進場方向正規化，做空規則對稱；各自原始BuyPower、BaisPower門檻仍必須成立，不讓強商品掩蓋弱商品。任一條件失敗即重置連續確認，原反轉模式也會在反轉事件再次核對。預設分別0、200、0，不改變舊參數行為；暖機及無資料不會補零通過。參數、門檻與拒絕計數均沿用共用紀錄。研究候選及5天比較保存於`docs/superpowers/evidence/power-consensus-20261007`，研究結果不自動修改實盤預設。

2026-10-07新增兩個預設關閉的研究濾網：`RequireAlignedMovement=1`要求225MC行情段與力道方向同向，未知方向跳過；不得與`RequireOppositeMovement=1`同時使用。`PriceRecoveryTicks>0`則要求有效225MC成交從反向段極值反彈／回落指定跳數，並連續完成原確認時間；需`SignalMode=1`及反向行情濾網。後者只使用已到達的有效成交，10倍數口數、無效報價與晚到成交不更新確認價格，跨時段／重連／恢復時清除價格。新欄位預設0，舊參數檔維持原語意。

冷卻20秒研究驗收為同一組參數在9/28、9/29、10/1、10/5、10/6每一天都完成至少100筆來回、扣費後淨損益為正、扣費後勝率高於40%。候選與紀錄在`docs/superpowers/evidence/power-cool20-20261007`，首日未通過即淘汰；完整跨日組另行保存。既有360秒版本封存於`releases/PowerReversal-20261007-filter360`。

模擬策略中心與實盤策略中心新增專用頁籤，原「買賣力道」頁仍可只觀測。交易商品為 225MC 一口，225M 確認，225 只記錄。三商品沿用同一指標核心與交易頁共用參數；BaisPower 為短期減長期平均，單位百分點。

策略頁新實例採「每日約100筆反向行情濾網研究參數」：SignalMode=1、RequireOppositeMovement=1：只在225MC行情方向與力道相反時進場，未知方向跳過；兩商品同向 BuyPower≥5%、BaisPower≥1百分點持續1秒，且必須收到有效225MC成交才發出一口市價意圖。指標秒數為力道10、短均10、長均60；完成約70秒暖機後才可進場。停損6跳（225MC為30點）、停利16跳（80點），平倉後冷卻360秒並重新確認力道，不設定必須成交的每日配額。停損與停利依送出意圖時的可執行報價建立；下一筆實際成交價可能不同，計畫報酬風險比不保證等於實現報酬風險比。

SignalMode=0保留原反轉模式：下跌段走到過去平均跌幅的60%，兩商品 BuyPower≥10%、BaisPower≥5百分點，持續2秒後等待反轉30點。已完成漲／跌各至少5段，視窗有效口數至少30與筆數至少5。做空對稱。RequireShortAverageDirection=0時不要求短均先翻正／負，仍要求即時力道與短長差同向；設1可恢復此限制。每段最多一次進場嘗試，資料失效、暫停、跨時段或重連即重置候選。SignalMode=1不使用MovementRatio／MinMovementSamples／AllowRiskRewardPullback，30點反轉與完成段平均仍顯示為行情觀測指標。

反轉確認時用225MC買一／賣一建立對齊 TickSize 的一口限價條件。沿用框架嚴格穿價：多單成交價低於買入條件價、空單高於賣出條件價才成交，僅觸價不成交。實盤由既有執行器在穿價後送市價 IOC；模擬限價按條件價成交，不採較有利穿越價。10秒未成交撤單並暫停，等待確認後才可重新暖機恢復；晚到成交保護平倉。

停損固定在反轉極值外1跳，目標固定為反轉極值加／減進場前相反方向平均段長。扣雙邊費用與設定滑價後剩餘空間／停損距離至少1.5倍。AllowRiskRewardPullback=1時，原價空間不足會反算符合1.5的較佳回檔限價，再往有利方向對齊跳動點；無有效價格仍跳過。設0恢復直接跳過。維持嚴格穿價、10秒期限，未穿價就撤單並暫停，不能推定成交。固定停損、停利一次只發單一 Market 出場意圖；模擬市價單依共用模型下一筆成交成交。180秒或收盤前60秒 Flatten；BuyPower 與短均反向持續5秒也 Flatten。

設定值與費用在新實例啟動前可編輯／儲存／載入；執行中參數固定，要先關閉收尾再啟動新實例。兩邊使用同一策略與共用帳本，不自行維護權威損益。單邊費用預設 JPY36、訊號成本必須與實際執行費用一致。實盤頁預設單日虧損 JPY3000，使用既有勾選、登入、帳戶確認、225MC白名單與一口限制；開發不啟動券商委託。

暫停仍接收持倉行情與保護性出場，恢復須空手／委託歸零，重新暖機。關閉會停止新倉並等待撤單及平倉；資料斷線且有部位／進場工作單時要求風險收尾。停損是本地保護，依賴程式、行情及券商通道；並未建立交易所原生停損單。

CSV 行情段使用既有指標保存；共用執行產物在 StrategyRuns／Backtests／StrategyLiveRuns。實盤最後成交與生命週期路由結束後才最終化紀錄。紀錄保存異常顯示 ObservationError、停止新倉且保留持倉風控；此實例不能恢復新倉，需要核對紀錄後另建實例。時間仍使用接收 Timestamp，沒有已驗證完整交易所日期時區。

`profiles/研究參數.json` 與 `profiles/原始保守參數.json` 可由頁籤「載入」使用，建置時隨程式複製。核心未提供參數仍保留原始保守預設；UI明確傳入力道連續研究參數，既有實例不變。以同一晚723,401筆行情分階段比較9組：保守基準0筆委託，研究回檔組1筆進場意圖、逾時撤單、0成交；延長到30秒亦未成交。這是可達性診斷，不代表獲利改善或最佳參數。完整紀錄見 `docs/superpowers/evidence/power-reversal-tuning/comparison-report.md`。

```powershell
dotnet run --project tests/PowerReversalSmokeTests --no-restore -p:UseSharedCompilation=false
python tests/PowerReversalSmokeTests/test_analysis.py
python KGIOptionPricing/Strategies/PowerReversal/analysis/analyze_power_reversal.py --runs <共用執行資料夾> --output <獨立輸出資料夾> --mode all
```

分析驗證完整產物、有效參數雜湊、費用、切分與可比較性，輸出 JSON／繁體中文 Markdown；樣本少於30筆、合成資料或缺少有效樣本外參數實驗時為 Incomplete，不推薦最佳參數。門檻是研究起始值，未具真實市場獲利證據或券商通道認證。

`profiles/每日100至200筆研究參數.json` 保留147筆的舊研究設定，可重新載入；原兩份反轉參數保留。2026/10/06完整日盤與夜盤147筆完整交易、勝率26.53%、扣成本淨損益−JPY12,034，屬單日樣本內交易頻率校準，尚非獲利參數。每次成交JPY36，模擬市價按共用模型下一筆成交價成交，未配置額外基準滑價或延遲；0／1／2跳逆向滑價另列壓力測試。實盤單日虧損上限仍保留，因此實盤可能較早暫停，不能保證與回測筆數相同。詳見 `docs/superpowers/evidence/power-frequency-20261006/comparison-report.md`。

目前新預設 `profiles/每日約100筆反向行情濾網研究參數.json`：RequireOppositeMovement=1、CooldownSeconds=360，其餘停損30點／停利80點及資料健康、成本與計畫報酬風險比保留。2026/10/06完整回測102筆、勝率31.37%、毛損益+JPY2,250、費用JPY7,344、淨損益−JPY5,094。這是毛損益與約100筆的單日研究目標達標，不是淨獲利或每日筆數保證。RequireOppositeMovement=0可關閉新濾網；載入舊參數組可還原舊設定。既有執行實例不自動變更。比較六組候選與獨立日期固定參數檢查見 `docs/superpowers/evidence/power-filter-20261006/comparison-report.md`。

自適應出場研究模式：AdaptiveExitMode=1（需SignalMode=1），以進場前225MC最近已完成上漲／下跌行情段的平均點數推算。多單停損使用下跌平均、停利使用上漲平均；空單相反。各方向至少MinMovementSamples筆，缺樣本跳過。停損乘StopMovementFraction後向上取整跳數，限制AdaptiveMinStopTicks至AdaptiveMaxStopTicks；停利乘TargetMovementFraction後向下取整並限制AdaptiveMaxTargetTicks，未滿一跳跳過。保留扣費與滑價預算後MinRewardRisk檢查。進場意圖建立時固定價格，成交後不隨新平均放寬；實際虧損仍可能因行情跳空或成交延遲超過觸發距離。預設關閉，原參數不改。

共用績效增加maximumTradeLoss（完整交易扣費後最大虧損，正額，無交易為null）、maximumIntradayLoss（淨權益相對起始0的最大虧損，包含浮動損益）、maximumDrawdown（淨權益高點至低點）。研究回測每筆微型LastPrice事件後觀察共用帳本，避免UI節流遺漏極值；這是接收時間行情，非逐筆交易所時序保證。每次日回測日夜盤連續統計，非盤段分開重置風險數字。

自適應050125研究參數保留20秒冷卻與跨商品40／20／10濾網，停損乘0.5、停利乘1.25；僅供本輪回溯研究，預設與既有執行實例不變。比較績效與最大單筆虧損、當日最大虧損／回撤見 `docs/superpowers/evidence/power-adaptive-20261007/comparison-report.md`，不得將首日選擇當成實盤獲利資格。
