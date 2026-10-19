"""Candidates56/57/59: completed-minute, trailing5-minute engineering variants.

Not original full-day frequency; not a verified predictor or an enabled actor input.
The caller must prove received-event continuity before marking a bar complete.
"""
import math
from dataclasses import dataclass
from datetime import datetime,timedelta

@dataclass(frozen=True)
class MinuteBar:
    end:datetime
    amount:float
    volume:float
    complete:bool

def minute_candidates(at,bars):
    missing={56:math.nan,57:math.nan,59:math.nan}
    if at.tzinfo is None:raise ValueError('Aware decision time required')
    received=[]
    for bar in bars:
        if bar.end.tzinfo is None:raise ValueError('Aware bar end required')
        if bar.end>at:continue
        if bar.end.second or bar.end.microsecond:raise ValueError('Exact minute boundary required')
        if not math.isfinite(bar.amount) or not math.isfinite(bar.volume) or bar.amount<0 or bar.volume<0:raise ValueError('Nonnegative finite observed amount/volume required')
        received.append(bar)
    if len({bar.end for bar in received})!=len(received):raise ValueError('Duplicate minute end')
    last=at.replace(second=0,microsecond=0)
    needed=[last-timedelta(minutes=4-i) for i in range(5)]
    byend={bar.end:bar for bar in received}
    if any(end not in byend or not byend[end].complete for end in needed):return missing
    selected=[byend[end] for end in needed]
    amounts=[bar.amount for bar in selected];volumes=[bar.volume for bar in selected]
    total=sum(amounts)
    entropy=-sum((a/total)*math.log(a/total) for a in amounts if a>0) if total>0 else math.nan
    low=min(volumes);width=(max(volumes)+1-low)/10
    counts=[0]*10
    for volume in volumes:counts[min(9,int((volume-low)/width))]+=1
    volume_entropy=-sum((n/5)*math.log(n/5) for n in counts if n)
    x=amounts[:-1];y=amounts[1:];mx=sum(x)/4;my=sum(y)/4
    vx=sum((v-mx)**2 for v in x);vy=sum((v-my)**2 for v in y)
    correlation=sum((a-mx)*(b-my) for a,b in zip(x,y))/math.sqrt(vx*vy) if vx>0 and vy>0 else math.nan
    return {56:entropy,57:volume_entropy,59:correlation}
