import unittest,numpy as np
from trend_value_domain import trend_value_mask
class TrendValueTests(unittest.TestCase):
 def test_direction_specific_and_zero_rejected(self):
  m=trend_value_mask(np.array([1,-1,1,0,-1]),np.array([2,-2,-2,2,0]))
  np.testing.assert_array_equal(m,[[True,False],[False,True],[False,False],[False,False],[False,False]])
 def test_nonfinite_and_shape_rejected(self):
  with self.assertRaises(ValueError):trend_value_mask([1,float('nan')],[1,1])
  with self.assertRaises(ValueError):trend_value_mask([1],[1,2])
class LaunchTests(unittest.TestCase):
 def test_only_all_completed_failed_models_allow_next_round(self):
  from trend_value_domain import require_failed_round
  failed=[{'validationRuleQualified':False}]*8
  require_failed_round({'outcome':'completed'},failed,8)
  for status in ('failed','stopped'):
   with self.assertRaises(ValueError):require_failed_round({'outcome':status},failed,8)
  with self.assertRaises(ValueError):require_failed_round({'outcome':'completed'},failed[:7],8)
  with self.assertRaises(ValueError):require_failed_round({'outcome':'completed'},failed[:7]+[{'validationRuleQualified':True}],8)