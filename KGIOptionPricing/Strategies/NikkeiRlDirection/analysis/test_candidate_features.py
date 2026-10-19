import unittest
from observation_schema import feature_names, research_exclusions
from policy_observation import policy_observation
import numpy as np

class CandidateTests(unittest.TestCase):
    def test_60dim_actor_mask_applied_before_normalization(self):
        raw=np.arange(60,dtype=np.float32)
        masked=policy_observation(raw,research_exclusions('baseline'))
        np.testing.assert_array_equal(masked[:40],raw[:40])
        np.testing.assert_array_equal(masked[40:],np.zeros(20))
        np.testing.assert_array_equal(raw,np.arange(60,dtype=np.float32))
    def test_research_state_and_groups(self):
        names=feature_names(60,'mini-micro-research')
        self.assertEqual(len(names),60)
        self.assertEqual(names[:40],feature_names(40,'mini-micro-power'))
        self.assertFalse(any(n.startswith('225_') for n in names))
        self.assertEqual(research_exclusions('baseline'),list(range(40,60)))
        self.assertEqual(research_exclusions('book'),list(range(48,60)))
        self.assertEqual(research_exclusions('flow'),list(range(40,48))+list(range(54,60)))
        self.assertEqual(research_exclusions('price'),list(range(40,54)))
        self.assertEqual(research_exclusions('all'),[])
        with self.assertRaises(ValueError):feature_names(40,'mini-micro-research')
        with self.assertRaises(ValueError):research_exclusions('unknown')

if __name__=='__main__':unittest.main()
