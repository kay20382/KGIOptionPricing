"""Offline factor screening; prices/labels are never execution P&L."""
import numpy as np
import math
from scipy.stats import rankdata,t as student_t
def block_evidence(rows):
    blocks=[];weights=[]
    for row in rows:
        times=np.asarray(row['times']);delta=np.asarray(row['delta']);keep=np.isfinite(times)&np.isfinite(delta)
        groups=np.floor(times[keep]/600).astype(np.int64);values=delta[keep];unique=np.unique(groups)
        blocks.append(np.array([values[groups==g].mean() for g in unique]))
        counts_per_block=np.array([(groups==g).sum() for g in unique],dtype=float)
        weights.append(counts_per_block/counts_per_block.sum() if len(unique) else counts_per_block)
    counts=[len(b) for b in blocks]
    if not counts or min(counts)<4:return dict(pValue=1.,meanImprovement=None,lower95=None,blocksPerDay=counts,dayPValue=None)
    means=np.array([w@b for w,b in zip(weights,blocks)]);mu=float(means.mean())
    # Cluster-sandwich variance for the daily observation mean. Equal block sizes
    # reduce to sample variance(block means)/K; partial blocks retain their weight.
    variances=np.array([len(b)/(len(b)-1)*np.sum((w*(b-m))**2) for w,b,m in zip(weights,blocks,means)])
    total=float(variances.sum());se=math.sqrt(total)/len(blocks)
    if se>0:
        df=total**2/sum(v*v/(n-1) for v,n in zip(variances,counts));p=float(student_t.sf(mu/se,df));lower=mu-float(student_t.ppf(.95,df))*se
    else:p=0. if mu>0 else 1.;lower=mu
    dayse=float(means.std(ddof=1)/math.sqrt(len(means))) if len(means)>1 else 0
    dayp=float(student_t.sf(mu/dayse,len(means)-1)) if dayse>0 else (0. if mu>0 else 1.)
    return dict(pValue=p,meanImprovement=mu,lower95=lower,blocksPerDay=counts,dayPValue=dayp)

def by_adjust(p):
    p=np.asarray(p,dtype=float)
    if not np.isfinite(p).all() or np.any((p<0)|(p>1)):raise ValueError('Finite p values required')
    n=len(p)
    if not n:return p
    order=np.argsort(p,kind='stable');adjusted=np.minimum.accumulate((p[order]*n*sum(1/i for i in range(1,n+1))/np.arange(1,n+1))[::-1])[::-1]
    result=np.empty(n);result[order]=np.minimum(1,adjusted);return result

def fit_univariate(x,y,kind):
    x=np.asarray(x);y=np.asarray(y);keep=np.isfinite(x)&np.isfinite(y);x=x[keep];y=y[keep]
    if not len(x) or np.std(x)<=1e-15:return None
    if kind=='linear':
        mean=float(x.mean());scale=float(x.std());z=(x-mean)/scale
        return dict(kind=kind,mean=mean,scale=scale,intercept=float(y.mean()),slope=float(np.mean(z*(y-y.mean()))))
    if kind!='bins':raise ValueError('Unknown univariate model')
    cuts=np.unique(np.quantile(x,[.2,.4,.6,.8]));groups=np.searchsorted(cuts,x,side='right')
    return dict(kind=kind,cuts=cuts.tolist(),values=[float(y[groups==i].mean()) if (groups==i).any() else float(y.mean()) for i in range(len(cuts)+1)])

def predict_univariate(model,x):
    x=np.asarray(x,dtype=float)
    if model['kind']=='linear':return model['intercept']+model['slope']*(x-model['mean'])/model['scale']
    out=np.asarray(model['values'])[np.searchsorted(model['cuts'],x,side='right')];out[~np.isfinite(x)]=np.nan;return out

def time_block_means(t,values,seconds):
    t=np.asarray(t);values=np.asarray(values);mask=np.isfinite(t)&np.isfinite(values);t=t[mask];values=values[mask]
    groups=np.floor(t/seconds).astype(np.int64)
    return np.array([values[groups==g].mean() for g in np.unique(groups)])

def book_factors(bid,ask,bq,aq):
    bid,ask,bq,aq=map(lambda v:np.asarray(v,dtype=float),(bid,ask,bq,aq))
    ids=[1,2,3,5,6,7,8,9,10,11,12,51];out={i:math.nan for i in ids}
    if any(v.shape!=(5,) or not np.isfinite(v).all() for v in (bid,ask,bq,aq)):return out
    if min(bid.min(),ask.min(),bq[0],aq[0])<=0 or np.any(bq<0) or np.any(aq<0) or not 0<ask[0]-bid[0]<=10 or np.any(np.diff(bid)>=0) or np.any(np.diff(ask)<=0):return out
    mid=(bid[0]+ask[0])/2;depth=bq.sum()+aq.sum()
    out.update({1:(ask[0]-bid[0])/5,2:depth,3:(bq[0]-aq[0])/(bq[0]+aq[0]),5:((ask[1]-ask[0])-(bid[0]-bid[1]))/5,
        12:((ask[np.argmax(aq)]-ask[0])-(bid[0]-bid[np.argmax(bq)]))/5,51:bq[0]/bq.sum()-aq[0]/aq.sum()})
    def slope(x,y):
        xx=x-x.mean();yy=y-y.mean();return float(xx@yy/(xx@xx)) if xx@xx>0 else math.nan
    slopes=[];curves=[];centres=[];variances=[];sweeps=[]
    for prices,q,side in ((ask,aq,1),(bid,bq,-1)):
        distance=side*(prices-mid)/5;cumulative=np.cumsum(q);slopes.append(slope(cumulative,distance));curves.append(slope(np.log(cumulative),np.log(distance)))
        d=side*(prices-prices[0])/5;weights=q/q.sum();centre=float(weights@d);centres.append(centre);variances.append(float(weights@((d-centre)**2)))
        takes=np.minimum(q,np.maximum(0,5-np.r_[0,cumulative[:-1]]));sweeps.append(float(prices@takes/5) if q.sum()>=5 else math.nan)
    out.update({6:sum(slopes)/2,7:sum(curves)/2,8:(sweeps[0]-sweeps[1])/5,9:(sum(sweeps)-2*mid)/5,
        10:centres[0]-centres[1],11:sum(variances)/2});return out

def spearman(x,y):
    keep=np.isfinite(x)&np.isfinite(y);x=x[keep];y=y[keep]
    if len(x)<10 or np.std(x)==0 or np.std(y)==0:return None
    return float(np.corrcoef(rankdata(x),rankdata(y))[0,1])

def deduplicate(rows,training,limit):
    selected=[]
    for row in rows:
        if any(row['family']==s['family'] or abs(spearman(training[row['key']],training[s['key']]) or 0)>=.8 for s in selected):continue
        selected.append(row)
        if len(selected)==limit:break
    return selected

def vcvar(bars):
    if len(bars)<120:return math.nan
    bars=bars[-120:]
    if any(not b['complete'] for b in bars):return math.nan
    values=[b['vwar'] for b in bars if b['volume']>0 and b['vwar'] is not None and math.isfinite(b['vwar'])]
    if len(values)<100:return math.nan
    values=np.asarray(values);q=np.quantile(values,.05,method='linear');return float(values[values<=q].mean())
