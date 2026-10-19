import unittest
import numpy as np
from policy_observation import policy_observation
from observation_schema import feature_names
from direction_warmstart import build_direction_samples
from test_direction_warmstart import state


class PaperFeaturesTest(unittest.TestCase):
    def test_new48_has_no_old_extra_names(self):
        names=feature_names(48)
        self.assertEqual(len(names),48)
        self.assertEqual(names[:40],feature_names(40))
        self.assertEqual(names[40],'mc_mlofi5s_level2')
        self.assertNotIn('holdingMfeTicks',names)
        self.assertNotIn('quote_signed_volume5s_mc',names)

    def test_48_mask_isolated_from_raw_inputs(self):
        raw=np.arange(48,dtype=np.float32)
        masked=policy_observation(raw,range(40,48))
        np.testing.assert_equal(masked[:40],raw[:40])
        self.assertTrue((masked[40:]==0).all())
        self.assertEqual(raw[47],47)

    def test_future_labels_do_not_replace_causal_paper_inputs(self):
        start=state('2026-09-29T09:00:00+08:00');start['observation']+=[1]*8
        future=state('2026-09-29T09:00:30+08:00',3);future['observation']+=[99]*8
        x,y=build_direction_samples([start,future],observation_size=48)
        self.assertEqual(x.shape,(1,48));self.assertEqual(y.tolist(),[1])
        np.testing.assert_equal(x[0,40:],np.ones(8))


if __name__=='__main__': unittest.main()
