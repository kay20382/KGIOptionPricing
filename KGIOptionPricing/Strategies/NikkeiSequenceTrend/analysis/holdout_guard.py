"""Exclusive persistent holdout claim shared by all experiment rounds."""
import json,re
from pathlib import Path
from datetime import datetime,timezone
def claim_holdout(base,date,symbol,run,details):
 if not re.fullmatch(r'\d{8}',date):raise ValueError('Invalid holdout date')
 datetime.strptime(date,'%Y%m%d')
 if not re.fullmatch(r'[A-Za-z0-9_-]+',symbol):raise ValueError('Invalid holdout symbol')
 directory=Path(base)/'holdout-consumption';directory.mkdir(exist_ok=True)
 marker=directory/(symbol+'-'+date+'.json')
 # Exclusive create is the claim. Never remove it, even when reading fails.
 with marker.open('x',encoding='utf-8') as f:
  json.dump(dict(date=date,symbol=symbol,run=str(run),claimedAtUtc=datetime.now(timezone.utc).isoformat(),details=details),f,indent=2)
 return marker