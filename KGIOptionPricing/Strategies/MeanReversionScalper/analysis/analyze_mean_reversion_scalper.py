"""Summarize the offline C# replay without changing the strategy or archive.

Usage: python analyze_mean_reversion_scalper.py --runs <backtest-output>
"""

import argparse
import csv
import hashlib
import json
from collections import defaultdict
from pathlib import Path


def summarize(rows):
    wins = [row["NetYen"] for row in rows if row["NetYen"] > 0]
    losses = [row["NetYen"] for row in rows if row["NetYen"] < 0]
    return {
        "trades": len(rows),
        "net_yen": sum(row["NetYen"] for row in rows),
        "win_rate": len(wins) / len(rows) if rows else None,
        "stop_loss_rate": sum(row["ExitRole"] == "StopLoss" for row in rows) / len(rows) if rows else None,
        "average_yen": sum(row["NetYen"] for row in rows) / len(rows) if rows else None,
        "profit_factor": sum(wins) / -sum(losses) if losses else None,
        "one_tick_each_fill_yen": sum(row["NetYen"] - 100 for row in rows),
        "two_ticks_each_fill_yen": sum(row["NetYen"] - 200 for row in rows),
    }


def load_inputs(runs):
    required = ("trades.json", "daily-summary.json")
    for name in required:
        if not (runs / name).is_file():
            raise ValueError(f"缺少必要檔案：{runs / name}")
    try:
        trades = json.loads((runs / "trades.json").read_text(encoding="utf-8"))
        daily = json.loads((runs / "daily-summary.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"回測資料無法讀取或 JSON 損壞：{error}") from error
    if not isinstance(trades, list) or not isinstance(daily, list) or not daily:
        raise ValueError("回測結果結構錯誤或缺少每日摘要")
    for row in daily:
        if not all(key in row for key in ("File", "MinRTicks", "Status", "ClosedTradeNetYen")):
            raise ValueError("daily-summary.json 缺少必要欄位")
    for row in trades:
        if not all(key in row for key in ("File", "MinRTicks", "CycleId", "NetYen", "Regime", "ExitRole")):
            raise ValueError("trades.json 缺少必要欄位")
    return trades, daily


def validate_manifests(runs, daily):
    issues = []
    input_baselines = {}
    run_ids = []
    summaries = {}
    for row in daily:
        archive = row["File"]
        r = row["MinRTicks"]
        run_dir = runs / Path(archive).stem / f"MR-R{r}"
        manifest_path = run_dir / "run-manifest.json"
        summary_path = run_dir / "run-summary.json"
        if not manifest_path.is_file() or not summary_path.is_file():
            raise ValueError(f"缺少執行資訊或共用摘要：{run_dir}")
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            summary = json.loads(summary_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise ValueError(f"執行資訊損壞：{run_dir}：{error}") from error
        if manifest.get("strategyType") != "MeanReversionScalper" or \
                manifest.get("instanceId") != f"MR-R{r}" or \
                manifest.get("mode") != "HistoricalReplay":
            raise ValueError(f"策略類型／實例／模式不符：{manifest_path}")
        normalized = manifest.get("normalizedParameters", "")
        expected_hash = hashlib.sha256(normalized.encode("utf-8")).hexdigest().upper()
        if manifest.get("parameterSha256") != expected_hash:
            raise ValueError(f"參數雜湊不符：{manifest_path}")
        parameters = manifest.get("effectiveParameters") or {}
        if json.loads(normalized) != parameters or int(parameters.get("MinimumRangeTicks", -1)) != r:
            raise ValueError(f"有效參數或 R 門檻不符：{manifest_path}")
        if manifest.get("commissionPerContract") != 36 or \
                not manifest.get("universe") or \
                manifest["universe"][0].get("productId") != "225MC" or \
                manifest["universe"][0].get("tickSize") != 5 or \
                manifest["universe"][0].get("contractMultiplier") != 10:
            raise ValueError(f"商品規格／費用不符：{manifest_path}")
        archive_id = manifest.get("inputArchive") or {}
        assumptions = manifest.get("executionAssumptions") or {}
        key = (archive_id.get("absolutePath"), archive_id.get("sizeBytes"),
               archive_id.get("lastWriteTimeUtc"), json.dumps(manifest["universe"], sort_keys=True),
               manifest["commissionPerContract"], assumptions.get("fillModel"))
        if archive in input_baselines and key != input_baselines[archive]:
            raise ValueError(f"同一行情檔的商品／費用／成交假設不可比較：{archive}")
        input_baselines[archive] = key
        if not archive_id.get("sha256"):
            issues.append(f"{archive}: 未保存輸入檔 SHA-256，僅可按路徑、長度與修改時間比對")
        if row["Status"] != "complete":
            issues.append(f"{archive}/R={r}: {row['Status']}，不納入參數排名")
        run_ids.append(manifest.get("runId"))
        summaries[(archive, r)] = summary
    return sorted(set(issues)), run_ids, summaries


def aggregate_logic(summaries, r=8):
    totals = {name: 0 for name in ("candidateSignals", "acceptedSignals", "submittedOrders",
                                   "cancelledOrders", "rejectedOrders", "fills", "stopLossExits")}
    reasons = defaultdict(int)
    thresholds = defaultdict(lambda: {"evaluated": 0, "passed": 0, "failed": 0})
    for (_, variant), summary in summaries.items():
        if variant != r:
            continue
        for name in totals:
            totals[name] += summary.get(name) or 0
        for name, count in (summary.get("primaryReasonCounts") or {}).items():
            reasons[name] += count
        for name, counts in (summary.get("thresholdFunnel") or {}).items():
            for measure in ("evaluated", "passed", "failed"):
                thresholds[name][measure] += counts.get(measure) or 0
    return {"totals": totals, "primaryReasons": dict(sorted(reasons.items())),
            "thresholdFunnel": dict(sorted(thresholds.items())),
            "mfeMae": "無逐筆持倉路徑，無法計算；不可填 0"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--runs", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--mode", choices=("logic", "parameters", "all"), default="all")
    args = parser.parse_args()
    try:
        trades, daily = load_inputs(args.runs)
        issues, run_ids, summaries = validate_manifests(args.runs, daily)
    except (ValueError, TypeError, KeyError) as error:
        parser.exit(2, f"分析停止：{error}\n")
    groups = defaultdict(list)
    for trade in trades:
        groups[trade["MinRTicks"]].append(trade)
    logic = aggregate_logic(summaries)
    lines = ["# 均值回歸策略歷史重播", "",
             "H=600 秒；F=0.6；K=1.2；225MC 每點 ¥10、每筆成交手續費 ¥36。",
             "基準模型：限價／停損須穿價並以委託價成交；風險市價平倉以送單後首筆有效對手盤（或成交）模擬。",
             "未納入實際送單延遲與滑價。",
             "1 跳／邊滑價壓力為每完整交易扣 ¥100，2 跳／邊為扣 ¥200。",
             "", "## 觀察事實", ""]
    if args.mode != "logic":
        lines += ["| 最小 R | 完整交易 | 淨損益 ¥ | 勝率 | 停損占比 | 每筆平均 ¥ | 1 跳／邊 ¥ | 2 跳／邊 ¥ |",
                  "|---:|---:|---:|---:|---:|---:|---:|---:|"]
        for r in sorted(groups):
            result = summarize(groups[r])
            lines.append(f"| {r} | {result['trades']} | {result['net_yen']:+,} | "
                         f"{result['win_rate']:.1%} | {result['stop_loss_rate']:.1%} | "
                         f"{result['average_yen']:+.1f} | "
                         f"{result['one_tick_each_fill_yen']:+,} | {result['two_ticks_each_fill_yen']:+,} |")
    lines += ["", "## 資料完整性", ""]
    for row in daily:
        if row["Status"] != "complete":
            lines.append(f"- {row['File']} / R={row['MinRTicks']}：{row['Status']}，"
                         f"檔尾部位 {row['OpenQuantity']}，不以檔尾價格虛構平倉。")
    if all(row["Status"] == "complete" for row in daily):
        lines.append("- 所有檔案與門檻的帳本和已配對交易損益一致。")
    lines += ["", "## R=8 各檔結果", "",
              "以下淨損益僅計完整配對交易；檔尾部位另外標記，不以未實現損益假裝平倉。", "",
              "| 檔案日期 | 首筆行情（台灣） | 末筆行情（台灣） | 完整交易 | 已平倉淨損益 ¥ | 狀態 |",
              "|---|---|---|---:|---:|---|"]
    for row in daily:
        if row["MinRTicks"] == 8:
            lines.append(f"| {row['File'][:8]} | {row['FirstEvent'] or '--'} | "
                         f"{row['LastEvent'] or '--'} | {row['CompletedTrades']} | "
                         f"{row['ClosedTradeNetYen']:+,} | {row['Status']} |")
    regime_groups = defaultdict(list)
    for trade in trades:
        regime_groups[(trade["MinRTicks"], trade["Regime"])].append(trade)
    if args.mode != "parameters":
        lines += ["", "## 邏輯診斷與分盤勢（225M 進場前 15 分鐘）", "",
                  "趨勢效率 ≥0.60 為 trend、≤0.30 為 range；15 分鐘高低差 ≥80 點為 highvol；"
                  "資料不足或參考價超過 30 秒未更新標記 unknown。", "",
                  f"R=8：候選 {logic['totals']['candidateSignals']}、接受 {logic['totals']['acceptedSignals']}、"
                  f"委託 {logic['totals']['submittedOrders']}、取消 {logic['totals']['cancelledOrders']}、"
                  f"成交 {logic['totals']['fills']}、停損出場 {logic['totals']['stopLossExits']}。", "",
                  "| 最小 R | 盤勢 | 交易數 | 淨損益 ¥ | 1 跳／邊 ¥ |",
                  "|---:|---|---:|---:|---:|"]
        for (r, regime), rows in sorted(regime_groups.items()):
            result = summarize(rows)
            lines.append(f"| {r} | {regime} | {result['trades']} | {result['net_yen']:+,} | "
                         f"{result['one_tick_each_fill_yen']:+,} |")
    if 8 in groups:
        base = summarize(groups[8])
        lines += ["", "## 分析推論", ""]
        if base["net_yen"] <= 0 or base["one_tick_each_fill_yen"] <= 0:
            lines.append("- R=8 未通過基準淨收益及每邊 1 跳滑價壓力測試；不建議按此參數啟用實盤。")
        path = lambda x: (x["File"], x["CycleId"], x["EntryTime"], x["ExitTime"],
                          x["EntryPrice"], x["ExitPrice"])
        if args.mode != "logic" and 6 in groups and [path(x) for x in groups[6]] == [path(x) for x in groups[8]]:
            lines.append("- R=6 與 R=8 的成交路徑相同；在本樣本中提高至 8 跳沒有形成有效篩選。")
        if args.mode != "parameters":
            lines += ["", "## 邏輯修改建議", "",
                      "- 高波動區間盤與混合盤先做排除實驗；低波動區間盤僅列為模擬候選，不能直接上線。"]
        if args.mode != "logic":
            lines += ["", "## 參數建議", "",
                      "- 先維持基準參數不變；按日期切分後測試 F、K 與高於 10 跳的 R，無樣本外結果前不排名。"]
    lines += ["", "## 仍需驗證", "",
              "此樣本日期短，且同日多個檔案可能不完整；盤勢分組屬探索性，不是實盤啟用門檻。",
              "未建模排隊順位、交易所延遲、斷線、部分成交、跳空滑價與實際可成交量。",
              "MFE、MAE 及完整持倉路徑尚未計算；輸入檔沒有封存 SHA-256，無獨立樣本外切分。",
              "檔尾未平倉列不可與完整交易收益直接比較；必須先補足完整時段，再作樣本外驗證。", ""]
    output = args.output or args.runs / "analysis"
    output.mkdir(parents=True, exist_ok=True)
    report = output / "analysis-report.md"
    report.write_text("\n".join(lines), encoding="utf-8")
    report_data = {
        "analysisVersion": "1.0", "mode": args.mode, "strategyType": "MeanReversionScalper",
        "runIds": run_ids, "validation": {"comparableByArchive": True, "issues": issues,
            "outOfSampleAvailable": False, "inputHashAvailable": False},
        "parameterComparisons": {str(r): summarize(groups[r]) for r in sorted(groups)}
            if args.mode != "logic" else None,
        "regimeFindings": {f"R={r}|{regime}": summarize(rows)
            for (r, regime), rows in sorted(regime_groups.items())}
            if args.mode != "parameters" else None,
        "logicEvidence": logic if args.mode != "parameters" else None,
        "parameterRankingAllowed": False,
        "confidence": "low: short sample, incomplete capture, no out-of-sample split, no latency model",
        "recommendations": ["R=8 未通過收益與滑價護欄，不建議依此回測啟用實盤",
            "日盤高波動區間／混合盤先做排除實驗，低波動區間盤僅保留模擬候選",
            "按日期切分樣本外資料後，再驗證 F、K、較高 R 與延遲"],
    }
    (output / "analysis-report.json").write_text(
        json.dumps(report_data, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.mode != "logic":
        with (output / "parameter-ranking.csv").open("w", encoding="utf-8-sig", newline="") as handle:
            writer = csv.writer(handle)
            writer.writerow(("min_r_ticks", "trades", "net_yen", "one_tick_each_fill_yen",
                             "rank", "eligible", "reason"))
            for r in sorted(groups):
                result = summarize(groups[r])
                writer.writerow((r, result["trades"], result["net_yen"],
                                 result["one_tick_each_fill_yen"], "", False,
                                 "缺少樣本外資料與輸入 SHA-256；未通過滑價護欄"))
    print(report)


if __name__ == "__main__":
    main()
