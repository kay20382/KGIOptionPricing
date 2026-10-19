"""Read verified sources into memory. No row-level file is written."""
import json,subprocess,math
from collections import deque
from datetime import datetime,timezone,timedelta
import numpy as np
from factor_significance import book_factors,vcvar
from minute_factor_calculations import minute_candidates
from minute_price_factors import PriceMinuteBar,minute_price_candidates
from minute_smart_money import minute_smart_money
from direction_warmstart import parse_at

CALCULABLE=[*range(1,13),15,16,17,21,23,24,25,*range(29,37),39,*range(51,62)]

def collect(bridge,cache,source,source_sha):
    process=subprocess.Popen(['dotnet',str(bridge),'export',str(cache),str(source),source_sha],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
    rows=[];extras=[];base=[];values=[];bars=deque(maxlen=120);previous=None;minute_values={};last_minute=None;summary=None
    try:
        for line in process.stdout:
            packet=json.loads(line)
            if packet.get('summary'):summary=packet;continue
            at=parse_at(packet['at']);t=at.timestamp();stats=packet['stats'];book=stats['book'];mid=(packet['bid']+packet['ask'])/2
            features={i:math.nan for i in CALCULABLE}
            if book is not None:
                features.update(book_factors(*book))
                if previous is not None:
                    reductions=[]
                    for pi,qi in ((0,2),(1,3)):
                        old=dict(zip(previous[pi],previous[qi]));new=dict(zip(book[pi],book[qi]));reductions.append(sum(max(old[p]-new[p],0) for p in old.keys()&new.keys()))
                    features[53]=reductions[1]-reductions[0]
                previous=book
                if stats['vwap30'] is not None:features[52]=(stats['vwap30']-mid)/5
            else:previous=None
            for i,k in ((15,'ofi5'),(16,'signedVolume5'),(21,'cv200'),(23,'signAc200'),(24,'run200'),(25,'hhi200')):
                features[i]=math.nan if stats[k] is None else stats[k]
            features[17]=stats['count5']/5;features[54]=stats['coverage30']
            for bar in stats['bars']:
                bars.append(bar)
                if bar['end']>t:raise ValueError('Future minute in factor')
            minute=t//60
            if minute!=last_minute:
                last_minute=minute
                typed=[PriceMinuteBar(datetime.fromtimestamp(b['end'],timezone(timedelta(hours=8))),b['amount'],b['volume'],b['complete'],b['open'],b['high'],b['low'],b['close']) for b in bars]
                minute_values={**minute_candidates(at,typed),**minute_price_candidates(at,typed),**minute_smart_money(at,typed),61:vcvar(list(bars))}
            features.update(minute_values)
            if len(rows)>=5 and packet['valid'] and all(r['valid'] for r in rows[-5:]):features[29]=math.log(mid/((rows[-5]['bid']+rows[-5]['ask'])/2))
            if len(rows)>=40 and packet['valid'] and all(r['valid'] for r in rows[-40:]):
                prices=np.array([(r['bid']+r['ask'])/2 for r in rows[-40:]]+[mid]);ret=np.diff(np.log(prices));rv=float(ret@ret);length=float(np.abs(np.diff(prices)).sum())
                features[30]=abs(prices[-1]-prices[0])/length if length>0 else 0.
                def corr(a,b):return float(np.corrcoef(a,b)[0,1]) if np.std(a)>0 and np.std(b)>0 else math.nan
                features[31]=corr(ret[:-1],ret[1:]);features[32]=rv;features[33]=float(ret[ret<0]@ret[ret<0])/rv if rv>0 else math.nan
                bv=math.pi/2*float(np.abs(ret[1:])@np.abs(ret[:-1]));features[34]=max(rv-bv,0)/rv if rv>0 else math.nan
                features[35]=corr(abs(ret[:-1]),abs(ret[1:]));features[36]=math.sqrt(len(ret))*float(np.sum(ret**3))/rv**1.5 if rv>0 else math.nan
            rows.append(dict(t=t,bid=packet['bid'],ask=packet['ask'],valid=packet['valid'],gap=False,x=[0.]*60,entryAllowed=packet['entryAllowed']))
            values.append([features[i] for i in CALCULABLE]);base.append(packet['features']);extras.append(packet['extras'])
        error=process.stderr.read();code=process.wait()
        if code or summary is None:raise ValueError('C# source export failed: '+error[-6000:])
    finally:
        if process.poll() is None:process.terminate();process.wait(timeout=10)
    return dict(rows=rows,values=np.asarray(values),base=np.asarray(base),extras=np.asarray(extras),summary=summary)

def calibrate(data,training):
    # Catalog4: six next-price-change transitions on3s snapshots (explicit engineering sampling).
    def states(panel):
        x=panel['values'];s=x[:,CALCULABLE.index(1)];q=x[:,CALCULABLE.index(3)]
        index=np.searchsorted([-.6,-.2,.2,.6],q,side='right')+5*(s>1.5)
        return index,np.isfinite(s)&np.isfinite(q)
    counts=np.zeros(10);g=np.zeros(10);transitions=np.zeros((10,10))
    for date in training:
        p=data[date];si,good=states(p);rows=p['rows'];mid=np.array([(r['bid']+r['ask'])/2 for r in rows]);next_change=None
        for i in range(len(rows)-2,-1,-1):
            if not rows[i]['valid'] or not rows[i+1]['valid']:next_change=None;continue
            if mid[i+1]!=mid[i]:next_change=i+1
            if next_change is not None and good[i] and good[next_change]:
                a=si[i];b=si[next_change];counts[a]+=1;g[a]+=(mid[next_change]-mid[i])/5;transitions[a,b]+=1
    active=counts>=200;g=np.divide(g,counts,out=np.zeros(10),where=counts>0);matrix=np.divide(transitions,counts[:,None],out=np.zeros((10,10)),where=counts[:,None]>0)
    future=np.zeros(10);term=g.copy()
    for _ in range(6):future+=term;term=matrix@term
    # Catalog39: past mini15s tick return lag3s; train-only beta to micro next15s move.
    xx=[];yy=[]
    from prediction_fill_benchmark import label_samples
    for d in training:
        p=data[d];_,y,ix=label_samples(p['rows'],15);use=np.array([j>0 and p['rows'][j-1]['valid'] for j in ix]);xx.extend(p['base'][ix[use]-1,11]);yy.extend(y[use])
    xx=np.asarray(xx);yy=np.asarray(yy);finite=np.isfinite(xx)&np.isfinite(yy);xx=xx[finite];yy=yy[finite]
    beta=float(np.mean((xx-xx.mean())*(yy-yy.mean()))/np.var(xx)) if len(xx)>=10 and np.var(xx)>0 else None
    for p in data.values():
        si,good=states(p);values=np.where(good&active[si],future[si],np.nan);p['values'][:,CALCULABLE.index(4)]=values
        good_lag=np.array([p['rows'][j-1]['valid'] and p['rows'][j]['valid'] for j in range(1,len(p['rows']))])
        p['values'][1:,CALCULABLE.index(39)]=np.where(good_lag,beta*p['base'][:-1,11],np.nan) if beta is not None else np.nan
    return dict(micropriceStates=10,micropriceStateTrainingCounts=counts.tolist(),micropriceSixStepValues=future.tolist(),miniLag3Seconds15SecondBeta=beta,miniCalibrationSamples=len(xx))
