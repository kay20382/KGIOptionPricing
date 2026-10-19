import unittest
import numpy as np
from direction_warmstart import build_direction_samples, restored_environment, parse_at


def state(at, move=0, valid=True, entry=True):
    obs = [0.0]*40
    obs[0], obs[2], obs[38], obs[39] = 7, move, int(valid), 1
    return dict(at=at, observation=obs, mask=[True,entry,entry,False,False])


class DirectionWarmstartTests(unittest.TestCase):
    def test_three_second_sampling_predicts_fifteen_seconds(self):
        states=[state(f'2026-09-29T09:00:{i:02d}+08:00') for i in range(0,19,3)]
        states[5]['observation'][1]=3
        states[5]['observation'][2]=-3
        x,y=build_direction_samples(states,horizon_seconds=15,decision_interval_seconds=3)
        self.assertEqual(y[0],1)
        np.testing.assert_array_equal(x[0],states[0]['observation'])
        broken=states[:2]+states[3:]
        x,y=build_direction_samples(broken,horizon_seconds=15,decision_interval_seconds=3)
        self.assertEqual(len(y),0)
    def test_csharp_timestamp_variable_fraction_lengths(self):
        self.assertEqual(parse_at('2026-10-06T08:07:19.45003+08:00').microsecond,450030)
        self.assertEqual(parse_at('2026-10-06T08:07:19.4500309+08:00').microsecond,450030)
        self.assertEqual(parse_at('2026-10-06T08:07:19+08:00').microsecond,0)

    def test_future_direction_is_label_and_never_an_input(self):
        for move, label in [(3,1),(-3,2),(2,0),(-2,0)]:
            start = state('2026-09-29T09:00:00+08:00')
            future = state('2026-09-29T09:00:30+08:00', move)
            x,y = build_direction_samples([start,future])
            np.testing.assert_array_equal(x[0], start['observation'])
            self.assertEqual(y.tolist(), [label])
            self.assertEqual(start['observation'][2], 0)

    def test_fifty_dimension_labels_keep_start_extras(self):
        start=state('2026-09-29T09:00:00+08:00');start['observation']+=[1]*10
        future=state('2026-09-29T09:00:30+08:00',3);future['observation']+=[99]*10
        x,y=build_direction_samples([start,future],observation_size=50)
        self.assertEqual(x.shape,(1,50));self.assertEqual(y.tolist(),[1])
        np.testing.assert_array_equal(x[0,40:],np.ones(10))

    def test_missing_horizon_or_gap_or_invalid_data_are_excluded(self):
        start = state('2026-09-29T09:00:00+08:00')
        for end in [state('2026-09-29T09:00:35+08:00',3),
                    state('2026-09-29T09:00:30+08:00',3,valid=False)]:
            x,y = build_direction_samples([start,end])
            self.assertEqual(x.shape,(0,40))
        future=state('2026-09-29T09:00:30+08:00',3)
        future['status']='IncompleteDataGap'
        self.assertEqual(len(build_direction_samples([start,future])[1]),0)

    def test_illegal_entry_or_cross_day_are_excluded(self):
        self.assertEqual(len(build_direction_samples([
            state('2026-09-29T09:00:00+08:00',entry=False),
            state('2026-09-29T09:00:30+08:00',3)])[1]),0)
        self.assertEqual(len(build_direction_samples([
            state('2026-09-29T23:59:45+08:00'),
            state('2026-09-30T00:00:15+08:00',3)])[1]),0)

    def test_input_contract_rejects_wrong_dimension(self):
        bad=state('2026-09-29T09:00:00+08:00'); bad['observation']=[0]*39
        with self.assertRaises(ValueError): build_direction_samples([bad])

    def test_frozen_environment_preserves_cooldown_and_legacy_defaults(self):
        manifest=dict(environmentConfig=dict(cooldownSeconds=5,rewardCostWeight=0,decisionLatencyMs=1))
        config=restored_environment(manifest)
        self.assertEqual(config,manifest['environmentConfig'])
        config['cooldownSeconds']=30
        self.assertEqual(manifest['environmentConfig']['cooldownSeconds'],5)
        self.assertEqual(restored_environment({}),{'cooldownSeconds':30})


if __name__=='__main__': unittest.main()
