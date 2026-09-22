"""離線分析週期雙邊完成單的可重現執行產物；不連線、不下單。"""
from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from pathlib import Path


STRATEGY_TYPE = "PeriodicQueueCycle"
MIN_COMPLETED_TRADES = 10


class AnalysisError(ValueError):
    """輸入證據不完整、不相容或不足時的 fail-closed 錯誤。"""


def _read_json(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise AnalysisError(f"InvalidJson:{path.name}") from error
    if not isinstance(value, dict):
        raise AnalysisError(f"InvalidSchema:{path.name}")
    return value


def _read_jsonl(path: Path, required: bool = True) -> list[dict]:
    if not path.exists():
        if required:
            raise AnalysisError(f"MissingArtifact:{path.name}")
        return []
    rows = []
    try:
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip():
                continue
            value = json.loads(line)
            if not isinstance(value, dict):
                raise AnalysisError(f"InvalidJsonl:{path.name}:{number}")
            rows.append(value)
    except json.JSONDecodeError as error:
        raise AnalysisError(f"InvalidJsonl:{path.name}") from error
    return rows


def _number(value, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise AnalysisError(f"InvalidMetric:{name}")
    return float(value)


def _require(value: dict, key: str, source: str):
    if key not in value or value[key] in (None, ""):
        raise AnalysisError(f"MissingField:{source}:{key}")
    return value[key]


def _load_run(directory: Path) -> dict:
    manifest = _read_json(directory / "run-manifest.json")
    summary = _read_json(directory / "run-summary.json")
    if _require(manifest, "strategyType", "manifest") != STRATEGY_TYPE:
        raise AnalysisError("WrongStrategyType")
    run_id = str(_require(manifest, "runId", "manifest"))
    parameter_hash = str(_require(manifest, "parameterSha256", "manifest"))
    archive = manifest.get("inputArchive") or {}
    if not isinstance(archive, dict) or not archive.get("sha256"):
        raise AnalysisError("MissingArchiveHash")
    universe = manifest.get("universe")
    if not isinstance(universe, list) or not universe or not isinstance(universe[0], dict):
        raise AnalysisError("MissingProductMapping")
    if not isinstance(manifest.get("executionAssumptions"), dict):
        raise AnalysisError("MissingExecutionAssumptions")
    _number(_require(manifest, "commissionPerContract", "manifest"), "commissionPerContract")
    completed = _number(_require(summary, "completedTrades", "summary"), "completedTrades")
    net = _number(_require(summary, "netPnl", "summary"), "netPnl")
    drawdown = _number(_require(summary, "maximumDrawdown", "summary"), "maximumDrawdown")
    decisions = _read_jsonl(directory / "strategy-decisions.jsonl")
    lifecycle = _read_jsonl(directory / "order-lifecycle.jsonl")
    execution_path = directory / "executions.csv"
    if not execution_path.exists():
        raise AnalysisError("MissingArtifact:executions.csv")
    try:
        with execution_path.open(newline="", encoding="utf-8") as source:
            executions = list(csv.DictReader(source))
    except (OSError, csv.Error) as error:
        raise AnalysisError("InvalidCsv:executions.csv") from error
    if not executions or "clientOrderId" not in executions[0]:
        raise AnalysisError("InvalidCsv:executions.csv")
    return {
        "directory": directory, "runId": run_id, "manifest": manifest, "summary": summary,
        "parameterHash": parameter_hash, "archiveHash": str(archive["sha256"]),
        "productMapping": {str(x.get("productId")): str(x.get("symbol")) for x in universe},
        "executionAssumptions": manifest["executionAssumptions"], "codeVersion": manifest.get("codeVersion"),
        "completedTrades": completed, "netPnl": net, "maximumDrawdown": drawdown,
        "decisions": decisions, "lifecycle": lifecycle, "executions": executions,
        "experiments": _read_jsonl(directory / "parameter-experiments.jsonl", required=False),
    }


def _discover_runs(source: Path) -> tuple[Path, list[Path], list[Path]]:
    if (source / "run-manifest.json").exists():
        root = source.parent
        directories = sorted(path for path in root.iterdir()
                             if path.is_dir() and (path / "run-manifest.json").exists())
        return root, directories, [source / "parameter-experiments.jsonl"]
    directories = sorted(path for path in source.iterdir() if path.is_dir() and (path / "run-manifest.json").exists())
    if not directories:
        raise AnalysisError("NoRunArtifacts")
    return source, directories, [path / "parameter-experiments.jsonl" for path in directories]


def _count(rows: list[dict], field: str) -> dict[str, int]:
    return dict(sorted(Counter(str(row.get(field, "Unavailable")) for row in rows).items()))


def _logic_report(runs: list[dict]) -> dict:
    decisions = [row for run in runs for row in run["decisions"]]
    lifecycle = [row for run in runs for row in run["lifecycle"]]
    return {
        "runs": sorted(run["runId"] for run in runs),
        "signalFunnel": _count(decisions, "decision"),
        "primaryReasonCounts": _count(decisions, "primaryReasonCode"),
        "directionCounts": _count(decisions, "direction"),
        "orderLifecycleCounts": _count(lifecycle, "state"),
        "executionCount": sum(len(run["executions"]) for run in runs),
        "dataQualityCounts": dict(sorted(Counter(
            flag for row in decisions for flag in row.get("dataQualityFlags", [])).items())),
    }


def _same(left, right) -> bool:
    return left == right


def _candidate_reasons(experiment: dict, baseline: dict, candidate: dict, used_splits: set[str]) -> list[str]:
    reasons = []
    if experiment.get("strategyType") != STRATEGY_TYPE or candidate["manifest"].get("strategyType") != STRATEGY_TYPE:
        reasons.append("WrongStrategyType")
    if experiment.get("status") != "Valid":
        reasons.append("ExperimentNotValid")
    if experiment.get("codeVersion") != baseline["codeVersion"] or candidate["codeVersion"] != baseline["codeVersion"]:
        reasons.append("IncompatibleCodeVersion")
    if experiment.get("inputArchiveSha256") != baseline["archiveHash"] or candidate["archiveHash"] != baseline["archiveHash"]:
        reasons.append("IncompatibleArchive")
    if not _same(experiment.get("productMapping"), baseline["productMapping"]) or candidate["productMapping"] != baseline["productMapping"]:
        reasons.append("IncompatibleProductMapping")
    if not _same(experiment.get("executionAssumptions"), baseline["executionAssumptions"]) or candidate["executionAssumptions"] != baseline["executionAssumptions"]:
        reasons.append("IncompatibleExecutionAssumptions")
    split = experiment.get("dataSplitId")
    if not isinstance(split, str) or not split:
        reasons.append("MissingDataSplit")
    elif split in used_splits:
        reasons.append("OverlappingDataSplit")
    if candidate["completedTrades"] < MIN_COMPLETED_TRADES:
        reasons.append("InsufficientCompletedTrades")
    if candidate["netPnl"] <= 0:
        reasons.append("NetAfterCostNotPositive")
    if candidate["maximumDrawdown"] > candidate["netPnl"]:
        reasons.append("MaximumDrawdown")
    stress = candidate["summary"].get("adverseSlippageStress", {})
    if not isinstance(stress, dict) or any(_number(stress.get(str(tick)), f"adverseSlippageStress.{tick}") <= 0 for tick in (0, 1, 2)):
        reasons.append("AdverseSlippageGuard")
    quality = candidate["summary"].get("dataQualityCounts", {})
    if not isinstance(quality, dict) or any(_number(value, "dataQualityCounts") > 0 for value in quality.values()):
        reasons.append("DataQualityGuard")
    return sorted(set(reasons))


def _parameter_report(runs: list[dict]) -> tuple[list[dict], list[dict]]:
    by_id = {run["runId"]: run for run in runs}
    experiments = sorted((experiment for run in runs for experiment in run["experiments"]),
                         key=lambda item: (str(item.get("experimentId", "")), str(item.get("candidateRunId", ""))))
    ranking, rejected, used_splits = [], [], set()
    for experiment in experiments:
        experiment_id = str(experiment.get("experimentId", ""))
        baseline = by_id.get(str(experiment.get("baselineRunId", "")))
        candidate = by_id.get(str(experiment.get("candidateRunId", "")))
        if not experiment_id or baseline is None or candidate is None:
            raise AnalysisError("MissingExperimentRun")
        reasons = _candidate_reasons(experiment, baseline, candidate, used_splits)
        split = experiment.get("dataSplitId")
        if not reasons and isinstance(split, str):
            used_splits.add(split)
        row = {"experimentId": experiment_id, "baselineRunId": baseline["runId"],
               "candidateRunId": candidate["runId"], "dataSplitId": split,
               "netPnl": candidate["netPnl"], "maximumDrawdown": candidate["maximumDrawdown"],
               "completedTrades": candidate["completedTrades"]}
        if reasons:
            rejected.append(row | {"reasonCodes": reasons})
        else:
            ranking.append(row | {"score": candidate["netPnl"] - candidate["maximumDrawdown"]})
    ranking.sort(key=lambda item: (-item["score"], -item["netPnl"], item["experimentId"]))
    rejected.sort(key=lambda item: item["experimentId"])
    return ranking, rejected


def _write_outputs(report: dict, output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    (output / "analysis-report.json").write_text(json.dumps(report, ensure_ascii=False, sort_keys=True, indent=2), encoding="utf-8")
    logic = report.get("logic", {})
    rows = ["# 週期雙邊完成單分析", "", "## 觀察事實", "",
            f"- 執行批次：{', '.join(report['runIds']) or '無'}。",
            f"- 訊號漏斗：{json.dumps(logic.get('signalFunnel', {}), ensure_ascii=False, sort_keys=True)}。",
            "", "## 分析推論", "",
            "- 僅以已驗證的執行產物比較；資料品質與風控失敗的候選不列入排序。",
            "", "## 邏輯修改建議", "",
            "- 先針對拒絕原因與資料品質計數最高者改善行情濾網，再擴大樣本。",
            "", "## 參數建議", ""]
    for row in report.get("parameterRanking", []):
        rows.append(f"- {row['experimentId']}（run={row['candidateRunId']}）：淨損益 {row['netPnl']}，回撤 {row['maximumDrawdown']}。")
    if not report.get("parameterRanking"):
        rows.append("- 證據不足或所有候選均未通過防護條件，未提供參數排名。")
    rows += ["", "## 仍需驗證", "", "- 需以未重疊的樣本外資料、0/1/2 ticks 不利滑價與完整資料品質記錄重驗。", ""]
    (output / "analysis-report.md").write_text("\n".join(rows), encoding="utf-8")
    ranking = report.get("parameterRanking", [])
    if len(ranking) >= 2:
        with (output / "parameter-ranking.csv").open("w", newline="", encoding="utf-8") as destination:
            writer = csv.DictWriter(destination, fieldnames=["experimentId", "candidateRunId", "dataSplitId", "netPnl", "maximumDrawdown", "completedTrades", "score"])
            writer.writeheader(); writer.writerows(ranking)


def run_analysis(source: str | Path, output: str | Path, mode: str = "all") -> dict:
    if mode not in {"logic", "parameters", "all"}:
        raise AnalysisError("InvalidMode")
    _, directories, _ = _discover_runs(Path(source))
    runs = [_load_run(directory) for directory in directories]
    report = {"strategyType": STRATEGY_TYPE, "runIds": sorted(run["runId"] for run in runs), "mode": mode,
              "logic": _logic_report(runs) if mode in {"logic", "all"} else {},
              "parameterRanking": [], "rejectedCandidates": []}
    if mode in {"parameters", "all"}:
        report["parameterRanking"], report["rejectedCandidates"] = _parameter_report(runs)
    _write_outputs(report, Path(output))
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--mode", choices=("logic", "parameters", "all"), default="all")
    args = parser.parse_args()
    try:
        report = run_analysis(args.input, args.output, args.mode)
    except AnalysisError as error:
        print(f"分析未完成：{error}")
        return 2
    if args.mode in {"parameters", "all"} and not report["parameterRanking"]:
        print("分析未完成：InsufficientValidCandidates")
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
