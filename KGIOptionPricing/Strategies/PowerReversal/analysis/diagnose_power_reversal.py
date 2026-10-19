"""Stream unfinished observation logs. Diagnostics only, never rank or trade."""
import argparse
import hashlib
import json
import re
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

def diagnose(path):
    reasons, failures, totals, seconds = Counter(), Counter(), Counter(), Counter()
    combinations = Counter()
    values = defaultdict(list)
    first = last = previous = None
    rows = 0
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for raw in stream:
            digest.update(raw)
            row = json.loads(raw)
            if row['strategyType'] != 'PowerReversal':
                raise ValueError('Unexpected strategy')
            stamp = re.sub(r'\.(\d+)', lambda m: '.' + m[1][:6].ljust(6,'0'), row['timestamp']).replace('Z', '+00:00')
            at = datetime.fromisoformat(stamp)
            if at.year == 1:
                continue
            if previous and at < previous[0]:
                raise ValueError('Out of order observations')
            if previous:
                seconds[previous[1]] += (at - previous[0]).total_seconds()
            code = row['primaryReasonCode']
            previous = (at, code)
            first = first or at
            last = at
            rows += 1
            reasons[code] += 1
            checks = row['thresholds']
            failed = []
            for check in checks:
                name = check['name']
                totals[name] += 1
                if not check['passed']:
                    failures[name] += 1
                    failed.append(name)
                v = check['observedValue']
                if v is not None and len(values[name]) < 200000:
                    values[name].append(float(v))
            combinations[','.join(failed)] += 1
    distributions = {}
    for name, samples in values.items():
        samples.sort()
        distributions[name] = {str(p):samples[min(len(samples)-1, int((len(samples)-1)*p/100))] for p in (0,10,25,50,75,90,100)}
    return dict(status='DiagnosticOnly', missing=['run-manifest.json', 'run-summary.json'],
                path=str(path.resolve()), sha256=digest.hexdigest(), rows=rows,
                first=str(first), last=str(last), reasons=dict(reasons), reasonSeconds=dict(seconds),
                gates={name:dict(evaluated=n,failed=failures[name],failPercent=100*failures[name]/n) for name,n in totals.items()},
                distributions=distributions, combinations=combinations.most_common(15))

if __name__ == '__main__':
    cli=argparse.ArgumentParser()
    cli.add_argument('--decisions', type=Path, required=True)
    cli.add_argument('--output', type=Path, required=True)
    args=cli.parse_args()
    result=diagnose(args.decisions)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:result[k] for k in ('rows','first','last','reasons','gates')},ensure_ascii=True))
