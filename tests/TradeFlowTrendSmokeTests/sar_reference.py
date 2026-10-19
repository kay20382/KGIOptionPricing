"""Batch Decimal reference for the approved SAR convention; generates test vectors only."""
import json
from decimal import Decimal as D
from pathlib import Path


def reference(bars):
    answer = [None] * len(bars)
    state = None
    for i in range(1, len(bars)):
        high, low, close = map(D, bars[i])
        previous = list(map(D, bars[i - 1]))
        if state is None:
            if close == previous[2]:
                continue
            long = close > previous[2]
            state = [min(low, previous[1]) if long else max(high, previous[0]), long,
                     max(high, previous[0]) if long else min(low, previous[1]), D('.02')]
        else:
            value, long, extreme, af = state
            projected = value + af * (extreme - value)
            if (long and low < projected) or (not long and high > projected):
                state = [extreme, not long, low if long else high, D('.02')]
            else:
                prior = [list(map(D, b)) for b in bars[max(0, i - 2):i]]
                bounded = min([projected] + [b[1] for b in prior]) if long else max([projected] + [b[0] for b in prior])
                updated = max(extreme, high) if long else min(extreme, low)
                state = [bounded, long, updated, min(D('.20'), af + D('.02')) if updated != extreme else af]
        answer[i] = dict(value=str(state[0]), isLong=state[1], extreme=str(state[2]), acceleration=str(state[3]))
    return answer


up = [[str(105 + i * 10), str(95 + i * 10), str(100 + i * 10)] for i in range(16)]
up += [['245', '180', '185'], ['200', '170', '175'], ['240', '172', '235']]
down = [[str(400 - int(low)), str(400 - int(high)), str(400 - int(close))] for high, low, close in up]
equal = [['105', '95', '100'], ['105', '95', '100'], ['110', '99', '108'], ['115', '95.3', '114']]
vectors = [dict(name=name, bars=bars, expected=reference(bars)) for name, bars in [('up-cap-reverse', up), ('down-cap-reverse', down), ('equal-init-boundary', equal)]]
assert vectors[0]['expected'][12]['acceleration'] == '0.20'
assert vectors[0]['expected'][16]['isLong'] is False
Path(__file__).with_name('sar-fixtures.json').write_text(json.dumps(vectors, indent=2) + '\n', encoding='utf-8')
