"""Read-only Taiwan postmarket theme breadth and news evidence helpers."""
from __future__ import annotations
import csv
from dataclasses import dataclass
from pathlib import Path

@dataclass(frozen=True)
class Theme: first: str; second: str
@dataclass(frozen=True)
class StockMove: code: str; name: str; change_pct: float; turnover: float
@dataclass(frozen=True)
class ThemeSummary:
    first: str; advancers: int; decliners: int; unchanged: int; average_change_pct: float; turnover: float; stock_codes: tuple[str,...]
@dataclass(frozen=True)
class AggregationResult: summaries: dict[str,ThemeSummary]; unmapped_codes: list[str]

def load_theme_mapping(path: Path) -> dict[str,list[Theme]]:
    result: dict[str,list[Theme]]={}
    with path.open(encoding='utf-8-sig',newline='') as f:
        for row in csv.DictReader(f):
            first=(row.get('一階題材') or '').strip(); second=(row.get('二階細分題材') or '').strip()
            for item in (row.get('對應股票') or '').replace('、',',').split(','):
                code=item.strip().split(' ',1)[0]
                if code.isdigit() and first: result.setdefault(code,[]).append(Theme(first,second))
    return result

def aggregate_theme_breadth(stocks, mapping) -> AggregationResult:
    buckets={}; unmapped=[]
    for stock in stocks:
        themes=mapping.get(stock.code,[])
        if not themes: unmapped.append(stock.code); continue
        for first in {theme.first for theme in themes}:
            buckets.setdefault(first,[]).append(stock)
    summaries={}
    for first,members in buckets.items():
        if len(members)<2: continue
        summaries[first]=ThemeSummary(first,sum(s.change_pct>0 for s in members),sum(s.change_pct<0 for s in members),sum(s.change_pct==0 for s in members),sum(s.change_pct for s in members)/len(members),sum(s.turnover for s in members),tuple(sorted(s.code for s in members)))
    return AggregationResult(dict(sorted(summaries.items(),key=lambda item:(-max(item[1].advancers,item[1].decliners),-item[1].turnover,-abs(item[1].average_change_pct),item[0]))),sorted(unmapped))
