import unittest
from observation_schema import feature_names

class PowerProfileTests(unittest.TestCase):
    def test_power40_excludes_large_and_preserves_state_indices(self):
        names=feature_names(40,'mini-micro-power')
        self.assertEqual(len(names),40)
        self.assertFalse(any(n.startswith('225_') for n in names))
        self.assertEqual(names[20:25],['225MC_buyPower30s','225MC_shortPower60s','225MC_longPower180s','225MC_biasPower','225MC_movementDirection'])
        self.assertEqual(names[30:],feature_names(40)[30:])
        with self.assertRaises(ValueError):feature_names(48,'mini-micro-power')
        with self.assertRaises(ValueError):feature_names(40,'unknown')

if __name__=='__main__':unittest.main()
