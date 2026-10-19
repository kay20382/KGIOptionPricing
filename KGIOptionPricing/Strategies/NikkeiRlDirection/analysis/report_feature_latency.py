"""Audit F1 frozen equivalence and separate 10 ms latency sensitivity."""
import argparse
import gzip
import hashlib
import json
import math
from pathlib import Path
from feature_experiments import SEEDS, COHORT
from fill_diagnostics import reconcile_episode
from performance_metrics import episode_metrics


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True)
    parser.add_argument('--suite',choices=['features','papers'],default='features')
    args=parser.parse_args();root=Path(args.root)
    variant='P1' if args.suite=='papers' else 'F1'
    models=root/('models/P1' if args.suite=='papers' else 'verified/F1')
    primary=json.loads((models/'backtest-report.json').read_text())
    rows=[];audits=[]
    for latency in [1,10]:
        directory=root/f'frozen-{variant}-{latency}ms'
        report=json.loads((directory/'backtest-multiday.json').read_text())
        if report['addedEpisodes']!=COHORT or report['latencyMs']!=latency or not report['modelsFrozen'] or [r['seed'] for r in report['scenarios']]!=SEEDS:
            raise ValueError('Wrong frozen cohort/configuration')
        for scenario in report['scenarios']:
            seed=scenario['seed']
            if scenario['latencyMs']!=latency or [r['episode'] for r in scenario['episodes']]!=COHORT:
                raise ValueError('Wrong scenario latency/cohort')
            if latency==1 and scenario['episodes']!=next(r['episodes'] for r in primary['scenarios'] if r['seed']==seed):
                raise ValueError('Frozen 1 ms differs from primary replay')
            fingerprints={p.name:hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in [models/f'policy-seed{seed}.onnx',models/f'ppo-seed{seed}.zip',models/f'normalizer-seed{seed}.pkl']}
            if report['fingerprints'][str(seed)]!=fingerprints: raise ValueError('Frozen bundle changed')
            totals={}
            with gzip.open(directory/f'feedback-seed{seed}-delay{latency}-cost0.jsonl.gz','rt',encoding='utf-8') as source:
                for line in source:
                    transition=json.loads(line);end=transition['next']
                    total=totals.setdefault(transition['episode'],dict(delta=0,reward=0,fills=[],transitions=0))
                    total['delta']+=end['deltaEquity'];total['reward']+=end['reward'];total['fills'].extend(end['fills'])
                    total['last']=end;total['transitions']+=1
            if sorted(totals)!=COHORT: raise ValueError('Missing sensitivity feedback')
            for episode in scenario['episodes']:
                total=totals[episode['episode']];end=total.pop('last')
                if not math.isclose(total['delta'],end['netPnl'],abs_tol=1e-6) or not math.isclose(total['reward'],end['totalReward'],abs_tol=1e-6):
                    raise ValueError('Sensitivity reward/ledger mismatch')
                outcomes=reconcile_episode(episode,end,total.pop('fills'))
                metric=episode_metrics(episode,10)
                if metric['costsJPY']!=metric['completedTrades']*72: raise ValueError('Sensitivity fee mismatch')
                rows.append(dict(seed=seed,episode=episode['episode'],latencyMs=latency,**metric))
                audits.append(dict(seed=seed,episode=episode['episode'],latencyMs=latency,fillTrades=len(outcomes),**total))
    comparisons=[]
    for row in [r for r in rows if r['latencyMs']==10]:
        base=next(r for r in rows if r['latencyMs']==1 and r['seed']==row['seed'] and r['episode']==row['episode'])
        comparisons.append(dict(seed=row['seed'],episode=row['episode'],tradesDelta=row['completedTrades']-base['completedTrades'],
            grossDeltaJPY=row['grossPnlJPY']-base['grossPnlJPY'],netDeltaJPY=row['netPnlJPY']-base['netPnlJPY']))
    result=dict(variant=variant,primaryLatencyMs=1,sensitivityLatencyMs=10,frozenPrimaryExactlyEqual=True,
        perDate=rows,comparisons=comparisons,feedbackAudits=audits,selectionUse=False,deploymentEligible=False,
        scope='Full causal replay; delay changes fills and subsequent policy feedback, not pure same-order slippage')
    (root/'latency-sensitivity.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(dict(episodes=len(rows),frozenPrimaryExactlyEqual=True,totalNetDeltaJPY=sum(r['netDeltaJPY'] for r in comparisons))))


if __name__=='__main__': main()
