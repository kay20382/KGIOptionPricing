import tempfile, unittest
from pathlib import Path
from research_resource_guard import Budget, ResourceLimit, safe_cleanup, Job
class ResourceTests(unittest.TestCase):
    def test_write_is_throttled_in_chunks_before_each_write(self):
        from unittest.mock import patch, mock_open
        from research_resource_guard import limited_write
        events=[];handle=mock_open()
        handle.return_value.write.side_effect=lambda chunk:events.append(('write',len(chunk)))
        with patch('research_resource_guard.Budget'),patch('research_resource_guard.Path.open',handle),patch('research_resource_guard.WRITE_RATE.consume',lambda n:events.append(('consume',n))):
            limited_write(Path('unused'),b'x'*(256*1024+7),Path('.'))
        self.assertEqual(events,[('consume',256*1024),('write',256*1024),('consume',7),('write',7)])
    def test_rate_credits_windows_sleep_overshoot(self):
        from unittest.mock import patch
        from research_resource_guard import Rate
        clock=[0.];sleeps=[]
        def sleep(delay):sleeps.append(delay);clock[0]+=max(.016,delay)
        with patch('research_resource_guard.time.monotonic',lambda:clock[0]),patch('research_resource_guard.time.sleep',sleep):
            r=Rate(1000);r.capacity=10;r.tokens=0;r.consume(1);r.consume(1)
            self.assertEqual(len(sleeps),1)
    def test_bounded_files_roundtrip(self):
        from research_resource_guard import limited_write,limited_read
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'x';limited_write(p,b'abc',Path(d));self.assertEqual(limited_read(p).read(),b'abc')
    def test_budget_rejects_before_oversized_write(self):
        with tempfile.TemporaryDirectory() as d:
            b=Budget(Path(d),max_bytes=10,max_file=8,reserve_free=0)
            with self.assertRaises(ResourceLimit):b.reserve(9)
            self.assertEqual(list(Path(d).iterdir()),[])
    def test_cleanup_cannot_escape(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)/'experiment';root.mkdir();outside=Path(d)/'source';outside.write_text('original')
            with self.assertRaises(ResourceLimit):safe_cleanup(root,[outside])
            self.assertTrue(outside.exists())
    def test_cleanup_keeps_results(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);temp=root/'scratch';temp.mkdir();(temp/'x').write_text('cache')
            (root/'result.json').write_text('{}');safe_cleanup(root,[temp]);self.assertFalse(temp.exists());self.assertTrue((root/'result.json').exists())
    def test_windows_job_can_assign_child(self):
        import sys,subprocess
        with Job(4*1024**3) as job:
            p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(1)']);job.assign(p);self.assertEqual(p.wait(),0)
    def test_quota_scan_tolerates_build_directory_disappearing(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);stable=root/'stable';stable.write_bytes(b'abc')
            def disappearing(*args,**kwargs):
                yield stable
                raise FileNotFoundError('MSBuild temporary directory removed during enumeration')
            with patch.object(Path,'rglob',disappearing):
                size,count=Budget(root,reserve_free=0).usage()
                self.assertGreaterEqual(size,3);self.assertGreaterEqual(count,1)
if __name__=='__main__':unittest.main()