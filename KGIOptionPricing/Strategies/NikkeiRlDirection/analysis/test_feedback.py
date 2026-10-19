import tempfile
import unittest
from pathlib import Path
from feedback import validate_splits, build_summary, verify_manifest


class FeedbackTests(unittest.TestCase):
    def test_same_day_cannot_be_both_train_and_validation(self):
        with self.assertRaises(ValueError):
            validate_splits(['20260928-day'], ['20260928-night'])

    def test_time_order_must_be_forward(self):
        with self.assertRaises(ValueError):
            validate_splits(['20261001-day'], ['20260928-day'])

    def test_empty_split_rejected(self):
        with self.assertRaises(ValueError):
            validate_splits([], ['20261001-day'])

    def test_small_or_prior_inspected_data_cannot_pass(self):
        result = build_summary([{'status':'CompleteEpisode','netPnl':100,'completedTrades':2}], 'DevelopmentPreviouslyInspected')
        self.assertEqual(result['status'], 'IncompleteDevelopmentValidation')
        self.assertEqual(result['netPnl'], 100)
        self.assertFalse(result['deploymentEligible'])

    def test_open_position_prevents_complete_pnl_claim(self):
        result = build_summary([{'status':'IncompleteUnclosedPosition','netPnl':100,'completedTrades':2}], 'PristineHoldout')
        self.assertEqual(result['status'], 'IncompleteExecution')

    def test_model_tampering_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'model.onnx';path.write_bytes(b'bad')
            with self.assertRaises(ValueError):
                verify_manifest(path, {'modelSha256':'0'*64,'observationSize':40,'actionNames':['Wait','EnterLong','EnterShort','Flatten','CancelEntry']})


if __name__ == '__main__':
    unittest.main()
