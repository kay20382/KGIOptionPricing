"""Research parent selection is separate from qualified model promotion."""
def select_iteration_parent(candidates):
    eligible=[c for c in candidates if not c['missingDates'] and c['totalTrades']>0 and c['expectedGrossPnlPerTradeJPY'] is not None]
    if not eligible:raise ValueError('No complete comparable parent')
    return sorted(eligible,key=lambda c:(-c['expectedGrossPnlPerTradeJPY'],c['group'],c['seed']))[0]

def transfer_statistics(mean,var,parent_excluded,excluded):
    if not set(parent_excluded).issubset(excluded):raise ValueError('Cannot revive previously masked features without a new calibration')
    means=list(mean);variances=list(var)
    for i in set(excluded)-set(parent_excluded):means[i]=0.;variances[i]=1.
    return means,variances

def validate_parent(parent,size,names,excluded,train,config,horizon,allow_factor_transfer=False,allow_entry_transfer=False,allow_earlier_training_extension=False,allow_readiness_transfer=False,allow_readiness_training_extension=False):
    previous_config=dict(parent['environmentConfig']);target_config=dict(config)
    old_readiness=previous_config.pop('researchReadinessProfile','all-research')
    new_readiness=target_config.pop('researchReadinessProfile','all-research')
    if old_readiness not in ['all-research','exclude-flow'] or new_readiness not in ['all-research','exclude-flow']:raise ValueError('Unknown readiness profile')
    if allow_readiness_training_extension and any([allow_factor_transfer,allow_entry_transfer,allow_earlier_training_extension,allow_readiness_transfer]):raise ValueError('Composite readiness/earlier transfer requires its exclusive flag')
    if allow_readiness_transfer or allow_readiness_training_extension:
        if allow_factor_transfer or allow_entry_transfer or allow_earlier_training_extension:raise ValueError('Readiness transfer cannot mix other transfers')
        if old_readiness!='all-research' or new_readiness!='exclude-flow' or previous_config!=target_config:raise ValueError('Readiness transfer changes only all-research to exclude-flow')
        if parent.get('schema')!='nikkei-rl-research-v1' or size!=60 or horizon!=15 or config.get('featureProfile')!='mini-micro-research' or config.get('decisionIntervalSeconds')!=3:raise ValueError('Readiness transfer requires research60/3s15s parent')
        original=parent['trainingEpisodes']
        if allow_readiness_training_extension:
            if len(original)!=4 or original!=sorted(original) or len({e[:8] for e in original})!=4:raise ValueError('Composite transfer requires chronological unique original four dates')
        elif len(train)!=4 or len(set(train))!=4 or train!=original:raise ValueError('Readiness transfer requires same four original episodes')
        if excluded!=parent['actorExcludedFeatures'] or len(set(excluded))!=len(excluded) or any(type(i)!=int or not 0<=i<60 for i in excluded) or not set(range(48,54)).issubset(excluded):raise ValueError('Readiness transfer preserves complete unique flow mask')
    elif old_readiness!=new_readiness:raise ValueError('Readiness change requires explicit transfer')
    if allow_earlier_training_extension or allow_readiness_training_extension:
        from datetime import datetime
        if allow_factor_transfer or allow_entry_transfer:raise ValueError('Earlier training extension cannot mix feature or entry transfers')
        old=parent['trainingEpisodes'];added=set(train)-set(old)
        for episode in [*old,*train]:datetime.strptime(episode[:8],'%Y%m%d')
        if not added or not set(old).issubset(train) or train!=sorted(train) or len({e[:8] for e in train})!=len(train):
            raise ValueError('Earlier extension requires chronological unique dates and all original episodes')
        if any(e[:8]>=min(p[:8] for p in old) for e in added):raise ValueError('New training dates must precede original training start')
    expected=dict(observationSize=size,featureNames=names,actorExcludedFeatures=excluded,
                  trainingEpisodes=train,environmentConfig=config,predictionHorizonSeconds=horizon)
    for key,value in expected.items():
        if key=='trainingEpisodes' and (allow_earlier_training_extension or allow_readiness_training_extension):continue
        if key=='environmentConfig':
            previous=dict(previous_config);target=dict(target_config)
            if not allow_entry_transfer:
                if previous!=target:raise ValueError('Resume parent contract mismatch: environmentConfig')
                continue
            old_mode=previous.pop('entryPriceMode','same-side');new_mode=target.pop('entryPriceMode','same-side')
            if old_mode!='same-side' or new_mode!='opposite-touch' or previous!=target:raise ValueError('Entry transfer permits only same-side to opposite-touch pricing')
            continue
        if key=='actorExcludedFeatures' and allow_factor_transfer:
            if not set(parent[key]).issubset(excluded):raise ValueError('Factor transfer cannot revive masked features')
            continue
        if parent.get(key)!=value:raise ValueError('Resume parent contract mismatch: '+key)
