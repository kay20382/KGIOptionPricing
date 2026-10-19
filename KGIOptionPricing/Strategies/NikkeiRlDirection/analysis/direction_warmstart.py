"""Training-only future direction labels; no execution/PnL model is copied here."""
import datetime
import re
import numpy as np


def parse_at(value):
    trimmed=re.sub(r'(\.\d{6})\d+', r'\1', value)
    return datetime.datetime.strptime(trimmed,'%Y-%m-%dT%H:%M:%S.%f%z' if '.' in trimmed else '%Y-%m-%dT%H:%M:%S%z')


def build_direction_samples(states, horizon_seconds=30, threshold_ticks=2, observation_size=40,decision_interval_seconds=None):
    if horizon_seconds not in [15,30] or threshold_ticks <= 0:
        raise ValueError('Labels require an existing 15/30-second return feature')
    if decision_interval_seconds is not None and (decision_interval_seconds not in [3,5] or horizon_seconds%decision_interval_seconds):
        raise ValueError('Invalid prediction horizon/cadence')
    times = [parse_at(s['at']) for s in states]
    if any(b <= a for a,b in zip(times,times[1:])):
        raise ValueError('Trajectory timestamps must be strictly increasing')
    observations = [np.asarray(s['observation'],dtype=np.float32) for s in states]
    if observation_size not in [40,48,50,56,60] or any(x.shape != (observation_size,) or not np.isfinite(x).all() for x in observations):
        raise ValueError('Invalid observation contract')
    lookup = {t:i for i,t in enumerate(times)}
    x,y = [],[]
    for i,(s,t) in enumerate(zip(states,times)):
        j = lookup.get(t + datetime.timedelta(seconds=horizon_seconds))
        if j is None or t.date() != times[j].date(): continue
        if decision_interval_seconds is not None and (j-i!=horizon_seconds//decision_interval_seconds or any((times[k+1]-times[k]).total_seconds()!=decision_interval_seconds for k in range(i,j))):continue
        if not s['mask'][1] or not s['mask'][2] or observations[i][30] != 0: continue
        if observations[i][39]*3600 <= 310: continue
        if any(observations[k][38] != 1 or states[k].get('status','').startswith('Incomplete')
               for k in range(i,j+1)): continue
        change = observations[j][1 if horizon_seconds==15 else 2]  # Future return is a label, never an actor input.
        x.append(observations[i].copy())
        y.append(1 if change>threshold_ticks else 2 if change < -threshold_ticks else 0)
    return np.asarray(x,dtype=np.float32).reshape(-1,observation_size), np.asarray(y,dtype=np.int64)


def restored_environment(manifest):
    """Old model bundles retain the original defaults."""
    # Historical v1 bundles without this field were trained with 30-second cooldown.
    return dict(manifest.get('environmentConfig', {'cooldownSeconds':30}))
