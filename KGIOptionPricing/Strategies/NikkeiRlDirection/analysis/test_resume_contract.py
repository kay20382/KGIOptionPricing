import unittest
from resume_contract import validate_parent, select_iteration_parent, transfer_statistics

class ResumeTests(unittest.TestCase):
    def test_parent_selection_uses_weighted_expectancy_despite_unqualified(self):
        candidates=[dict(group='A',seed=22,qualified=False,missingDates=[],totalTrades=313,expectedGrossPnlPerTradeJPY=-8.3),
                    dict(group='B',seed=11,qualified=False,missingDates=[],totalTrades=63,expectedGrossPnlPerTradeJPY=-9.5),
                    dict(group='missing',seed=11,missingDates=['D'],totalTrades=1,expectedGrossPnlPerTradeJPY=900)]
        self.assertEqual(select_iteration_parent(candidates)['group'],'A')

    def test_zero_trade_parent_rejected(self):
        with self.assertRaises(ValueError):select_iteration_parent([dict(missingDates=[],totalTrades=0,expectedGrossPnlPerTradeJPY=None)])

    def test_explicit_factor_transfer_resets_only_newly_masked_statistics(self):
        mean,var=transfer_statistics([3.,7.,9.],[2.,4.,6.],[],[1])
        self.assertEqual(mean,[3.,0.,9.]);self.assertEqual(var,[2.,1.,6.])
        with self.assertRaises(ValueError):transfer_statistics([3.],[2.],[0],[])

    def test_resume_rejects_changed_feature_execution_or_training_dates(self):
        parent=dict(observationSize=60,featureNames=['x']*60,actorExcludedFeatures=[],trainingEpisodes=['T'],
                    environmentConfig=dict(cooldownSeconds=10),predictionHorizonSeconds=15)
        validate_parent(parent,60,['x']*60,[],['T'],dict(cooldownSeconds=10),15)
        for field,value in [('observationSize',40),('actorExcludedFeatures',[40]),('trainingEpisodes',['V']),
                            ('environmentConfig',dict(cooldownSeconds=0)),('predictionHorizonSeconds',30)]:
            with self.assertRaises(ValueError):validate_parent(dict(parent,**{field:value}),60,['x']*60,[],['T'],dict(cooldownSeconds=10),15)
    def test_entry_transfer_changes_only_declared_quote_mode(self):
        parent=dict(observationSize=60,featureNames=['x']*60,actorExcludedFeatures=[],trainingEpisodes=['T'],
                    environmentConfig=dict(cooldownSeconds=10,decisionLatencyMs=1),predictionHorizonSeconds=15)
        target=dict(cooldownSeconds=10,decisionLatencyMs=1,entryPriceMode='opposite-touch')
        with self.assertRaises(ValueError):validate_parent(parent,60,['x']*60,[],['T'],target,15)
        validate_parent(parent,60,['x']*60,[],['T'],target,15,allow_entry_transfer=True)
        for bad in [dict(target,cooldownSeconds=0),dict(target,decisionLatencyMs=0),dict(target,entryPriceMode='market')]:
            with self.assertRaises(ValueError):validate_parent(parent,60,['x']*60,[],['T'],bad,15,allow_entry_transfer=True)

    def test_earlier_training_extension_requires_explicit_flag_and_preserves_parent(self):
        old=['20260928-postgap-day','20260929-day','20260930-day','20261005-postgap-day']
        parent=dict(observationSize=60,featureNames=['x']*60,actorExcludedFeatures=[],trainingEpisodes=old,
                    environmentConfig=dict(cooldownSeconds=10,decisionLatencyMs=1),predictionHorizonSeconds=15)
        extended=['20260915-day','20260916-day']+old
        with self.assertRaises(ValueError):validate_parent(parent,60,['x']*60,[],extended,parent['environmentConfig'],15)
        validate_parent(parent,60,['x']*60,[],extended,parent['environmentConfig'],15,allow_earlier_training_extension=True)
        for bad in [extended[::-1],extended+['20261006-day'],extended+['20260928-replaced'],extended[1:2]+old[1:],old,extended+['20260915-day']]:
            with self.assertRaises(ValueError):validate_parent(parent,60,['x']*60,[],bad,parent['environmentConfig'],15,allow_earlier_training_extension=True)
        for field,value in [('environmentConfig',dict(cooldownSeconds=0,decisionLatencyMs=1)),('actorExcludedFeatures',[40]),('predictionHorizonSeconds',30)]:
            with self.assertRaises(ValueError):validate_parent(dict(parent,**{field:value}),60,['x']*60,[],extended,parent['environmentConfig'],15,allow_earlier_training_extension=True)
        with self.assertRaises(ValueError):validate_parent(parent,60,['x']*60,[],extended,parent['environmentConfig'],15,allow_factor_transfer=True,allow_earlier_training_extension=True)

class ReadinessTransferTests(unittest.TestCase):
    def setUp(self):
        self.train=['20260928-postgap-day','20260929-day','20260930-day','20261005-postgap-day']
        self.mask=list(range(48,54));self.names=['x']*60
        self.config=dict(featureProfile='mini-micro-research',decisionIntervalSeconds=3,cooldownSeconds=10,
                         decisionLatencyMs=1,commission=36,maxStopPoints=150,maxHoldingSeconds=0)
        self.parent=dict(schema='nikkei-rl-research-v1',observationSize=60,featureNames=self.names,
                         actorExcludedFeatures=self.mask,trainingEpisodes=self.train,
                         environmentConfig=self.config,predictionHorizonSeconds=15)
        self.target=dict(self.config,researchReadinessProfile='exclude-flow')
    def validate(self,**kwargs):
        return validate_parent(kwargs.pop('parent',self.parent),60,self.names,kwargs.pop('mask',self.mask),
                               kwargs.pop('train',self.train),kwargs.pop('config',self.target),15,
                               allow_readiness_transfer=True,**kwargs)
    def test_explicit_readiness_transfer_only(self):
        with self.assertRaises(ValueError):validate_parent(self.parent,60,self.names,self.mask,self.train,self.target,15)
        self.validate()
    def test_other_config_changes_rejected(self):
        for field,value in [('commission',0),('decisionLatencyMs',0),('cooldownSeconds',0),('maxStopPoints',155),('maxHoldingSeconds',60)]:
            with self.subTest(field=field),self.assertRaises(ValueError):self.validate(config=dict(self.target,**{field:value}))
    def test_same_original_four_dates_and_mask_required(self):
        for train in [self.train[:-1],self.train+['20260915-day'],self.train[::-1],self.train[:-1]+['20261006-day']]:
            with self.assertRaises(ValueError):self.validate(train=train)
        for mask in [self.mask[:-1],self.mask+[40],self.mask+[53],self.mask+[60]]:
            with self.assertRaises(ValueError):self.validate(mask=mask)
        with self.assertRaises(ValueError):self.validate(parent=dict(self.parent,actorExcludedFeatures=self.mask[:-1]),mask=self.mask[:-1])
    def test_no_mixed_transfer_or_reverse_direction(self):
        for flag in ['allow_factor_transfer','allow_entry_transfer','allow_earlier_training_extension']:
            with self.assertRaises(ValueError):self.validate(**{flag:True})
        for profile in ['all-research','unknown']:
            with self.assertRaises(ValueError):self.validate(config=dict(self.target,researchReadinessProfile=profile))
        with self.assertRaises(ValueError):self.validate(parent=dict(self.parent,environmentConfig=self.target))
    def test_legacy_default_profile_semantically_equal(self):
        validate_parent(self.parent,60,self.names,self.mask,self.train,dict(self.config,researchReadinessProfile='all-research'),15)

class ReadinessEarlierTransferTests(unittest.TestCase):
    def setUp(self):
        base=ReadinessTransferTests();base.setUp()
        self.parent=base.parent;self.names=base.names;self.mask=base.mask;self.config=base.target
        self.extended=[date+'-day' for date in ['20260915','20260916','20260917','20260918','20260921','20260922','20260923','20260924']]+base.train
    def validate(self,**kwargs):
        return validate_parent(kwargs.pop('parent',self.parent),60,self.names,kwargs.pop('mask',self.mask),
                               kwargs.pop('train',self.extended),kwargs.pop('config',self.config),15,
                               allow_readiness_training_extension=True,**kwargs)
    def test_explicit_composite_accepts_real_twelve_period_shape(self):
        self.validate()
        with self.assertRaises(ValueError):validate_parent(self.parent,60,self.names,self.mask,self.extended,self.config,15)
    def test_old_flags_still_cannot_mix_and_composite_rejects_flags(self):
        with self.assertRaises(ValueError):validate_parent(self.parent,60,self.names,self.mask,self.extended,self.config,15,allow_readiness_transfer=True,allow_earlier_training_extension=True)
        for flag in ['allow_factor_transfer','allow_entry_transfer','allow_earlier_training_extension','allow_readiness_transfer']:
            with self.assertRaises(ValueError):self.validate(**{flag:True})
    def test_future_duplicate_original_removal_and_order_rejected(self):
        for train in [self.extended+['20261006-day'],self.extended+['20260915-day'],self.extended[::-1],self.extended[:-1],self.parent['trainingEpisodes']]:
            with self.assertRaises(ValueError):self.validate(train=train)
    def test_same_features_cost_risk_and_target_profile_required(self):
        for config in [dict(self.config,commission=0),dict(self.config,cooldownSeconds=0),dict(self.config,maxStopPoints=155),dict(self.config,researchReadinessProfile='all-research')]:
            with self.assertRaises(ValueError):self.validate(config=config)
        with self.assertRaises(ValueError):self.validate(mask=self.mask+[40])
        with self.assertRaises(ValueError):self.validate(parent=dict(self.parent,schema='nikkei-rl-research-pruned-flow-v1'))
    def test_original_parent_requires_chronological_unique_four_dates(self):
        for original in [self.parent['trainingEpisodes'][::-1],self.parent['trainingEpisodes'][:3]+[self.parent['trainingEpisodes'][0]]]:
            with self.assertRaises(ValueError):self.validate(parent=dict(self.parent,trainingEpisodes=original))

if __name__=='__main__':unittest.main()
