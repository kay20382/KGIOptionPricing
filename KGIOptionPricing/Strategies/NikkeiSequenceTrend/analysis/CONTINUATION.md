# 日經序列模型持續研究

使用者已授權未達目標就修改直到完成。只限研究與模擬，不是真實下單或現行策略部署授權。

主工作區 E:\PROG\C#\KGIOptionPricing。研究根目錄 C:\Users\user\.codex\visualizations\2026\10\14\01a13a6e-e0ad-70a0-a8b8-fd3cc6210661\sequence-research。

目前第四輪20261015-v4已完成：8候選全不合格；LSTM22選定44筆、43.18%勝率、-718JPY、PF0.933，每日28/16筆；其他選定均負淨損益。資源Job峰值772423680bytes、保留36397042bytes，cache/scratch/replay-temp已刪。此completed僅代表研究程序結束，非驗收完成。

第五輪20261015-v5正在執行 run_trend_alpha.py (49 tests passed)。唯主要變動：價值/勝率監督僅對過去60s300s同向的方向，保留域內所有盈虧；30秒頭全部健康窗口、推論歷史窗口及進出場未變。20小型NPZ+train-only normalizer SHA複製；R5 cache-provenance/原始源SHA/訓練sourceSHA釘選，不可改正在跑的版本。resource-summary出現前讀progress.log與模型結果，不另開訓練。資源guardian Job限4GiB、WS3GiB、CPU2、RAM保留8GiB、C保留30GiB、根5GiB/200檔、最終100MiB、讀20寫5MiB/s；終了刪該輪中間檔。

驗收硬門檻：淨損益最大化（僅在合格候選間比較）、勝率>40%、淨PF>2、每個完整策略日最大虧損<8000JPY、每日至少10筆完成平倉交易。零交易、開倉未清、缺資料、浮動風險未全量回放不能通過。正式需要凍結候選、保留測試集、凍結後5個新完整策略日、額外滑價壓力、官方事件日曆驗證；宏觀事件Unknown/CME holiday尚未完整驗證，不可冒稱部署就緒。

模型120×45過去10分鐘/5秒樣本+16aux30min/2h；30秒forecast與成交起算180秒net/win分離，linear/GRU/LSTM與HistGB樹。净目标裁切128ticks、MSE條件均值，不能聲稱無裁切的send expectation。每邊基礎36JPY手續費，225MC tick5/點值10，1ms嚴格穿價被動成交、止損50points、入場10秒截止，限180秒實際成交起算，不得忽略费用或放寬指標。

保留集10/9從未被R3/R4讀取；R5若有合格候選才在freeze之後獨占建立研究根/holdout-consumption/225MC-20261009.json後讀取。marker永久保留，換輪不能再次讀；品質失敗也不能刪或當成可再調參來源。來源保留原值，不排序、不原地修歷史。9/28與10/6反覆調整屬開發驗證，不能稱為獨立樣本外證據；不要靠無限門檻/種子掃描追逐兩天結果。

原始資料 E:\PROG\C#\KGIOptionPricing\KGIOptionPricing\bin\Debug\每日執行檔\NikkeiResearch。10/15目前439968759bytes，已有寫入；此前0bytes情況已自行恢復，原因未查明，不能重啟真實app或下單。完整策略日包含該來源日07:45–14:40及16:00–次日04:55；獨立新日須完成才能验收，活動檔不能原地讀寫或稱穩定。缺quality只能記錄拒絕與等新增資料。

Python普通 C:\Users\user\AppData\Local\Programs\Python\Python310\python.exe，PYTHONUTF8=1、LOKY_MAX_CPU_COUNT=2。ONNX export用workspace .venv-rl，不裝新套件。新一輪先有明确失敗診斷、凍結計畫、測試及單一資源guardian；不修改舊source/model/cache指紋。原始行情不可刪。C#bridge/runtime是離線工具，不修改現行appDLL與PPO部署模型。

只對本研究明確可刪的中間檔做safe_cleanup。E docs/superpowers/evidence已hash驗證搬C並用junction，不能對junction遞迴刪除。保留既有dirty worktree與所有其他使用者修改。不寫記憶，不送外部訊息。不建立新任務；在本thread續作。
## 第五輪結束補記
第五輪8候選全不合格，resource-summary outcome=completed只是程序結束。Job峰值752697344bytes，保留34425884bytes，cache/temps清除。第五輪GRU22选定43笔、-646JPY、34.88%、PF0.915，仍失敗。未建立freeze或consume holdout。不要無理由接著重調同兩個驗證日。
下一研究先扩大完整可靠資料日/時間前推驗證，且診斷被動進場逆向選擇；新入場方式須重做匹配标签、成本与因果成交测试。正式驗收還未開始。詳細结果 docs/superpowers/plans/2026-10-15-nikkei-sequence-iteration-status.md。
每小時heartbeat建立被auto-review拒絕（持久資源使用與檔案修改需使用者明確授權）。已提出授權選項，未收到同意，不能用其他方式繞過、不能說排程已設定。
E盤剩餘空間查詢約3.8GiB且下降，現行兩個20261015策略log實際合計8.5GiB持续寫入；GCI目錄列舉大小會stale，必須Get-Item精確查询。原始行情有寫入，約504MiB。已獨占讀取並限速copy已停止20261014-110250的1927736219bytes log到C storage-migration-20261015/closed-run-110250；驗證完成後移除E副本，保留manifest及E archive pointer，活動檔案不動。勿另開重複copy。
## 搬移完成
20261014-110250已停止策略記錄1927736219bytes已搬至C槽，SHA256=9440099E8170D5812E1CF20FA7AD91ABB4AFD1F377D9E1F78F13518A78F260F5。獨占來源、限速寫5MiB/s/驗證讀20MiB/s、Flush(true)、兩端SHA相同、來源metadata不變才刪E副本。E原資料夾保留strategy-events-archive.json指向C副本，委託/成交等其他檔案未移動。C storage-migration-20261015/closed-run-110250/manifest.json保留實證。