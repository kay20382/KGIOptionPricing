"""Keep exact normalized market rows and audit excluded recorder metadata."""
import argparse
import collections
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
source, output = Path(args.source), Path(args.output)
before = source.stat()
digest = hashlib.sha256()
counts = collections.Counter()
max_dropped = 0
with source.open('rb') as reader, output.open('xb') as writer:
    for raw in reader:
        digest.update(raw)
        row = json.loads(raw)
        kind = row['Kind']
        max_dropped = max(max_dropped, row.get('Dropped', 0))
        counts[kind] += 1
        if kind == 'trade' or kind.startswith('depth'):
            if row['Product'] in ['225MC', '225M', '225']:
                writer.write(raw)
        elif kind not in ['session', 'status', 'contract', 'api_packet']:
            raise ValueError(f'Unknown recorder row kind: {kind}')
after = source.stat()
if before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns:
    raise ValueError('Source changed during extraction')
if max_dropped:
    raise ValueError('Recorder dropped events; reject input')
result = dict(source=str(source.resolve()), sourceSha256=digest.hexdigest().upper(),
              counts=dict(counts), maxDropped=max_dropped, output=str(output.resolve()),
              limitations='Exact normalized market rows preserved; api_packet is parallel raw log, metadata excluded')
output.with_suffix('.extraction.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result), flush=True)
