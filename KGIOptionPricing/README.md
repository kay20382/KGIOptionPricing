# 主程式設定與建置

需要 Windows x64 與 .NET 10 SDK。

首次使用時，將此目錄的 `config.example.json` 複製為 `config.json`，再填入自己的行情／交易連線資料。`config.json` 是本機私密設定，已排除於 Git；請勿提交帳號、密碼或權杖。

在儲存庫根目錄建置：

```powershell
dotnet build KGIOptionPricing.sln -c Release
```

設定範例的 `SaveTickData` 控制 `NikkeiResearch` 日經原始 TICK 記錄器：`true` 開啟、`false` 關閉，修改後重新啟動程式生效。未設定時預設開啟，格式錯誤時停用並顯示原因。它與策略績效及一般日誌設定獨立。

策略績效輸出採摘要模式。RL 模型仍屬研究用途；開放手動實盤操作不代表模型已通過績效驗收。請保留既有登入、人工確認、商品數量及風險限制。

TICK 設定與主要回歸檢查：

```powershell
dotnet run --project tests/TickRecordingSmokeTests -c Release
dotnet run --project tests/NikkeiRlMainSmokeTests -c Release
dotnet run --project tests/StrategyFrameworkSmokeTests -c Release
```

測試使用合成資料及模擬閘道，不發出券商實單。
