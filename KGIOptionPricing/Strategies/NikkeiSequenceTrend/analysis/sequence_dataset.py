"""Bounded raw aggregation and lazy windows. No order execution or P&L here."""
import json,hashlib,math,time,struct,re,io
from pathlib import Path
from datetime import datetime,timezone
from collections import deque
import numpy as np
from research_resource_guard import Rate,Budget,limited_write
from session_context import context_features,session
from micro_factors import MicroFrame

MARKET_NAMES=['return_ticks','range_ticks','realized_variance','log_volume','spread_ticks','obi1','obi5','log_depth5','weighted_mid_offset','depth_curvature_asymmetry']
FEATURE_NAMES=[p+'_'+n for p in ('225MC','225M') for n in MARKET_NAMES]+['day_open','day_close','night_open','night_close','macro_window','us_open','us_regular','us_electronic','time_sin','time_cos','since_day_open','until_day_close','since_night_open','until_night_close','since_us_open','until_us_close','until_macro','since_macro','macro_calendar_known']

FEATURE_NAMES_V2=FEATURE_NAMES+[p+'_'+n for p in ('225MC','225M') for n in ('trade_imbalance','trade_count_imbalance','depth_pressure_change')]

def parse_timestamp(stamp):
    # .NET recorder has100ns fractions; Python3.10 accepts only3/6 digits.
    # Truncate the final100ns digit, preserve original bytes in sourceSHA.
    match=re.match(r'^(.{19})\.(\d+)([+-]\d\d:\d\d|Z)$',stamp)
    if match:stamp=match[1]+'.'+match[2][:6].ljust(6,'0')+match[3]
    return datetime.fromisoformat(stamp)

def aggregate_book(bid,ask,bq,aq,tick):
    if min(map(len,(bid,ask,bq,aq)))<5 or min(bid[:5]+ask[:5])<=0 or ask[0]<=bid[0] or min(bq[:5]+aq[:5])<0:raise ValueError('Invalid five-level book')
    qb=sum(bq[:5]);qa=sum(aq[:5]);total=qb+qa
    if total<=0 or bq[0]+aq[0]<=0:raise ValueError('Empty book')
    mid=(bid[0]+ask[0])/2;wm=(ask[0]*bq[0]+bid[0]*aq[0])/(bq[0]+aq[0])
    curvature=sum(bq[i]-2*bq[i+1]+bq[i+2]-aq[i]+2*aq[i+1]-aq[i+2] for i in range(3))/total
    return mid,[(ask[0]-bid[0])/tick,(bq[0]-aq[0])/(bq[0]+aq[0]),(qb-qa)/total,math.log1p(total),(wm-mid)/tick,curvature]

class Samples:
    def __init__(self,x,mid,times,valid):
        self.x=x;self.mid=np.asarray(mid);self.times=np.asarray(times);self.valid=valid
        bad=np.concatenate(([0],np.cumsum(~np.asarray(valid,bool))))
        self.inference_indices=np.asarray([i for i in range(119,len(x)) if bad[i+1]-bad[i-119]==0 and abs(times[i]-times[i-119]-595)<1e-6],dtype=np.int64)
        rows=[i for i in self.inference_indices if i+6<len(x) and bad[i+7]-bad[i+1]==0 and abs(times[i+6]-times[i]-30)<1e-6]
        self.indices=np.asarray(rows,dtype=np.int64)
        self.targets=(self.mid[self.indices+6]-self.mid[self.indices])/5
        self.classes=np.where(self.targets>2+1e-6,2,np.where(self.targets< -2-1e-6,0,1))
    def window(self,i):
        short=self.x[i-119:i+1].copy();aux=[]
        for length in (360,1440):
            start=max(0,i-length+1);v=self.valid[start:i+1];m=self.mid[start:i+1];z=self.x[start:i+1]
            if len(m)>1 and np.any(v):
                ok=m[v];span=max(float(np.ptp(ok)),5)
                aux += [(ok[-1]-ok[0])/5,float(np.std(np.diff(ok)/5)),(ok[-1]-np.min(ok))/span,float(np.mean(z[v,3])),float(np.mean(z[v,6])),float(np.mean(z[v,4])),len(m)/length,float(np.mean(v))]
            else:aux += [0.]*8
        return short,np.asarray(aux,dtype=np.float32)

def build_samples(x,mid,times,valid):
    # Own a snapshot: a future write to caller buffers must not rewrite samples.
    return Samples(np.array(x,dtype=np.float32,copy=True),np.asarray(mid),np.asarray(times),np.asarray(valid,bool))

def aggregate_source(path,cache_root,calendar=None,progress=None,ordering_policy='strict',features_v2=False):
    path=Path(path);cache_root=Path(cache_root);cache_root.mkdir(parents=True,exist_ok=True)
    if ordering_policy not in ('strict','quarantine'):raise ValueError('Unknown ordering policy')
    stat=path.stat();hasher=hashlib.sha256();reader_rate=Rate(20*1024**2);writer_rate=Rate(5*1024**2)
    products={p:dict(book=None,at=None,anchor=None,lastmid=None,low=None,high=None,rv=0.,volume=0,micro=MicroFrame()) for p in ('225MC','225M')}
    sessions={};current=None;boundary=None;previous=None;seq_previous=None;rows=0;bad=0;dropped=0;ordering=0;zero_quantity=0;order_defects=[];quality_pending=False;calendar=calendar or {}
    def emit(end):
        nonlocal current,quality_pending
        dt=datetime.fromtimestamp(end,timezone.utc);key=session(dt)
        if key is None:return
        values=[];healthy=not quality_pending;quality_pending=False;book_times=[]
        for p in products.values():
            good=p['book'] is not None and p['at'] is not None and 0<=end-p['at']<=2 and p['book'][1][0]<=2
            healthy &= good;book_times.append(p['at'] or 0)
            if p['book']:
                mid,book=p['book'];anchor=p['anchor'];low=p['low'] if p['low'] is not None else mid;high=p['high'] if p['high'] is not None else mid
                values += [0 if anchor is None else (mid-anchor)/5,(high-low)/5,p['rv'],math.log1p(p['volume']),*book]
                healthy &= anchor is not None;p['anchor']=mid
            else:values += [0.]*10
            p.update(low=None,high=None,rv=0.,volume=0)
        healthy &= max(book_times)-min(book_times)<=1 and previous is not None and end-previous<=10
        state=sessions.setdefault(key,dict(x=[],mid=[],times=[],valid=[],quotes=[],trades=[],symbols=set()))
        if current!=key:
            # No aggregation or anchor may carry across sessions.
            healthy=False;current=key
        state['x'].append(values+context_features(dt,calendar)+([v for p in products.values() for v in p['micro'].drain()] if features_v2 else []));state['times'].append(end);state['valid'].append(healthy)
        mc=products['225MC']['book'];mid=mc[0] if mc else 0
        state['mid'].append(mid)
        b,a=(0,0) if mc is None else (mid-mc[1][0]*2.5,mid+mc[1][0]*2.5)
        state['quotes'].append([end,b,a,healthy])
    with path.open('rb',buffering=1024**2) as f:
        for raw in f:
            hasher.update(raw);reader_rate.consume(len(raw));rows+=1
            if b'"Kind": "trade"' not in raw and b'"Kind":"trade"' not in raw and b'"Kind": "depth' not in raw and b'"Kind":"depth' not in raw:continue
            row=json.loads(raw);product=row.get('Product')
            if product not in products:continue
            if row.get('Dropped',0):dropped=max(dropped,row['Dropped'])
            stamp=row['Time'];at=parse_timestamp(stamp).timestamp();seq=row.get('Sequence')
            if previous is not None and (at<previous or seq is not None and seq_previous is not None and seq<=seq_previous):
                ordering+=1
                rollback=max(0,previous-at);sequence_lag=0 if seq is None or seq_previous is None else max(0,seq_previous-seq)
                if ordering_policy=='strict' or rollback>2 or sequence_lag>128 or ordering>1000:raise ValueError('Source ordering defect outside bounded quarantine: '+str(path))
                order_defects.append(dict(line=rows,rollbackSeconds=rollback,sequenceLag=sequence_lag))
                quality_pending=True
                for state in products.values():
                    state.update(book=None,at=None,anchor=None,lastmid=None,low=None,high=None,rv=0.,volume=0);state['micro'].reset()
                continue # File order remains authoritative; never sort or rewrite.

            boundary=boundary or (math.floor(at/5)+1)*5
            # Match existing C# causality: boundary is emitted before consuming
            # an event at that boundary. No later event repairs an earlier frame.
            while at>=boundary:emit(boundary);boundary+=5
            if previous is not None and at-previous>10:
                for p in products.values():
                    p.update(anchor=None,lastmid=None,low=None,high=None,rv=0.,volume=0);p['micro'].reset()
            previous=at;seq_previous=seq
            p=products[product]
            key=session(datetime.fromtimestamp(at,timezone.utc))
            if row['Kind'].startswith('depth'):
                try:
                    book=aggregate_book(row['Bid'],row['Ask'],row['BidQty'],row['AskQty'],5)
                    mid=book[0];p['micro'].book(sum(row['BidQty'][:5]),sum(row['AskQty'][:5]))
                    if p['lastmid'] is not None:p['rv']+=((mid-p['lastmid'])/5)**2
                    p['book']=book;p['at']=at;p['lastmid']=mid
                    p['low']=mid if p['low'] is None else min(p['low'],mid);p['high']=mid if p['high'] is None else max(p['high'],mid)
                except ValueError:p['book']=None;p['at']=None
            elif row['Kind']=='trade':
                qty=row.get('TradeQty');price=row.get('Last')
                if qty is None or qty<0 or not price or price<=0:bad+=1;continue
                if qty==0:zero_quantity+=1;continue # Indicative updates cannot fill paper orders.
                p['volume']+=qty
                if p['book'] and p['at'] is not None:
                    mid,b=p['book'];p['micro'].trade(price,qty,mid-b[0]*2.5,mid+b[0]*2.5,at-p['at'])
                else:p['micro'].trade(price,qty,0,0,3)
                if key and product=='225MC':
                    state=sessions.setdefault(key,dict(x=[],mid=[],times=[],valid=[],quotes=[],trades=[],symbols=set()))
                    state['trades'].append([at,price,seq or rows]);state['symbols'].add(row['Symbol'])
            if rows%300000==0 and progress:progress(path.name,rows)
    if dropped or bad:raise ValueError('Dropped or invalid trades: '+str(path))
    after=path.stat()
    if (stat.st_size,stat.st_mtime_ns)!=(after.st_size,after.st_mtime_ns):raise ValueError('Source still writing: '+str(path))
    manifest=dict(source=str(path),sourceBytes=stat.st_size,sourceMtimeNs=stat.st_mtime_ns,sourceSha256=hasher.hexdigest(),rows=rows,ordering=ordering,orderingPolicy=ordering_policy,featureSchema='v2-45' if features_v2 else 'v1-39',orderDefects=order_defects,dropped=dropped,zeroQuantityTradesExcluded=zero_quantity,sessions=[])
    for key,data in sessions.items():
        if len(data['x'])<150:continue
        if len(data['symbols'])!=1:raise ValueError('Unknown/multiple tradable contracts')
        dst=cache_root/(key+'.npz')
        # Compress a bounded day, then consume the write budget BEFORE each chunk.
        buffer=io.BytesIO()
        np.savez_compressed(buffer,x=np.asarray(data['x'],np.float32),mid=np.asarray(data['mid']),times=np.asarray(data['times']),valid=np.asarray(data['valid']),quotes=np.asarray(data['quotes']),trades=np.asarray(data['trades']),symbol=np.asarray(next(iter(data['symbols']))))
        limited_write(dst,buffer.getvalue(),cache_root)
        manifest['sessions'].append(dict(id=key,frames=len(data['x']),validFrames=sum(data['valid']),first=data['times'][0],last=data['times'][-1],cache=str(dst),fullSession=(data['times'][-1]-data['times'][0]>= (6*3600+50*60 if key.endswith('day') else 12*3600+50*60))))
    return manifest
