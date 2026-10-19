import unittest
from feature_experiments import combination_eligible


def rows(seed,passed):
    return [dict(variant='F1',seed=seed,episode=f'{day}-day',initialTargetPassed=passed,fullDayEligible=True)
            for day in ['20261006','20261007']]


class FeatureExperimentTests(unittest.TestCase):
    def test_two_seeds_must_each_pass_both_days(self):
        self.assertTrue(combination_eligible(rows(11,True)+rows(22,True)+rows(33,False),'F1'))
        data=rows(11,False)+rows(22,False)+rows(33,False)
        data[0]['initialTargetPassed']=True;data[3]['initialTargetPassed']=True
        self.assertFalse(combination_eligible(data,'F1'))

    def test_missing_duplicate_or_partial_cannot_qualify(self):
        data=rows(11,True)+rows(22,True)+rows(33,True)
        with self.assertRaises(ValueError): combination_eligible(data[:-1],'F1')
        changed=data.copy();changed[-1]=dict(changed[0])
        with self.assertRaises(ValueError): combination_eligible(changed,'F1')
        for row in data: row['fullDayEligible']=False
        self.assertFalse(combination_eligible(data,'F1'))


if __name__=='__main__': unittest.main()
