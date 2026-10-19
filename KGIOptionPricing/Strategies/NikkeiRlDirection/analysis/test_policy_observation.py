import unittest
import numpy as np
from policy_observation import policy_observation


class PolicyObservationTests(unittest.TestCase):
    def test_fifty_dimension_mask_preserves_causal_prefix_and_source(self):
        source=np.arange(50,dtype=np.float32)
        actual=policy_observation(source,[40,49])
        np.testing.assert_array_equal(actual[:40],source[:40])
        self.assertEqual(actual[40],0);self.assertEqual(actual[49],0);self.assertEqual(source[49],49)

    def test_past_account_profit_cannot_change_masked_market_state(self):
        first = np.arange(40, dtype=np.float32)
        second = first.copy()
        second[33:35] = [-1000, 1000]
        np.testing.assert_array_equal(policy_observation(first, [33,34]), policy_observation(second, [33,34]))
        self.assertEqual(first[33], 33)
        self.assertEqual(policy_observation(first, [33,34])[30], first[30])


if __name__ == '__main__':
    unittest.main()
