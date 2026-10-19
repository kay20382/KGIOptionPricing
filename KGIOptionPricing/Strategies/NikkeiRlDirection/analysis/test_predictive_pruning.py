import unittest
import numpy as np
from observation_schema import feature_names
from policy_observation import policy_observation
from predictive_pruning import build_samples, retained_by_rule
from test_direction_warmstart import state
from report_predictive_pruning import validate_run_report,validate_benchmark_runtime,update_funnel,bootstrap_method_description

class PredictivePruningTests(unittest.TestCase):
    def test_56_shape_names_and_mask_preserve_state(self):
        names=feature_names(56)
        self.assertEqual(names[:48],feature_names(48))
        self.assertEqual(names[48],'mc_weighted_mid_offset_ticks')
        raw=np.arange(56,dtype=np.float32)
        masked=policy_observation(raw,list(range(30))+list(range(40,56)))
        np.testing.assert_equal(masked[30:40],raw[30:40])
        self.assertEqual(masked[55],0)
        self.assertEqual(raw[55],55)

    def test_five_second_label_uses_future_only_as_target(self):
        a=state('2026-09-29T09:00:00+08:00');a['observation'] += [7]*16
        b=state('2026-09-29T09:00:05+08:00');b['observation'] += [99]*16;b['observation'][0]=1
        x,y,t=build_samples([a,b],5,0)
        self.assertEqual(y.tolist(),[1]);self.assertEqual(x.shape,(1,56))
        self.assertEqual(x[0,48],7)

    def test_unhealthy_window_rejected(self):
        a=state('2026-09-29T09:00:00+08:00');a['observation'] += [1]*16
        b=state('2026-09-29T09:00:05+08:00');b['observation'] += [1]*16;b['observation'][38]=0
        self.assertEqual(len(build_samples([a,b],5,0)[1]),0)

    def test_rule_requires_both_folds_and_positive_interval(self):
        self.assertTrue(retained_by_rule([.01,.02],.001))
        self.assertFalse(retained_by_rule([-.001,.02],.001))
        self.assertFalse(retained_by_rule([.01,.02],-.001))
        self.assertFalse(retained_by_rule([.001,.002],.001))

    def test_report_rejects_changed_latency_and_masks(self):
        from feature_experiments import TRAIN,COHORT,SEEDS
        report=dict(environmentConfig=dict(cooldownSeconds=10,decisionLatencyMs=1,rewardCostWeight=0,observationSize=56),
            actorExcludedFeatures=list(range(40,56)),train=TRAIN,validation=COHORT,requestedSteps=102400,
            directionWarmstartEpochs=20,featureNames=feature_names(56),
            training=[dict(seed=s,actualSteps=102400) for s in SEEDS])
        selection=dict(masks={'C0':list(range(40,56))})
        validate_run_report(report,'C0',selection)
        report['environmentConfig']['decisionLatencyMs']=0
        with self.assertRaises(ValueError):validate_run_report(report,'C0',selection)
        report['environmentConfig']['decisionLatencyMs']=1;report['actorExcludedFeatures']=[]
        with self.assertRaises(ValueError):validate_run_report(report,'C0',selection)

    def test_five_second_candidate_requires_explicit_label_contract(self):
        from feature_experiments import TRAIN,COHORT,SEEDS
        report=dict(environmentConfig=dict(cooldownSeconds=10,decisionLatencyMs=1,rewardCostWeight=0,observationSize=56),
            actorExcludedFeatures=[0],train=TRAIN,validation=COHORT,requestedSteps=102400,
            directionWarmstartEpochs=20,featureNames=feature_names(56),training=[dict(seed=s,actualSteps=102400) for s in SEEDS],
            directionLabel=dict(horizonSeconds=5,exclusiveThresholdTicks=0))
        selection=dict(masks={'N':[0]})
        validate_run_report(report,'N5',selection)
        report['directionLabel']['horizonSeconds']=30
        with self.assertRaises(ValueError):validate_run_report(report,'N5',selection)

    def test_runtime_benchmark_cannot_use_another_binary(self):
        rows=[dict(variant=v,seed=s) for v in ['C0','R','N','N5'] for s in [11,22,33]]
        evidence=dict(runtimeSha256='A'*64,models=rows,phase='After all training and frozen sensitivity completed')
        validate_benchmark_runtime(evidence,'A'*64)
        with self.assertRaises(ValueError):validate_benchmark_runtime(evidence,'B'*64)
        evidence['models']=rows[:-1]
        with self.assertRaises(ValueError):validate_benchmark_runtime(evidence,'A'*64)

    def test_entry_funnel_separates_intent_from_actual_fill(self):
        funnel={}
        row=dict(proposedAction=1,next=dict(effectiveAction=1,pendingEntry=True,overrideReason='',fills=[]))
        update_funnel(funnel,row)
        self.assertEqual(funnel['entrySignals'],1);self.assertEqual(funnel['entryFills'],0)
        row=dict(proposedAction=0,next=dict(effectiveAction=0,pendingEntry=False,overrideReason='',fills=[dict(role=0)]))
        update_funnel(funnel,row)
        self.assertEqual(funnel['entrySignals'],1);self.assertEqual(funnel['entryFills'],1)

    def test_bootstrap_discloses_retained_sample_blocks_not_clock_blocks(self):
        description=bootstrap_method_description(dict(blockSamples=6,bootstrapReplicates=399))
        self.assertIn('6個保留樣本',description)
        self.assertIn('時間跨度不固定',description)
        self.assertIn('不是30秒時間區塊',description)

if __name__=='__main__': unittest.main()
