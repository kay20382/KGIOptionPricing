"""C-only worker. Supervisor owns resource limits and cleanup on every exit."""
import json,time,sys
from pathlib import Path
from sequence_dataset import aggregate_source

def main(root):
    root=Path(root).resolve();config=json.loads((root/'experiment-config.json').read_text(encoding='utf-8'))
    manifests=[];failures=[]
    checkpoint=root/'resume-checkpoint.json'
    if checkpoint.exists():
        import hashlib
        saved=json.loads(checkpoint.read_text(encoding='utf-8'))
        for rel,sha in saved['cacheSha256'].items():
            if hashlib.sha256((root/rel).read_bytes()).hexdigest()!=sha:raise ValueError('Resume cache changed')
        for item in config['sources']:
            st=Path(item['path']).stat()
            if (st.st_size,st.st_mtime_ns)!=(item['bytes'],item['mtimeNs']):raise ValueError('Resume source metadata changed')
        print('RESUME verified small sampled caches; original raw files not reread',flush=True)
        from train_sequence_trend import train_and_evaluate
        train_and_evaluate(root,config,saved['sources']);return
    for i,source in enumerate(config['sources']):
        path=Path(source['path']);st=path.stat()
        if (st.st_size,st.st_mtime_ns)!=(source['bytes'],source['mtimeNs']):raise ValueError('Source changed before extraction')
        print(f"AGGREGATE {i+1}/{len(config['sources'])} {source['date']} {source['role']}",flush=True)
        try:
            m=aggregate_source(path,root/'cache',progress=lambda name,n:print(f'ROWS {name} {n}',flush=True));m['role']=source['role'];manifests.append(m)
            print('SESSIONS',json.dumps(m['sessions']),flush=True)
        except ValueError as e:
            failures.append(dict(source=source['path'],reason=str(e)));print('EXCLUDED',str(e),flush=True)
        (root/'source-manifest.json').write_text(json.dumps(dict(sources=manifests,excluded=failures),ensure_ascii=False,indent=2),encoding='utf-8')
    from train_sequence_trend import train_and_evaluate
    train_and_evaluate(root,config,manifests)

if __name__=='__main__':main(sys.argv[1])
