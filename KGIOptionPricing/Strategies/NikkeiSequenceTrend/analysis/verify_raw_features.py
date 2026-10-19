"""Verify C# raw features on reserved tuning sources only; no holdout reads."""
import json,subprocess
from pathlib import Path
import numpy as np
from execution_alpha import pooled_features
from research_resource_guard import limited_write,safe_cleanup

def verify_raw_sources(root,days):
    by_source={}
    for day in days:by_source.setdefault(day['source'],[]).append(day)
    temp=root/'feature-temp';temp.mkdir(exist_ok=True);reports=[]
    for source,group in by_source.items():
        source=Path(source);before=source.stat()
        if any((before.st_size,before.st_mtime_ns)!=(d['sourceBytes'],d['sourceMtimeNs']) for d in group):raise ValueError('Feature source changed after aggregation')
        request={};expected={}
        for d in group:
            s=d['samples'];eligible=s.inference_indices
            if len(eligible)<4:raise ValueError('Not enough feature parity windows')
            indices=sorted(set(int(eligible[j]) for j in (0,len(eligible)//3,2*len(eligible)//3,len(eligible)-1)))
            request[d['id']]=indices
            for i in indices:expected[(d['id'],i)]=(d,i)
        request_path=temp/'request.json';output=temp/'actual.json'
        limited_write(request_path,json.dumps(request).encode(),root)
        print('RAW_FEATURE_PARITY',source.name,flush=True)
        subprocess.run(['dotnet',str(root/'feature-runtime/NikkeiSequenceFeatureParity.dll'),str(source),str(request_path),str(output)],capture_output=True,text=True,check=True,timeout=600)
        observed=json.loads(output.read_text(encoding='utf-8'));errors={};seen=set()
        for row in observed:
            key=(row['session'],row['index'])
            if key in seen or key not in expected:raise ValueError('Unexpected/duplicate native feature window')
            seen.add(key);d,i=expected[key];s=d['samples']
            if not row['historyEligible'] or not row['valid'] or row['rawSourceSha256']!=d['sourceSha256']:raise ValueError('Native feature quality/source mismatch')
            x,a=s.window(i);targets=dict(sequence=x.flatten(),background=a,pooled=pooled_features(s,[i])[0])
            for name,target in targets.items():
                native=np.asarray(row[name],dtype=target.dtype);error=float(np.max(np.abs(native-target)));errors[name]=max(error,errors.get(name,0))
                if not np.isfinite(native).all() or error>2e-5:raise ValueError('Native raw feature mismatch '+str((key,name,error)))
        if seen!=set(expected):raise ValueError('Missing native feature windows')
        after=source.stat()
        if (before.st_size,before.st_mtime_ns)!=(after.st_size,after.st_mtime_ns):raise ValueError('Feature source changed during replay')
        reports.append(dict(source=str(source),sourceSha256=group[0]['sourceSha256'],sessions=request,maximumAbsoluteErrors=errors,passed=True))
        safe_cleanup(root,[request_path,output])
    result=dict(passed=True,scope='Four eligible raw-history windows per full reliable validation session; absent macro calendar remains Unknown',sources=reports)
    (root/'raw-feature-source-parity.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    safe_cleanup(root,[temp]);return result