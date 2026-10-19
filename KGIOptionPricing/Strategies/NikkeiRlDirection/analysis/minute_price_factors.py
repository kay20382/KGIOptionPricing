"""55true-amount path illiquidity and60centered close/volume ratio;5closed minutes."""
import math
from dataclasses import dataclass
from datetime import timedelta
from minute_factor_calculations import MinuteBar,minute_candidates

@dataclass(frozen=True)
class PriceMinuteBar(MinuteBar):
    open:float|None=None
    high:float|None=None
    low:float|None=None
    close:float|None=None

def minute_price_candidates(at,bars):
    bars=list(bars)
    # Shared integrity guard: timezone, exact boundaries, duplicates, finite amounts.
    minute_candidates(at,bars)
    missing={55:math.nan,60:math.nan};last=at.replace(second=0,microsecond=0)
    required=[last-timedelta(minutes=4-i) for i in range(5)]
    byend={bar.end:bar for bar in bars if bar.end<=at}
    if any(end not in byend or not byend[end].complete for end in required):return missing
    selected=[byend[end] for end in required];valid=[]
    for bar in selected:
        if bar.volume==0:
            if bar.amount!=0:raise ValueError('Zero volume cannot have observed positive amount')
            continue
        values=[bar.open,bar.high,bar.low,bar.close]
        if any(v is None or not math.isfinite(v) or v<=0 for v in values) or bar.amount<=0 or not bar.low<=min(bar.open,bar.close)<=max(bar.open,bar.close)<=bar.high:
            raise ValueError('Positive consistent observed OHLC required')
        if not bar.low-1e-9<=bar.amount/bar.volume<=bar.high+1e-9:raise ValueError('True amount inconsistent with recorded OHLC')
        valid.append(bar)
    if not valid:return missing
    # Fixed225MC multiplier10JPY/point. No approximation with close*volume.
    out={55:sum((2*(bar.high-bar.low)-abs(bar.close-bar.open))/(10*bar.amount) for bar in valid),60:math.nan}
    # No carry-forward close for an empty minute. Zero observed volume is retained
    # for55with valid-bar exclusion;60needs the same5observed closes.
    if len(valid)==5:
        total=sum(bar.volume for bar in valid)
        ratio=sum(bar.close*bar.volume for bar in valid)/total/(sum(bar.close for bar in valid)/5)
        out[60]=ratio-1 # Fixed centering, not train/validation normalization.
    return out
