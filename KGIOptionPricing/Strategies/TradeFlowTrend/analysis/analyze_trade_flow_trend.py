"""Read-only diagnostics for the shared TradeFlowTrend run artifacts (stdlib only)."""
import argparse
import csv
import hashlib
import json
import math
import re
import statistics
import sys
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path


class AnalysisError(ValueError):
    pass


def parse_timestamp(value):
    # .NET emits seven fractional digits; Python 3.10 accepts at most six.
    return datetime.fromisoformat(re.sub(r"(\.\d{6})\d+(?=[+-]|Z|$)", r"\1", value).replace("Z", "+00:00"))


def read_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise AnalysisError(f"無法讀取 {path}：{exc}") from exc


def read_rows(path):
    try:
        return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    except (OSError, ValueError) as exc:
        raise AnalysisError(f"JSONL 損壞或缺失 {path}：{exc}") from exc


def validate_manifest(m):
    if m.get("strategyType") != "TradeFlowTrend" or m.get("schemaVersion", 1) != 1:
        raise AnalysisError("策略類型或結構版本不符")
    p, normalized = m.get("effectiveParameters"), m.get("normalizedParameters")
    if not isinstance(p, dict) or not p or not isinstance(normalized, str):
        raise AnalysisError("缺少完整有效參數與標準化內容")
    try:
        if json.loads(normalized) != p:
            raise AnalysisError("參數字典與標準化內容不一致")
        actual = hashlib.sha256(normalized.encode("utf-8")).hexdigest().upper()
        if actual != str(m.get("parameterSha256", "")).upper():
            raise AnalysisError("參數雜湊不符")
        ranges = {"Quantity": (1, 100), "FastSeconds": (1, 3600), "SlowSeconds": (1, 3600),
                  "StopTicks": (1, 10000), "ProfitTicks": (1, 10000), "SarActivationTicks": (1, 10000),
                  "SarBarSeconds": (1, 300), "History30": (1, 3600), "History60": (1, 3600),
                  "History180": (1, 3600), "MaxBookAgeMs": (100, 60000), "EntryDirectionThreshold": (0, 100),
                  "EntryAverageMargin": (0, 200), "ExitDirectionThreshold": (0, 100)}
        for key, (low, high) in ranges.items():
            if key in p and not low <= float(p[key]) <= high:
                raise AnalysisError(f"參數 {key} 超出範圍")
        if float(p["FastSeconds"]) >= float(p["SlowSeconds"]):
            raise AnalysisError("快線必須小於慢線")
        if not 0 < float(p["SarStart"]) <= float(p["SarMax"]) <= 1 or not 0 < float(p["SarStep"]) <= float(p["SarMax"]):
            raise AnalysisError("SAR AF 範圍錯誤")
    except (KeyError, TypeError, ValueError) as exc:
        if isinstance(exc, AnalysisError):
            raise
        raise AnalysisError(f"參數格式不完整：{exc}") from exc


def validate_splits(splits):
    parsed = []
    for split in splits:
        try:
            start, end = (parse_timestamp(split[k]) for k in ("start", "end"))
            if start.tzinfo is None or end.tzinfo is None or start >= end:
                raise ValueError("起迄或時區無效")
            parsed.append((start, end, split["kind"]))
        except (KeyError, TypeError, ValueError) as exc:
            raise AnalysisError(f"資料切分不完整：{exc}") from exc
    parsed.sort()
    if any(a[1] > b[0] for a, b in zip(parsed, parsed[1:])):
        raise AnalysisError("訓練／驗證／樣本外區間重疊")
    return {item[2] for item in parsed}


def comparable(a, b):
    keys = ("universe", "commissionPerContract", "executionAssumptions")
    fingerprint = a.get("inputArchive", {}).get("sha256")
    return bool(fingerprint) and fingerprint == b.get("inputArchive", {}).get("sha256") and all(a.get(k) == b.get(k) and a.get(k) is not None for k in keys)


def distribution(values):
    good = [float(v) for v in values if v is not None and isinstance(v, (int, float)) and math.isfinite(v)]
    return {"count": len(good), "min": min(good) if good else None,
            "max": max(good) if good else None, "avg": statistics.mean(good) if good else None}


def load_run(folder):
    m = read_json(folder / "run-manifest.json")
    validate_manifest(m)
    s = read_json(folder / "run-summary.json")
    decisions = read_rows(folder / "strategy-decisions.jsonl")
    orders = read_rows(folder / "order-lifecycle.jsonl")
    experiments = read_rows(folder / "parameter-experiments.jsonl")
    try:
        with (folder / "executions.csv").open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            if not {"timestamp", "instanceId", "price", "quantity", "commission"}.issubset(reader.fieldnames or []):
                raise AnalysisError("成交 CSV 欄位不足")
            fills = list(reader)
            for row in fills:
                for key in ("price", "quantity", "commission"):
                    if not math.isfinite(float(row[key])):
                        raise AnalysisError("成交 CSV 非有限數值")
    except (OSError, ValueError, KeyError) as exc:
        raise AnalysisError(f"成交檔案 {folder}：{exc}") from exc
    return {"manifest": m, "summary": s, "decisions": decisions, "orders": orders, "fills": fills, "experiments": experiments}


def diagnose(run):
    m, s = run["manifest"], run["summary"]
    samples = [d for d in run["decisions"] if d.get("kind") == "FeatureSample"]
    features = defaultdict(list)
    session_reasons = defaultdict(Counter)
    for row in samples:
        for key, value in row.get("features", {}).items():
            features[key].append(value)
        try:
            hour = parse_timestamp(row["timestamp"]).hour
            session = "日盤" if 7 <= hour < 15 else "夜盤"
            session_reasons[session][row.get("primaryReasonCode", "未知")] += 1
        except (ValueError, KeyError):
            session_reasons["時間缺失"]["缺資料"] += 1
    quality = Counter(flag for row in run["decisions"] for flag in row.get("dataQualityFlags", []))
    states = Counter(row.get("state", "未知") for row in run["orders"])
    reasons = Counter(row.get("reasonCode", "未知") for row in run["orders"])
    return {"runId": m["runId"], "samples": len(samples), "fixedRi": sum(d.get("kind") == "RiClosed" for d in run["decisions"]),
            "features": {k: distribution(v) for k, v in sorted(features.items())},
            "sessionReasons": {k: dict(sorted(v.items())) for k, v in sorted(session_reasons.items())},
            "dataQuality": dict(sorted(quality.items())), "orderStates": dict(sorted(states.items())),
            "orderReasons": dict(sorted(reasons.items())), "fillRows": len(run["fills"]),
            "summary": s, "recommendations": [{"evidence": f"{m['runId']}: {len(samples)} 秒採樣、{len(run['orders'])} 委託轉換",
                "text": "先核對未知成交比例、暖機與撤單最終水位，再比較訊號門檻。",
                "risk": "合成資料、資料不足或缺少對照組，不能推論策略具有獲利能力。"}]}


def rank_runs(runs):
    by_id = {r["manifest"]["runId"]: r for r in runs}
    eligible, excluded, groups = [], [], set()
    for run in runs:
        m, s = run["manifest"], run["summary"]
        records = [x for x in run["experiments"] if x.get("candidateRunId") == m["runId"]]
        reasons = []
        if not records:
            reasons.append("缺少事前實驗、護欄與資料切分")
        else:
            ex = records[-1]
            base = by_id.get(ex.get("baselineRunId"))
            if base is None or not comparable(base["manifest"], m):
                reasons.append("基準與候選來源或執行假設不可比較")
            kinds = validate_splits(ex.get("dataSplits", []))
            if not {"train", "validation", "test"}.issubset(kinds):
                reasons.append("缺少訓練／驗證／樣本外區間")
            if ex.get("primaryObjective") != "NetPnl" or ex.get("status") != "Valid":
                reasons.append("事前目標未支援或實驗尚未有效")
            gates = ex.get("guardrailValues", {})
            required = ("minimumCompletedTrades", "maximumDrawdown", "minimumStress2")
            if any(gates.get(k) is None for k in required):
                reasons.append("缺少明確護欄")
            else:
                if s.get("completedTrades") is None or s["completedTrades"] < gates["minimumCompletedTrades"]:
                    reasons.append("完整交易樣本不足")
                if s.get("maximumDrawdown") is None or s["maximumDrawdown"] > gates["maximumDrawdown"]:
                    reasons.append("回撤護欄未通過")
                stress = s.get("adverseSlippageStress", {}).get("2")
                if stress is None or stress < gates["minimumStress2"]:
                    reasons.append("2 tick 壓力護欄未通過")
            if any(v for v in s.get("dataQualityCounts", {}).values()):
                reasons.append("存在資料品質問題")
            if (s.get("dataQualityEvaluations") or 0) <= 0 or "dataQualityCounts" not in s:
                reasons.append("缺少實際資料品質檢查證據")
            if m.get("missingProvenanceFields") or s.get("netPnl") is None:
                reasons.append("追溯資訊或淨結果不完整")
            # Never infer out-of-sample results from a single aggregate run.
            if not all(ex.get("splitResults", {}).get(k, {}).get("completedTrades", 0) > 0 for k in ("train", "validation", "test")):
                reasons.append("缺少各切分實際結果")
        if reasons:
            excluded.append({"runId": m["runId"], "reasons": reasons})
        else:
            groups.add(json.dumps({"archive": m["inputArchive"]["sha256"],
                "universe": m["universe"], "commission": m["commissionPerContract"],
                "execution": m["executionAssumptions"], "splits": ex["dataSplits"],
                "guardrails": ex["guardrailValues"], "objective": ex["primaryObjective"]}, sort_keys=True))
            eligible.append({"runId": m["runId"], "parameters": m["effectiveParameters"], "netPnl": s["netPnl"],
                             "maximumDrawdown": s["maximumDrawdown"], "stress": s["adverseSlippageStress"]})
    if len(groups) > 1:
        return [], excluded + [{"runId": x["runId"], "reasons": ["候選跨不同資料或實驗條件，請分組分析"]} for x in eligible]
    if len(eligible) < 2:
        return [], excluded + [{"runId": x["runId"], "reasons": ["可比較候選不足兩組，不排名"]} for x in eligible]
    eligible.sort(key=lambda r: (-r["netPnl"], r["runId"]))
    for i, row in enumerate(eligible, 1):
        row["rank"] = i
    return eligible, excluded


def run_analysis(runs, output, mode="all"):
    if mode not in ("logic", "parameters", "all"):
        raise AnalysisError("mode 必須為 logic、parameters 或 all")
    root, destination = Path(runs).resolve(), Path(output).resolve()
    files = [root / "run-manifest.json"] if (root / "run-manifest.json").is_file() else sorted(root.glob("*/run-manifest.json"))
    if not files:
        raise AnalysisError(f"沒有已完成的執行資訊清單：{root}")
    loaded = [load_run(file.parent) for file in files]
    if any(destination == f.parent or f.parent in destination.parents and destination.name != "analysis" for f in files):
        raise AnalysisError("輸出必須為獨立分析目錄")
    rankings, excluded = rank_runs(loaded) if mode != "logic" else ([], [])
    report = {"analysisVersion": 1, "strategyType": "TradeFlowTrend", "mode": mode,
              "runs": [r["manifest"]["runId"] for r in loaded],
              "logic": [diagnose(r) for r in loaded] if mode != "parameters" else [],
              "rankings": rankings, "excluded": excluded,
              "confidence": "低；需檢查相鄰參數與樣本外穩健性", "parameterRecommendations": [],
              "limitations": ["分析只讀取既有紀錄；不執行回測、不改正式參數。",
                              "沒有完整相鄰參數與樣本外實驗時，不提出最佳參數區間。"]}
    if not rankings:
        report["limitations"].append("沒有足夠可比較候選，未產生績效排名。")
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "analysis-report.json").write_text(json.dumps(report, ensure_ascii=False, sort_keys=True, indent=2, allow_nan=False), encoding="utf-8")
    lines = ["# 內外盤順勢策略分析", "", "## 觀察事實", "", "執行：" + "、".join(report["runs"])]
    for row in report["logic"]:
        lines.append(f"- {row['runId']}：{row['samples']} 筆秒採樣、{row['fixedRi']} 筆固定 Ri、{row['fillRows']} 筆成交。")
    lines += ["", "## 分析推論", "", report["confidence"], "", "## 邏輯修改建議", "", "先檢查資料新鮮度、未知量與委託終結水位。證據與門檻分布見同名 JSON。", "", "## 參數建議", "", "保留核准預設；目前不自動提出或套用新參數。"]
    for row in excluded:
        lines.append(f"- {row['runId']}：" + "；".join(row["reasons"]))
    lines += ["", "## 仍需驗證", ""] + ["- " + item for item in report["limitations"]]
    (destination / "analysis-report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    if rankings:
        with (destination / "parameter-ranking.csv").open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=["rank", "runId", "netPnl", "maximumDrawdown", "parameters", "stress"])
            writer.writeheader()
            writer.writerows(rankings)
    else:
        (destination / "parameter-ranking.csv").unlink(missing_ok=True)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="內外盤策略紀錄分析（不送單、不修改策略）")
    parser.add_argument("--runs", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--mode", choices=["logic", "parameters", "all"], default="all")
    args = parser.parse_args()
    try:
        run_analysis(args.runs, args.output, args.mode)
    except AnalysisError as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(2)
