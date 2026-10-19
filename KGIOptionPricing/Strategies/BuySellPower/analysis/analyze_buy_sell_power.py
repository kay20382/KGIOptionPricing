"""Read-only movement CSV diagnostics; no orders, backtests or parameter changes."""
import argparse
from collections import defaultdict
import csv
import json
from pathlib import Path
from statistics import mean

PARAMETERS = ("PowerWindowSeconds", "ShortAverageSeconds", "LongAverageSeconds", "ReversalPoints", "MovementAveragePeriods")

def analyze(path):
    files = sorted(path.rglob("行情段紀錄.csv")) if path.is_dir() else [path]
    if not files:
        raise ValueError("找不到行情段紀錄.csv")
    groups = defaultdict(list)
    for file in files:
        with file.open(encoding="utf-8-sig", newline="") as stream:
            for number, row in enumerate(csv.DictReader(stream), 2):
                key = tuple(row[name] for name in ("Product", "Symbol", "SessionId") + PARAMETERS)
                row["evidence"] = f"{file}:{number}"
                groups[key].append(row)
    output = []
    for key, rows in sorted(groups.items()):
        n = int(key[-1])
        done = [row for row in rows if row["IsCompleted"].lower() == "true"]
        up = [float(row["Points"]) for row in done if row["Direction"] == "上漲"][-n:]
        down = [float(row["Points"]) for row in done if row["Direction"] == "下跌"][-n:]
        output.append({"product": key[0], "symbol": key[1], "session": key[2],
            "parameters": dict(zip(PARAMETERS, key[3:])), "up_samples": len(up), "down_samples": len(down),
            "average_up_points": mean(up) if up else None, "average_down_points": mean(down) if down else None,
            "incomplete_count": len(rows) - len(done), "completed_total": len(done),
            "evidence": [row["evidence"] for row in rows],
            "limitations": ["觀測樣本不足" ] if len(up) < n or len(down) < n else []})
    return {"trading_logic_implemented": False, "groups": output,
        "parameter_recommendations": [], "limitations": ["本版本只提供指標與行情統計，未建立進出場，不能據此推薦交易參數。", "時間為封存事件接收時間；原始成交時刻的日期與時區尚未驗證。"]}

def main():
    parser = argparse.ArgumentParser(description="買賣力道行情段統計（唯讀）")
    parser.add_argument("input", type=Path)
    parser.add_argument("--mode", choices=("logic", "parameters", "all"), default="all")
    parser.add_argument("--output", required=True, type=Path, help="輸出前綴，產生 .json 與 .md")
    args = parser.parse_args()
    result = analyze(args.input)
    result["mode"] = args.mode
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.with_suffix(".json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = ["# 買賣力道歷史統計", "", *result["limitations"], "", "| 商品／合約 | 時段 | 上漲平均／樣本 | 下跌平均／樣本 | 未完成段 |", "|---|---|---|---|---|"]
    for group in result["groups"]:
        def value(key):
            item = group[key]
            return "尚無資料" if item is None else f"{item:g}"
        lines.append(f"| {group['product']}／{group['symbol']} | {group['session']} | {value('average_up_points')}／{group['up_samples']} | {value('average_down_points')}／{group['down_samples']} | {group['incomplete_count']} |")
        lines.extend(["", "生效參數：" + json.dumps(group["parameters"], ensure_ascii=False), ""])
    args.output.with_suffix(".md").write_text("\n".join(lines), encoding="utf-8")
    print(f"已完成 {len(result['groups'])} 組觀測統計：{args.output.with_suffix('.json')}")

if __name__ == "__main__":
    main()
