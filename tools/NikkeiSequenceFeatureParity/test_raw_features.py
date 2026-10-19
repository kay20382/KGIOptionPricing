import sys,json,tempfile,subprocess,hashlib
from pathlib import Path
from datetime import datetime,timedelta
import numpy as np
root=Path(sys.argv[1]);sys.path.insert(0,str(root/'source'))
from sequence_dataset import aggregate_source,build_samples
from execution_alpha import pooled_features
from research_resource_guard import safe_cleanup

def main():
    temp=root/'feature-test';temp.mkdir(exist_ok=True);results=[]
    for date in ('2026-09-28T21:20:00+08:00','2026-11-02T22:20:00+08:00','2026-12-25T22:20:00+08:00','2026-09-28T07:45:00+08:00'):
        at=datetime.fromisoformat(date);rows=[];seq=0
        for j in range(7800):
            if 640<=j<661:continue
            stamp=at+timedelta(seconds=j-.1);base=10000+5*((j//30)%8)
            for product,shift in (('225MC',0),('225M',15)):
                seq+=1;bid=base+shift
                rows.append(dict(Kind='depth',Time=stamp.isoformat(),Product=product,Sequence=seq,Dropped=0,Bid=[bid-i*5 for i in range(5)],Ask=[bid+5+i*5 for i in range(5)],BidQty=[10+(j+i)%5 for i in range(5)],AskQty=[11+(j+i)%7 for i in range(5)]))
            seq+=1;rows.append(dict(Kind='trade',Time=stamp.isoformat(),Product='225MC',Sequence=seq,Dropped=0,Last=base+(0 if j%3==0 else 5 if j%3==1 else 2.5),TradeQty=0 if j%19==0 else 1+j%5,Symbol='MC'))
        path=temp/'raw.jsonl';path.write_text('\n'.join(json.dumps(r) for r in rows)+'\n',encoding='utf-8')
        manifest=aggregate_source(path,temp/'cache',features_v2=True)
        session=manifest['sessions'][0]
        with np.load(session['cache']) as z:s=build_samples(z['x'],z['mid'],z['times'],z['valid'])
        indices=[119,125,133,160,330,1499];request=temp/'request.json';request.write_text(json.dumps({session['id']:indices}))
        actual=temp/'actual.json'
        subprocess.run(['dotnet',str(root/'feature-runtime/NikkeiSequenceFeatureParity.dll'),str(path),str(request),str(actual)],capture_output=True,text=True,check=True,timeout=60)
        observed=json.loads(actual.read_text());assert len(observed)==len(indices),('missing feature windows',observed)
        errors={}
        for row,i in zip(observed,indices):
            assert row['session']==session['id'] and row['index']==i and row['valid']==bool(s.valid[i]),row
            assert row['rawSourceSha256']==hashlib.sha256(path.read_bytes()).hexdigest(),row
            assert row['historyEligible']==bool(i in s.inference_indices),row
            x,a=s.window(i);expected={'sequence':x.flatten(),'background':a,'pooled':pooled_features(s,[i])[0]}
            for key,target in expected.items():
                value=np.asarray(row[key],dtype=target.dtype);error=float(np.max(np.abs(target-value)))
                errors[key]=max(error,errors.get(key,0));assert np.isfinite(value).all() and error<2e-5,(date,i,key,error)
        results.append(dict(sourceDate=date,windows=indices,maximumAbsoluteErrors=errors,passed=True))
    (root/'raw-feature-fixture-parity.json').write_text(json.dumps(dict(scope='Synthetic raw events: summer/winter US time, holiday, future gap, flow and 10m/30m/2h features',results=results),indent=2))
    safe_cleanup(root,[temp]);print('PASS raw C#/Python 24 feature windows, history eligibility and source SHA')
if __name__=='__main__':main()