# 金融模型候選因子（74–81）

新增8項研究候選，未加入正式RL actor、60維快取或實盤C#策略。

2026-10-11實際驗證完成：189項假說、945列逐日彙總；方向增量0項通過，波動預測23種設定通過（GARCH9、EWMA9、RV1、HAR4）。波動檢驗只證明相對較早訓練常數方差基準的改善，未證明GARCH優於EWMA或交易獲利。完整來源／程式／快取指紋前後一致，24項新增測試及224項既有契約測試通過。

[專案機器可讀新增因子庫](financial-model-candidates.json)包含公式、來源、逐項驗證狀態，以及績效／驗證／獨立稽核SHA。原候選庫作為前置研究登記保留，新增因子使用此獨立補充庫，不改既有actor特徵索引。

|ID|模型|作用|
|---:|---|---|
|74|ARIMA(1,1,0)，零漂移|未來跳數方向預測|
|75|ARIMA(1,1,1)，條件平方誤差校準|未來跳數方向預測|
|76|GARCH(1,1)，零均值Gaussian QMLE|未來累積方差／風險|
|77|EWMA，60秒半衰期|短期波動風險|
|78|60秒RV期限尺度化|既有波動率家族的新研究版本|
|79|Parkinson取樣區間工程版|價格區間波動風險|
|80|Kalman報酬局部水準|因果濾波方向預測|
|81|HAR型30／60／180秒工程版|盤中多尺度波動預測|

決策間隔3／5／10秒，預測期限15／30／60秒；模型步長固定3秒，按1秒參考時鐘的三相位分別更新。估參只用較早訓練日，不以評估資料選階／窗口；缺口重置，至少60秒暖機（HAR180秒）。13日期為8訓練、5已檢視評估，不能稱為全新樣本外。

方向增量與直接波動檢驗分開，共189假說，與1127歷史合併BY修正。模型具統計預測能力不等於交易獲利。原1ms動作延遲、10秒出場冷卻、150點停損與strict-cross成交規則留待共用帳本回測；本輪不模擬成交。

候選庫、Python公式與JSON校準係數供後續C#實作參照；目前沒有新增因子的Python/C#逐值驗證，不宣稱已接入或1ms全流程。

- [完整公式、來源與候選庫](C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-financial-model-source-20261011/candidate-library.json)
- [實作與驗證設計](C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-financial-model-source-20261011/design.md)
- [實際行情驗證報告](C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-financial-model-validation-20261011/prediction-report.md)
- [逐日績效CSV](C:/Users/user/.codex/visualizations/2026/10/07/01a1161a-64d8-7c81-9e7d-b725164d7ff5/nikkei-financial-model-validation-20261011/daily-prediction-performance.csv)

缺資料／不足樣本保留原因，不補零；不保存逐筆因子、委託或成交明細。舊61候選庫、62–73文獻候選及既有凍結績效不覆寫。

