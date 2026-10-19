"""Independently audit recorded reward/equity sums and model manifests."""
import argparse
import gzip
import hashlib
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--run', required=True)
args = parser.parse_args()
run = Path(args.run)
files = list(run.glob('feedback-*.jsonl.gz'))
assert len(files) == 50, '45 stress plus 5 measured-latency episodes required'
transitions = 0
for path in files:
    delta = reward = 0.0
    with gzip.open(path, 'rt', encoding='utf-8') as source:
        for line in source:
            end = json.loads(line)['next']
            delta += end['deltaEquity']
            reward += end['reward']
            transitions += 1
    assert end['status'] == 'CompleteEpisode' and end['position'] == 0, path
    assert math.isclose(delta, end['netPnl'], abs_tol=1e-7), path
    assert math.isclose(reward, end['totalReward'], abs_tol=1e-7), path
manifests = list(run.glob('policy-*.manifest.json'))
assert len(manifests) == 5
for path in manifests:
    manifest = json.loads(path.read_text(encoding='utf-8'))
    model = path.with_suffix('').with_suffix('.onnx')
    assert hashlib.sha256(model.read_bytes()).hexdigest().upper() == manifest['modelSha256']
    assert len(manifest['featureNames']) == 40
print(f'Feedback accounting PASS {len(files)} episodes / {transitions} transitions')
print('Five model manifests SHA/schema PASS')
