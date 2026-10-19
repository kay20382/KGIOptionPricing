from fill_diagnostics import validate_daily_keys

COHORT=['20261006-day','20261007-day']
TRAIN=['20260928-postgap-day','20260929-day','20261005-postgap-day']
SEEDS=[11,22,33]
MASKS={'B0':[],'B1':list(range(40,50)),'F1':list(range(43,50)),
       'F2':[40,41,42,45,46,47,48,49],'F3':[40,41,42,43,44,48,49],'F4':list(range(40,48))}


def combination_eligible(rows,variant):
    selected=[r for r in rows if r['variant']==variant]
    validate_daily_keys(selected,[variant],SEEDS,COHORT)
    eligible=0
    for seed in SEEDS:
        days=[r for r in selected if r['seed']==seed]
        eligible+=all(r['fullDayEligible'] and r['initialTargetPassed'] for r in days)
    return eligible>=2
