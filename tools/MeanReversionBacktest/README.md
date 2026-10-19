# 225MC 均值回歸離線回測

此工具只讀 NikkeiResearch JSONL 檔；不連接交易帳號，也不送實單。使用正式的雙側限價 `MeanReversionScalperStrategy`、`StrategyHost`、共用損益帳本與歷史重播行情。預設每檔同時測試最小 R=4、6、8、10 跳，其他參數固定為 H=600 秒、F=0.6、K=1.2、最大 R=20 跳、最大價差 1 跳、行情新鮮度 2 秒、冷卻 5 秒；225MC 每點 ¥10、每筆成交手續費 ¥36。均線預設關閉；關閉時只有 225MC 是必要行情，225／225M 有檔案時仍供盤勢標籤使用。

```
dotnet run --project tools/MeanReversionBacktest/MeanReversionBacktest.csproj -- --input <NikkeiResearch目錄> --output <全新輸出目錄>
dotnet run --project tools/MeanReversionBacktest/MeanReversionBacktest.csproj -- --input <NikkeiResearch目錄> --output <另一個全新輸出目錄> --min-range 8 --max-range 20 --use-ma --ma-reference 225M --shift-ticks 1
python KGIOptionPricing/Strategies/MeanReversionScalper/analysis/analyze_mean_reversion_scalper.py --runs <輸出目錄> --output <輸出目錄>/analysis --mode all
```

`--min-range` 可固定單一最小 R；`--max-range` 設最大 R；`--use-ma` 啟用指定參考商品的 H 秒均線，`--shift-ticks` 設要求位移 N（0–20，實際 N 可能因 bid1／ask1 被動價格限制而縮小）。所有模式仍同時掛出原始一買一賣；均線只共同平移兩側價格。比較開關時須使用相同輸入檔、成本與 R 範圍，且不可從本樣本挑參數後直接宣稱樣本外績效。

限價／停損單沿用共用紙上成交器的嚴格穿價條件；市價風險平倉以送單後下一筆有效對手盤報價成交，若沒有報價則等待下一筆成交。報價首檔數量必須足夠。這是研究假設，未模擬延遲、委託簿撤單、部分成交與真實滑價。分析報告另以每邊 1／2 跳滑價作壓力測試，不把其視為實際成交價。

每個策略實例分開保存 `run-manifest.json`、`executions.csv`、`strategy-decisions.jsonl`、`order-lifecycle.jsonl`、`run-summary.json`；輸出根目錄有 `daily-summary.json`、`trades.json`、`regime-summary.json`，分析輸出在 `analysis/`。分析腳本支援 `--mode logic|parameters|all`，輸出 Markdown、JSON 及不可排名原因；沒有樣本外資料時不會產生勝出參數。`complete` 僅表示該檔模擬帳本已對平、沒有留倉或收盤後平倉，不表示行情涵蓋完整日夜盤；須另檢查首末筆時間。若檔尾仍有部位、損益對不平或平倉晚於市場收盤，命令會傳回非零值。絕不在檔尾虛構平倉。

盤勢標籤只使用進場前 15 分鐘的 225M 逐筆價抽樣：走勢效率 ≥0.60 為趨勢、≤0.30 為區間；15 分鐘高低差 ≥80 指數點為高波動。資料不足與超過 30 秒未更新為 `unknown`。上述界線僅供分組研究，不是已驗證的交易濾網。
