"""Candidate58: trailing20closed-minute engineering Q; no full-day alpha claim."""
import math
from datetime import timedelta
from minute_factor_calculations import minute_candidates


def minute_smart_money(at, bars):
    bars=list(bars)
    minute_candidates(at,bars)  # Shared received-minute integrity checks.
    last=at.replace(second=0,microsecond=0)
    needed=[last-timedelta(minutes=20-i) for i in range(21)]
    byend={bar.end:bar for bar in bars if bar.end<=at}
    if any(end not in byend or not byend[end].complete for end in needed):
        return {58:math.nan}
    selected=[byend[end] for end in needed]
    valid=[]
    for previous,current in zip(selected,selected[1:]):
        p=getattr(previous,'close',None);c=getattr(current,'close',None)
        if previous.volume<=0 or current.volume<=0 or p is None or c is None:continue
        if not math.isfinite(p+c) or p<=0 or c<=0:
            raise ValueError('Positive finite observed close required')
        valid.append((abs(c/p-1)/current.volume**.25,current.end,c,current.volume))
    # Fixed20 valid returns: do not silently shorten the research window.
    if len(valid)!=20:return {58:math.nan}
    total=sum(v[3] for v in valid)
    ordered=sorted(valid,key=lambda v:(-v[0],v[1]))
    prefix=[];cumulative=0.
    for bar in ordered:
        cumulative+=bar[3]
        if cumulative/total>.2:break
        prefix.append(bar)
    if not prefix:return {58:math.nan}
    subset=sum(v[2]*v[3] for v in prefix)/sum(v[3] for v in prefix)
    allprice=sum(v[2]*v[3] for v in valid)/total
    return {58:subset/allprice-1}  # Fixed centering, not fitted normalization.
