"""Read shared run artifacts; never mutate parameters, run strategies or submit orders."""
import argparse
import csv
import hashlib
import gzip
import json
import math
import sys
from collections import Counter
from datetime import datetime
from pathlib import Path


class AnalysisError(ValueError):
    pass


ADAPTIVE_DEFAULTS = dict(AdaptiveExitMode="0",StopMovementFraction="0.5",TargetMovementFraction="1",AdaptiveMinStopTicks="2",AdaptiveMaxStopTicks="12",AdaptiveMaxTargetTicks="40")
ADAPTIVE_DEFAULTS.update(AdaptiveForceMode="0",AdaptiveAgreementMode="0",AdaptiveHoldingMode="0",AdaptiveLookbackSeconds="300",AdaptiveMinSamples="120",AdaptiveForcePercentile="70",AdaptiveHoldingFraction="0.5",AdaptiveMinHoldingSeconds="60")
ADAPTIVE_DEFAULTS.update(PowerExitMode="0",ExitWeakPowerThreshold="10",TrailingExitMode="0",TrailingActivateRisk="1",TrailingDistanceRisk="0.5")
ADAPTIVE_DEFAULTS.update(EntryExecutionMode="0",WeightedMidMode="0")
RANGES = {
    "EntryExecutionMode": (0,1), "WeightedMidMode": (0,1),
    "PowerExitMode": (0,2), "ExitWeakPowerThreshold": (-100,100), "TrailingExitMode": (0,1),
    "TrailingActivateRisk": (.1,5), "TrailingDistanceRisk": (.1,3),
    "AdaptiveForceMode": (0,1), "AdaptiveAgreementMode": (0,1), "AdaptiveHoldingMode": (0,1),
    "AdaptiveLookbackSeconds": (60,3600), "AdaptiveMinSamples": (10,3600), "AdaptiveForcePercentile": (50,95),
    "AdaptiveHoldingFraction": (.1,3), "AdaptiveMinHoldingSeconds": (1,86400),
    "AdaptiveExitMode": (0,1), "StopMovementFraction": (.05,3), "TargetMovementFraction": (.05,5),
    "AdaptiveMinStopTicks": (1,1000), "AdaptiveMaxStopTicks": (1,1000), "AdaptiveMaxTargetTicks": (1,2000),
    "PowerWindowSeconds": (1, 3600), "ShortAverageSeconds": (1, 86400), "LongAverageSeconds": (1, 86400),
    "ReversalPoints": (.01, 10000), "MovementAveragePeriods": (1, 10000), "PowerThreshold": (0, 100),
    "RequireShortAverageDirection": (0, 1),
    "AllowRiskRewardPullback": (0, 1),
    "SignalMode": (0, 1), "StopTicks": (1, 1000), "TargetTicks": (1, 1000), "CooldownSeconds": (1, 3600),
    "RequireOppositeMovement": (0, 1),
    "RequireAlignedMovement": (0, 1),
    "PriceRecoveryTicks": (0, 1000),
    "MinJointPower": (0,100), "MaxPowerDifference": (0,200), "MinShortAveragePower": (0,100),
    "BaisThreshold": (0, 200), "ConfirmSeconds": (1, 300), "MovementRatio": (0, 10), "MinMovementSamples": (1, 10000),
    "MinQuantity": (1, 10000000), "MinTradeCount": (1, 100000), "MaxDataAgeSeconds": (.1, 60),
    "MaxSkewSeconds": (0, 60), "MaxSpreadTicks": (1, 100), "MinRewardRisk": (.1, 20), "StopBufferTicks": (1, 100),
    "SlippageTicks": (0, 100), "CommissionPerSide": (0, 100000), "MaxHoldingSeconds": (1, 86400),
    "ExitConfirmSeconds": (1, 300), "EntryTimeoutSeconds": (1, 300), "CloseBeforeSeconds": (1, 3600),
}


def read(path, lines=False):
    if lines:
        return list(iter_records(path))
    try:
        text = path.read_text(encoding="utf-8-sig")
        def invalid(value):
            raise ValueError("非有限數值 " + value)
        return [json.loads(row, parse_constant=invalid) for row in text.splitlines() if row.strip()] if lines else json.loads(text, parse_constant=invalid)
    except (OSError, ValueError) as exc:
        raise AnalysisError(f"資料缺失或損壞 {path}: {exc}") from exc


def iter_records(path):
    """Read preserved JSONL or lossless gzip; bound memory to individual rows."""
    actual=path if path.exists() else Path(str(path)+'.gz')
    opener=gzip.open if actual.suffix=='.gz' else open
    def invalid(value):raise ValueError('非有限數值 '+value)
    try:
        with opener(actual,'rt',encoding='utf-8-sig') as stream:
            for line in stream:
                if line.strip():yield json.loads(line,parse_constant=invalid)
    except (OSError,UnicodeError,ValueError,EOFError) as exc:
        raise AnalysisError(f"資料缺失或損壞 {path}: {exc}") from exc


def validate_parameters(values):
    values = dict(values)
    for key,default in ADAPTIVE_DEFAULTS.items():values.setdefault(key,default)
    values.setdefault("RequireShortAverageDirection", "1")  # Legacy manifests used the direction filter.
    values.setdefault("AllowRiskRewardPullback", "0")
    for key,default in [('MinJointPower','0'),('MaxPowerDifference','200'),('MinShortAveragePower','0')]:values.setdefault(key,default)
    for key, default in (("SignalMode", "0"), ("StopTicks", "6"), ("TargetTicks", "16"), ("CooldownSeconds", "180"), ("RequireOppositeMovement", "0"), ("RequireAlignedMovement", "0"), ("PriceRecoveryTicks", "0")):
        values.setdefault(key, default)
    try:
        for key, (lo, hi) in RANGES.items():
            value = float(values[key])
            if key in ("EntryExecutionMode", "WeightedMidMode") and value != int(value):raise ValueError(key + " 必須整數")
            if not math.isfinite(value) or not lo <= value <= hi:
                raise ValueError(key)
            if key.endswith("Seconds") and key not in ("MaxDataAgeSeconds", "MaxSkewSeconds") or key in ("MovementAveragePeriods", "MinMovementSamples", "MinQuantity", "MinTradeCount", "StopBufferTicks", "SlippageTicks", "RequireShortAverageDirection", "AllowRiskRewardPullback", "SignalMode", "StopTicks", "TargetTicks", "RequireOppositeMovement", "RequireAlignedMovement", "PriceRecoveryTicks", "AdaptiveExitMode", "AdaptiveMinStopTicks", "AdaptiveMaxStopTicks", "AdaptiveMaxTargetTicks", "AdaptiveForceMode", "AdaptiveAgreementMode", "AdaptiveHoldingMode", "AdaptiveMinSamples", "PowerExitMode", "TrailingExitMode"):
                if value != int(value):
                    raise ValueError(key + " 必須整數")
        if float(values["EntryExecutionMode"])==1 and float(values["SignalMode"])!=1:raise ValueError("加權中價混合進場需連續力道模式")
        if float(values["AdaptiveExitMode"])==1 and float(values["SignalMode"])!=1 or float(values["AdaptiveMinStopTicks"])>float(values["AdaptiveMaxStopTicks"]):
            raise ValueError("自適應出場模式或停損上下限矛盾")
        if any(float(values[k])==1 for k in ("AdaptiveForceMode","AdaptiveAgreementMode","AdaptiveHoldingMode")) and float(values["SignalMode"])!=1:
            raise ValueError("自適應參數需要連續力道模式")
        if float(values["AdaptiveMinSamples"])>float(values["AdaptiveLookbackSeconds"]):
            raise ValueError("自適應樣本數超過每秒取樣容量")
        if float(values["AdaptiveHoldingMode"])==1 and float(values["AdaptiveMinHoldingSeconds"])>float(values["MaxHoldingSeconds"]):
            raise ValueError("自適應持倉上下限矛盾")
        if float(values["ShortAverageSeconds"]) > float(values["LongAverageSeconds"]):
            raise ValueError("短期不可大於長期")
        if float(values["MinMovementSamples"]) > float(values["MovementAveragePeriods"]) or float(values["MaxSkewSeconds"]) > float(values["MaxDataAgeSeconds"]):
            raise ValueError("樣本／時間差門檻矛盾")
        if float(values["RequireAlignedMovement"])==1 and float(values["RequireOppositeMovement"])==1:
            raise ValueError("同向行情與反向行情濾網不可同時啟用")
        if float(values["PriceRecoveryTicks"])>0 and (float(values['SignalMode'])!=1 or float(values['RequireOppositeMovement'])!=1):
            raise ValueError("價格回復濾網需使用連續力道模式與反向行情濾網")
    except (KeyError, TypeError, ValueError) as exc:
        raise AnalysisError(f"有效參數不完整或超出範圍: {exc}") from exc


def validate_splits(splits):
    ranges = []
    try:
        for split in splits:
            start = datetime.fromisoformat(split["start"].replace("Z", "+00:00"))
            end = datetime.fromisoformat(split["end"].replace("Z", "+00:00"))
            if not start.tzinfo or not end.tzinfo or start >= end or split["kind"] not in ("train", "validation", "out-of-sample"):
                raise ValueError("起迄、時區或切分名稱無效")
            ranges.append((start, end))
        ranges.sort()
        if any(a[1] > b[0] for a, b in zip(ranges, ranges[1:])):
            raise ValueError("資料切分重疊")
    except (KeyError, TypeError, ValueError) as exc:
        raise AnalysisError(f"資料切分錯誤: {exc}") from exc


def load_run(folder, strategy_type="PowerReversal", parameter_validator=None):
    parameter_validator = parameter_validator or validate_parameters
    m = read(folder / "run-manifest.json")
    if m.get("strategyType") != strategy_type or m.get("schemaVersion", 1) != 1:
        raise AnalysisError("策略類型或資料版本不符")
    values = m.get("effectiveParameters", {})
    parameter_validator(values)
    normalized = m.get("normalizedParameters", "")
    try:
        if json.loads(normalized) != values or hashlib.sha256(normalized.encode()).hexdigest().upper() != str(m.get("parameterSha256", "")).upper():
            raise ValueError("參數雜湊／標準化字典不符")
        if float(m["commissionPerContract"]) != float(values["CommissionPerSide"]):
            raise ValueError("帳本費用與訊號成本不一致")
    except (KeyError, TypeError, ValueError) as exc:
        raise AnalysisError(str(exc)) from exc
    if (folder/'retention-status.json').exists():
        marker=read(folder/'retention-status.json');summary=read(folder/'run-summary.json')
        if marker.get('mode')!='performance-only' or marker.get('userRequested') is not True or marker.get('verifiedBeforeDeletion') is not True or marker.get('parameterSha256')!=m.get('parameterSha256') or marker.get('inputArchiveSha256')!=m.get('inputArchive',{}).get('sha256') or marker.get('completedTrades')!=summary.get('completedTrades') or marker.get('netPnl')!=summary.get('netPnl'):
            raise AnalysisError('績效保留標記與已核對的績效／參數／輸入不符')
        return dict(manifest=m,summary=summary,decisions=None,orders=None,experiments=[],fills=None,performanceOnly=True)
    run = {"manifest": m, "summary": read(folder / "run-summary.json"), "decisions": [dict(primaryReasonCode=row.get('primaryReasonCode','MissingReason')) for row in iter_records(folder / "strategy-decisions.jsonl")],
           "orders": read(folder / "order-lifecycle.jsonl", True), "experiments": read(folder / "parameter-experiments.jsonl", True)}
    for experiment in run["experiments"]:
        validate_splits(experiment.get("dataSplits", []))
        parameter_validator(experiment.get("candidateParameters", {}))
    try:
        with (folder / "executions.csv").open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            if not {"timestamp", "instanceId", "role", "price", "quantity", "commission"}.issubset(reader.fieldnames or []):
                raise ValueError("成交欄位不足")
            run["fills"] = list(reader)
            for row in run["fills"]:
                if not all(math.isfinite(float(row[key])) for key in ("price", "quantity", "commission")):
                    raise ValueError("成交非有限數值")
    except (OSError, ValueError, KeyError) as exc:
        raise AnalysisError(f"成交 CSV 缺失／損壞: {exc}") from exc
    return run


def comparable(a, b):
    fingerprint = a.get("inputArchive", {}).get("sha256")
    return bool(fingerprint) and fingerprint == b.get("inputArchive", {}).get("sha256") and all(a.get(k) is not None and a.get(k) == b.get(k) for k in ("universe", "commissionPerContract", "executionAssumptions"))


def analyze(runs, mode="all", strategy_type="PowerReversal"):
    reports = []
    for run in runs:
        m, summary = run["manifest"], run["summary"]
        reasons = Counter(summary.get('primaryReasonCounts',{})) if run.get('performanceOnly') else Counter(row.get("primaryReasonCode", "MissingReason") for row in run["decisions"])
        issues = []
        if run.get('performanceOnly'):issues.append('逐筆紀錄已依使用者要求刪除；僅能分析保留績效與彙總，無法重新稽核成交、延遲及個別訊號')
        if (summary.get("completedTrades") or 0) < 30:
            issues.append("完成交易不足30筆，無法判斷策略期望值")
        if m.get("executionAssumptions", {}).get("synthetic") == "true":
            issues.append("合成測試資料，不代表市場績效")
        if not m.get("inputArchive", {}).get("sha256"):
            issues.append("缺少輸入封存指紋")
        valid_experiments = [e for e in run["experiments"] if e.get("status") == "Valid" and e.get("dataSplits") and any(s.get("kind") == "out-of-sample" for s in e["dataSplits"])]
        if not valid_experiments:
            issues.append("尚無具樣本外切分的有效參數實驗，不推薦最佳參數")
        advice = []
        if reasons.get("DATA_GATE"):
            advice.append("先分解暖機、口數、筆數、時間差與價差拒絕原因，確認資料完整性")
        if reasons.get("INSUFFICIENT_ROOM"):
            advice.append("比較30點反轉確認消耗的空間與停損距離，勿只為增加交易而降低報酬風險門檻")
        if reasons.get("REVERSAL_UNARMED"):
            advice.append("檢查力道領先反轉的時間分布，再以獨立日期比較確認秒數")
        reports.append({"runId": m.get("runId"), "status": "Incomplete" if issues else "ReadyForReview", "limitations": issues,
                        "logic": {"reasonCounts": dict(sorted(reasons.items())), "thresholdFunnel": summary.get("thresholdFunnel", {}), "recommendations": advice,
                                  "movementFilter": {"required": m.get("effectiveParameters", {}).get("RequireOppositeMovement", "0") == "1",
                                                     "rejectedObservations": summary.get("secondaryReasonCounts", {}).get("MovementOpposesPower", 0)},
                                  "alignedMovementFilter": {"required": m.get("effectiveParameters", {}).get("RequireAlignedMovement", "0") == "1",
                                                            "rejectedObservations": summary.get("secondaryReasonCounts", {}).get("MovementAlignedPower", 0)},
                                  "priceRecoveryFilter": {"minimumTicks": int(m.get("effectiveParameters", {}).get("PriceRecoveryTicks", "0")),
                                                          "rejectedObservations": summary.get("secondaryReasonCounts", {}).get("PriceRecoveryTicks", 0)},
                                  "consensusFilter": {"minimumJointPower":float(m['effectiveParameters'].get('MinJointPower','0')),
                                                      "maximumPowerDifference":float(m['effectiveParameters'].get('MaxPowerDifference','200')),
                                                      "minimumShortAveragePower":float(m['effectiveParameters'].get('MinShortAveragePower','0')),
                                                      "rejectedObservations":{k:summary.get('secondaryReasonCounts',{}).get(k,0) for k in ['JointBuyPower','PowerDifference','225M.SustainedPower','225MC.SustainedPower']}}} if mode != "parameters" else None,
                        "metrics": {key: summary.get(key) for key in ("completedTrades", "netPnl", "winRate", "maximumTradeLoss", "maximumIntradayLoss", "maximumDrawdown", "adverseSlippageStress", "mfePoints", "maePoints")},
                        "parameterExperiments": valid_experiments if mode != "logic" else None})
    comparisons = [{"a": a["manifest"].get("runId"), "b": b["manifest"].get("runId"), "comparable": comparable(a["manifest"], b["manifest"])} for i, a in enumerate(runs) for b in runs[i+1:]]
    return {"strategyType": strategy_type, "baisPowerUnit": "percentage points", "mode": mode, "runs": reports, "comparisons": comparisons,
            "ranking": None, "rankingReason": "完整切分、護欄及足夠樣本驗證後才可排名；本工具不自動更新策略"}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--runs", required=True, type=Path); p.add_argument("--output", required=True, type=Path)
    p.add_argument("--mode", choices=("all", "logic", "parameters"), default="all"); args = p.parse_args()
    try:
        folders = [args.runs] if (args.runs / "run-manifest.json").exists() else sorted({x.parent for x in args.runs.rglob("run-manifest.json")})
        if not folders:
            raise AnalysisError("找不到共用執行產物")
        result = analyze([load_run(x) for x in folders], args.mode); args.output.mkdir(parents=True, exist_ok=True)
        (args.output / "analysis-report.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        lines = ["# 買賣力道反轉策略分析", "", "BaisPower 為短期減長期平均，單位百分點。", ""]
        for r in result["runs"]:
            lines += [f"## {r['runId']}：{r['status']}", "", *["- " + x for x in r["limitations"]], "", "共用績效：" + json.dumps(r["metrics"], ensure_ascii=False), ""]
            if r["logic"]:
                lines += ["拒絕與決策：" + json.dumps(r["logic"]["reasonCounts"], ensure_ascii=False), "", *["- " + x for x in r["logic"]["recommendations"]], ""]
        lines += [result["rankingReason"]]
        (args.output / "analysis-report.md").write_text("\n".join(lines), encoding="utf-8"); print("分析完成；請依限制解讀結果。")
    except AnalysisError as exc:
        print(str(exc), file=sys.stderr); return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
