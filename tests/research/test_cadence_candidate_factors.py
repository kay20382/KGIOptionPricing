import sys, unittest
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts/research'))
from cadence_candidate_factors import duration_cv, price_moments, causal_candidates


class CadenceCandidateTests(unittest.TestCase):
    def test_duration_cv_uses_observed_intervals_and_no_fake_zero_for_missing(self):
        self.assertIsNone(duration_cv([1, 2, 3]))
        self.assertEqual(duration_cv([0, 1, 2, 3]), 0)
        self.assertAlmostEqual(duration_cv([0, 1, 2, 6]), np.std([1, 1, 4]) / 2)
        self.assertIsNone(duration_cv([1, 1, 1, 1]))
        self.assertGreater(duration_cv([0, 0, 1, 3]), 0)
        with self.assertRaises(ValueError): duration_cv([0, 2, 1, 3])

    def test_jump_and_skew_use_log_returns_and_constant_window_defined(self):
        jump, skew = price_moments([100] * 41)
        self.assertEqual((jump, skew), (0, 0))
        jump, skew = price_moments([100] * 40 + [110])
        self.assertEqual(jump, 1)
        r = np.diff(np.log([100] * 40 + [110]))
        self.assertAlmostEqual(skew, np.mean((r - r.mean())**3) / np.mean((r-r.mean())**2)**1.5)
        with self.assertRaises(ValueError): price_moments([100, 0, 110])

    def test_candidates_do_not_consume_future_or_boundary_trade(self):
        frames = [self.frame(i, [i - 2.5, i - 1.5]) for i in range(0, 126, 3)]
        first = causal_candidates(frames)
        extra = frames + [self.frame(126, [125.5])]
        np.testing.assert_allclose(first, causal_candidates(extra)[:len(first)], equal_nan=True)
        frames[-1]['trades'].append({'at': 123.0})
        with self.assertRaises(ValueError): causal_candidates(frames)

    def test_stale_quote_resets_price_path_without_filling_missing(self):
        frames = [self.frame(i, [i - 2.5, i - 1.5]) for i in range(0, 126, 3)]
        self.assertTrue(np.isfinite(causal_candidates(frames)[-1]).all())
        frames[20]['features'][7] = 3
        self.assertTrue(np.isnan(causal_candidates(frames)[-1, 1:]).all())
        frames[20]['dataGap'] = True
        self.assertTrue(np.isnan(causal_candidates(frames)[20]).all())

    @staticmethod
    def frame(at, trades):
        return dict(at=float(at), bid=100, ask=105, features=[0]*30,
                    dataGap=False, trades=[dict(at=float(t)) for t in trades])


if __name__ == '__main__': unittest.main()
