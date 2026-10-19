"""Causal domain mask for direction-specific trend value supervision."""
import numpy as np
def trend_value_mask(trend60,trend300):
 a=np.asarray(trend60);b=np.asarray(trend300)
 if a.ndim!=1 or a.shape!=b.shape or not np.isfinite(a).all() or not np.isfinite(b).all():raise ValueError('Invalid trend domain')
 return np.column_stack(((a>0)&(b>0),(a<0)&(b<0)))
def require_failed_round(summary,results,expected):
 if expected<=0 or summary.get('outcome')!='completed' or len(results)!=expected or any(r.get('validationRuleQualified') is not False for r in results):raise ValueError('Prior round must finish all expected models, with none qualified')