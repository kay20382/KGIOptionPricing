import json
import tempfile
import unittest
from unittest.mock import patch
from incremental_daily_backtest import model_entries,readiness_route
from datetime import datetime, timezone
from pathlib import Path
from incremental_daily_backtest import discover, extract_day, combine_parts, needs_processing, date_usage, main, refresh_report


class IncrementalDailyTests(unittest.TestCase):
    def test_accepted_daily_report_and_coverage_changes_are_rejected(self):
        from incremental_daily_backtest import verify_accepted_date,sha
        version=self.root/'version';version.mkdir();result=version/'daily-performance.json';index=version/'segment-index.json'
        result.write_text('{}');index.write_text('{}')
        entry=dict(version=str(version),dailyResultSha256=sha(result),coverageInputs={str(index):sha(index)})
        verify_accepted_date(entry)
        result.write_text('{"fake":true}')
        with self.assertRaises(ValueError):verify_accepted_date(entry)
        result.write_text('{}');index.write_text('{"fullDayCoverage":true}')
        with self.assertRaises(ValueError):verify_accepted_date(entry)
    def test_daily_root_cannot_switch_clock_modes(self):
        from incremental_daily_backtest import pin_clock_mode
        pin=pin_clock_mode(self.root,'wall')
        self.assertEqual(pin_clock_mode(self.root,'wall'),pin)
        with self.assertRaises(ValueError):pin_clock_mode(self.root,'monotonic')
    def test_monotonic_clock_reconstructs_receive_time_without_sorting(self):
        records=[dict(Kind='session',Time='2026-10-01T06:55:00+08:00',StopwatchFrequency=10000000),
                 dict(self.market('08:00:01.1234567'),Monotonic=10000000,Sequence=1),
                 dict(self.market('08:00:00.9876543'),Monotonic=10000001,Sequence=2)]
        raw=self.rows('clock.jsonl',records)
        audit=extract_day(raw,self.root/'clock-out.jsonl','20261008',clock_mode='monotonic')
        rows=[json.loads(line) for line in (self.root/'clock-out.jsonl').read_text().splitlines()]
        self.assertEqual(rows[1]['Time'],'2026-10-08T08:00:01.1234568+08:00')
        self.assertEqual(rows[1]['OriginalTime'],'2026-10-08T08:00:00.9876543+08:00')
        self.assertEqual([row['Sequence'] for row in rows],[1,2]);self.assertEqual(audit['clockMode'],'monotonic')

    def test_monotonic_clock_rejects_missing_frequency_and_decreasing_counter(self):
        rows=[dict(self.market('08:00:00'),Monotonic=100,Sequence=1),dict(self.market('08:00:01'),Monotonic=99,Sequence=2)]
        with self.assertRaises(ValueError):extract_day(self.rows('missing-freq.jsonl',rows),self.root/'missing-out.jsonl','20261008',clock_mode='monotonic')
        header=dict(Kind='session',StopwatchFrequency=10000000)
        with self.assertRaises(ValueError):extract_day(self.rows('backward-clock.jsonl',[header]+rows),self.root/'bad-out.jsonl','20261008',clock_mode='monotonic')

    def test_scan_plan_includes_missing_earlier_dates_without_replaying_history(self):
        from incremental_daily_backtest import pending_recording_dates
        self.rows('20261001_nikkei_a.jsonl', [])
        self.rows('20261008_nikkei_b.jsonl', [])
        self.rows('20261009_nikkei_c.jsonl', [])
        self.rows('20260901_nikkei_old.jsonl', [])
        historical=[dict(date='20260915'),dict(date='20261008')]
        result=pending_recording_dates([self.root],historical,{})
        self.assertEqual(list(result),['20261001','20261009'])
        self.assertEqual(list(pending_recording_dates([self.root],historical,{},include_dates={'20261001'})),['20261001'])
        # Registry days remain discoverable so their signatures can be rechecked.
        result=pending_recording_dates([self.root],historical,{'20261008':dict(status='Complete')})
        self.assertEqual(list(result),['20261001','20261008','20261009'])

    def test_coverage_file_mutation_is_rejected_after_read(self):
        from incremental_daily_backtest import pin_coverage_index, verify_coverage_index
        index=self.root/'segment-index.json'
        index.write_text(json.dumps(dict(date='20261001',fullDayCoverage=False)),encoding='utf-8')
        value,pin=pin_coverage_index(index,'20261001')
        self.assertFalse(value['fullDayCoverage']);verify_coverage_index(pin)
        index.write_text(json.dumps(dict(date='20261001',fullDayCoverage=True)),encoding='utf-8')
        with self.assertRaises(ValueError):verify_coverage_index(pin)

    def test_readiness_routes_keep_legacy_and_pruned_datasets_separate(self):
        legacy=dict(schema='nikkei-rl-research-v1',environmentConfig={})
        config=dict(researchReadinessProfile='exclude-flow',featureProfile='mini-micro-research',decisionIntervalSeconds=3)
        pruned=dict(schema='nikkei-rl-research-pruned-flow-v1',observationSize=60,actorExcludedFeatures=list(range(48,54)),environmentConfig=config)
        old=readiness_route(legacy,Path('version'));new=readiness_route(pruned,Path('version'))
        self.assertEqual(old['profile'],'all-research');self.assertEqual(new['profile'],'exclude-flow')
        self.assertNotEqual(old['datasetPath'],new['datasetPath']);self.assertNotEqual(old['cacheDirectory'],new['cacheDirectory'])
        self.assertNotEqual(old['bridge'],new['bridge'])
        for bad in [dict(pruned,schema='nikkei-rl-research-v1'),dict(pruned,actorExcludedFeatures=[48]),dict(pruned,environmentConfig=dict(config,researchReadinessProfile='unknown'))]:
            with self.assertRaises(ValueError):readiness_route(bad,Path('version'))
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def rows(self, name, rows):
        p = self.root / name
        p.write_text(''.join(json.dumps(r)+'\n' for r in rows), encoding='utf-8')
        return p

    def market(self, time, product='225MC', **extra):
        return dict(Kind='trade', Time='2026-10-08T'+time+'+08:00', Product=product,
                    Symbol=product+'-Z26', Last=70000, TradeQty=1, Dropped=0, **extra)

    def test_discover_new_date_without_static_calendar(self):
        self.rows('20261102_nikkei_a.jsonl', [])
        self.rows('20261007_nikkei_b.jsonl', [])
        self.rows('ignore.jsonl', [])
        self.assertEqual(list(discover([self.root], '20261008')), ['20261102'])

    def test_catalog_supports_single_seed_factor_diagnostics(self):
        folder=self.root/'models';folder.mkdir()
        (folder/'policy-seed22.onnx').write_bytes(b'model')
        (folder/'policy-seed22.manifest.json').write_text(json.dumps(dict(seed=22)))
        self.assertEqual([(g,s) for g,s,p in model_entries({'factor':str(folder)})],[('factor',22)])
        (folder/'policy-seed22.manifest.json').write_text(json.dumps(dict(seed=11)))
        with self.assertRaises(ValueError):model_entries({'factor':str(folder)})

    def test_extract_exact_day_micro_and_mini_only(self):
        raw = self.rows('raw.jsonl', [dict(Kind='session', Dropped=0), self.market('07:45:00'),
             self.market('07:45:01', '225'), self.market('14:45:00', '225M'), self.market('16:00:00')])
        out = self.root / 'day.jsonl'
        audit = extract_day(raw, out, '20261008')
        self.assertEqual(audit['marketRows'], 2)
        self.assertEqual([json.loads(s)['Product'] for s in out.read_text().splitlines()], ['225MC','225M'])

    def test_dropped_metadata_rejects_day(self):
        raw = self.rows('raw.jsonl', [self.market('08:00:00'), dict(Kind='status', Dropped=1)])
        with self.assertRaisesRegex(ValueError, 'Dropped'):
            extract_day(raw, self.root/'out.jsonl', '20261008')

    def test_out_of_order_is_rejected_not_sorted(self):
        raw = self.rows('raw.jsonl', [self.market('08:00:01'), self.market('08:00:00')])
        with self.assertRaisesRegex(ValueError, '順序'):
            extract_day(raw, self.root/'out.jsonl', '20261008')

    def test_dotnet_seven_decimal_time_and_submicrosecond_order(self):
        raw = self.rows('raw.jsonl', [self.market('08:00:00.1234567'), self.market('08:00:00.1234568')])
        self.assertEqual(extract_day(raw, self.root/'ok.jsonl', '20261008')['marketRows'], 2)
        raw = self.rows('reverse.jsonl', [self.market('08:00:00.1234568'), self.market('08:00:00.1234567')])
        with self.assertRaisesRegex(ValueError, '順序'):
            extract_day(raw, self.root/'bad.jsonl', '20261008')

    def test_dotnet_variable_fraction_precision(self):
        raw = self.rows('raw.jsonl', [self.market('08:00:00.'+'1'*n) for n in range(1,8)])
        self.assertEqual(extract_day(raw,self.root/'ok.jsonl','20261008')['marketRows'],7)

    def test_overlapping_sessions_are_rejected(self):
        a = self.rows('a.jsonl', [self.market('08:00:00'), self.market('09:00:00')])
        b = self.rows('b.jsonl', [self.market('08:30:00'), self.market('09:30:00')])
        parts = [extract_day(p, self.root/(p.stem+'-part.jsonl'), '20261008') for p in [a,b]]
        with self.assertRaisesRegex(ValueError, '重疊'):
            combine_parts(parts, self.root/'combined.jsonl')

    def test_append_changes_trigger_version_but_unchanged_complete_skips(self):
        entry = dict(signature='A', status='Complete')
        self.assertFalse(needs_processing(entry, 'A'))
        self.assertTrue(needs_processing(entry, 'B'))
        self.assertTrue(needs_processing(dict(signature='A',status='Failed'), 'A'))

    def test_old_newly_added_date_not_pristine_future(self):
        frozen = '20261009'
        self.assertEqual(date_usage('20261008', frozen), 'NewDateRetrospective')
        self.assertEqual(date_usage('20261009', frozen), 'NewDateRetrospective')
        self.assertEqual(date_usage('20261012', frozen), 'FutureFrozenEvaluation')

    def test_empty_scan_writes_registry_and_report(self):
        exp = self.root/'experiment'; exp.mkdir()
        (exp/'analysis-report.json').write_text(json.dumps(dict(daily=[dict(date='20261007',group='baseline',seed=11,extraCostTicksPerSide=0,
             fullDayCoverage=False,trades=0,wins=0,grossPnlJPY=0,netPnlJPY=0,winRatePercent=None)])))
        recordings = self.root/'recordings'; recordings.mkdir()
        out = self.root/'output'
        with patch('incremental_daily_backtest.frozen_files', return_value={}), patch('sys.argv',
                  ['runner','--experiment',str(exp),'--recordings',str(recordings),'--output',str(out),
                   '--catalog',str(self.root/'none.json'),'--historical-report',str(self.root/'none-report.json')]):
            main()
        self.assertTrue((out/'registry.json').is_file())
        self.assertTrue((out/'last-run.json').is_file())
        self.assertFalse((out/'.update.lock').exists())

    def test_failed_extraction_removes_only_owned_temporary_parts(self):
        exp=self.root/'experiment';exp.mkdir()
        (exp/'analysis-report.json').write_text(json.dumps(dict(daily=[dict(date='20260915',group='baseline',seed=11,extraCostTicksPerSide=0,fullDayCoverage=False,trades=0,wins=0,grossPnlJPY=0,netPnlJPY=0,winRatePercent=None)])))
        recordings=self.root/'recordings';recordings.mkdir();raw=recordings/'20261001_nikkei_a.jsonl';raw.write_text('original')
        out=self.root/'output'
        def fail(source,output,date,clock_mode='wall'):
            Path(output).write_text('partial');raise ValueError('Bad recording')
        with patch('incremental_daily_backtest.frozen_files',return_value={}),patch('incremental_daily_backtest.extract_day',side_effect=fail),patch('sys.argv',['runner','--experiment',str(exp),'--recordings',str(recordings),'--output',str(out),'--catalog',str(self.root/'none.json'),'--historical-report',str(self.root/'none-report.json'),'--dates','20261001']):
            with self.assertRaises(ValueError):main()
        self.assertEqual(raw.read_text(),'original')
        self.assertTrue(list(out.glob('versions/*/*/failure.json')))
        self.assertFalse(list(out.glob('versions/*/*/part*.jsonl')))
        self.assertIn('Bad recording',json.loads((out/'registry.json').read_text())['dates']['20261001']['reason'])

    def test_daily_report_uses_new_inclusive_objective_and_all_requested_columns(self):
        exp=self.root/'exp';exp.mkdir();out=self.root/'out';out.mkdir()
        row=dict(date='20261007',group='baseline',seed=11,extraCostTicksPerSide=0,
                 fullDayCoverage=True,trades=50,wins=20,grossPnlJPY=2500,netPnlJPY=-1100,
                 winRatePercent=40,averageWinJPY=100,averageLossJPY=-103.333,initialTarget=False,advancedTarget=False)
        (exp/'analysis-report.json').write_text(json.dumps(dict(daily=[row])))
        state=dict(frozenAt='2026-10-09T08:00:00+08:00',frozenDate='20261009',dates={})
        refresh_report(exp,out,state,cutoff='20261005')
        result=json.loads((out/'daily-performance.json').read_text())['daily'][0]
        self.assertEqual(result['expectedGrossPnlPerTradeJPY'],50)
        self.assertTrue(result['hardTargetPass'])
        self.assertTrue(result['initialTarget'])
        doc=(out/'daily-performance.md').read_text(encoding='utf-8')
        self.assertIn('平均淨獲利',doc);self.assertIn('每筆期望毛損益',doc)


if __name__ == '__main__':
    unittest.main()
