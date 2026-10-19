"""One supervised C: job across aggregation, labels and fitting; staged source pins."""
import sys,json,hashlib,time,gc
from pathlib import Path
from sequence_dataset import aggregate_source
from research_resource_guard import guarded_run

def worker(root):
    config=json.loads((root/'experiment-config.json').read_text(encoding='utf-8'));manifests=[];failures=[]
    pins={name:hashlib.sha256((root/'source'/name).read_bytes()).hexdigest() for name in ('sequence_dataset.py','micro_factors.py','session_context.py','research_resource_guard.py')}
    (root/'aggregation-source-sha256.json').write_text(json.dumps(pins,indent=2))
    for item in config['sources']:
        print('AGGREGATE',item['date'],item['role'],flush=True)
        try:
            m=aggregate_source(item['path'],root/'cache',ordering_policy='quarantine',features_v2=True,progress=lambda name,n:print('RAW',name,n,flush=True))
            m.update(role=item['role'],requestedDate=item['date'],riskReplayReliable=m['ordering']==0)
            manifests.append(m)
            print('SOURCE_DONE',item['date'],'ordering',m['ordering'],'frames',sum(s['frames'] for s in m['sessions']),flush=True)
        except (ValueError,json.JSONDecodeError) as e:
            failures.append(dict(source=item['path'],date=item['date'],reason=str(e)))
            print('SOURCE_REJECTED',item['date'],str(e),flush=True)
        (root/'source-manifest.json').write_text(json.dumps(dict(sources=manifests,excluded=failures),indent=2));gc.collect()
    print('AGGREGATION_READY',flush=True)
    # Modelling sources may be added before fitting; aggregation modules cannot change.
    while not (root/'fit-ready.json').exists():time.sleep(2)
    if any(hashlib.sha256((root/'source'/name).read_bytes()).hexdigest()!=sha for name,sha in pins.items()):raise RuntimeError('Aggregation source changed')
    from fit_execution_alpha import fit
    fit(root,config,manifests)

if __name__=='__main__':
    root=Path(sys.argv[1]).resolve()
    if '--worker' in sys.argv:worker(root)
    else:print(json.dumps(guarded_run([sys.executable,str(root/'source/run_alpha_experiment.py'),str(root),'--worker'],root),indent=2))