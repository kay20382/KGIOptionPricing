import unittest
from fill_diagnostics import fill_outcomes, reconcile_episode, validate_daily_keys


def fill(role,price,seconds):
    return dict(role=role,price=price,quantity=1,commission=36,symbol='MC',
        timestamp=f'2026-10-06T09:00:{seconds:02}+08:00')


class FillDiagnosticsTests(unittest.TestCase):
    def test_long_short_results_and_exit_roles_reconcile(self):
        rows=fill_outcomes([fill(0,100,0),fill(4,115,10),fill(1,100,20),fill(3,130,30)])
        self.assertEqual(rows[0]['grossPnlJPY'],150)
        self.assertEqual(rows[0]['netPnlJPY'],78)
        self.assertEqual(rows[1]['netPnlJPY'],-372)
        self.assertEqual(rows[1]['exitRole'],'StopLoss')
        self.assertEqual(rows[1]['holdingSeconds'],10)

    def test_orphan_or_unclosed_fill_is_rejected(self):
        for records in [[fill(4,100,0)],[fill(0,100,0)],
                        [fill(0,100,0),fill(1,100,10)]]:
            with self.assertRaises(ValueError): fill_outcomes(records)

    def test_report_and_feedback_must_cross_reconcile(self):
        episode=dict(status='CompleteEpisode',position=0,netPnl=78,costs=72,completedTrades=1,
            maxDrawdown=5,totalReward=2.5,winRate=100,averageWinningPoints=7.8,
            averageLosingPoints=None,userDefinedProfitFactor=None)
        fills=[fill(0,100,0),fill(4,115,10)]
        reconcile_episode(episode,dict(episode),fills)
        for field,bad in [('netPnl',79),('costs',0),('completedTrades',2),('winRate',0),
                          ('averageWinningPoints',20),('totalReward',0)]:
            changed=dict(episode);changed[field]=bad
            with self.assertRaises(ValueError): reconcile_episode(changed,episode,fills)
        wrong=dict(episode);wrong['winRate']=0
        with self.assertRaises(ValueError): reconcile_episode(wrong,wrong,fills)

    def test_duplicate_daily_key_cannot_replace_missing_seed(self):
        rows=[dict(variant='D',seed=seed,episode=day) for seed in [11,22] for day in ['a','b']]
        validate_daily_keys(rows,['D'],[11,22],['a','b'])
        rows[-1]=dict(rows[0])
        with self.assertRaises(ValueError): validate_daily_keys(rows,['D'],[11,22],['a','b'])


if __name__=='__main__': unittest.main()
