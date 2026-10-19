"""Windows research limits. Never controls the broker process or external data."""
import ctypes as c
from ctypes import wintypes as w
import os, shutil, time, json, threading
from pathlib import Path

GIB=1024**3
class ResourceLimit(RuntimeError): pass

class IO(c.Structure):
    _fields_=[(n,c.c_ulonglong) for n in ('ReadOperationCount','WriteOperationCount','OtherOperationCount','ReadTransferCount','WriteTransferCount','OtherTransferCount')]
class BASIC(c.Structure):
    _fields_=[('PerProcessUserTimeLimit',c.c_longlong),('PerJobUserTimeLimit',c.c_longlong),('LimitFlags',w.DWORD),('MinimumWorkingSetSize',c.c_size_t),('MaximumWorkingSetSize',c.c_size_t),('ActiveProcessLimit',w.DWORD),('Affinity',c.c_size_t),('PriorityClass',w.DWORD),('SchedulingClass',w.DWORD)]
class EXTENDED(c.Structure):
    _fields_=[('BasicLimitInformation',BASIC),('IoInfo',IO),('ProcessMemoryLimit',c.c_size_t),('JobMemoryLimit',c.c_size_t),('PeakProcessMemoryUsed',c.c_size_t),('PeakJobMemoryUsed',c.c_size_t)]
class MEMORY(c.Structure):
    _fields_=[('length',w.DWORD),('load',w.DWORD)]+[(n,c.c_ulonglong) for n in ('totalPhys','availPhys','totalPage','availPage','totalVirtual','availVirtual','availExtended')]

def available_ram():
    m=MEMORY();m.length=c.sizeof(m)
    if not c.windll.kernel32.GlobalMemoryStatusEx(c.byref(m)):raise c.WinError()
    return m.availPhys

class Job:
    def __init__(self,limit):
        self.k=c.WinDLL('kernel32',use_last_error=True)
        self.k.CreateJobObjectW.restype=w.HANDLE
        self.k.CreateJobObjectW.argtypes=[c.c_void_p,w.LPCWSTR]
        for name in ('SetInformationJobObject','QueryInformationJobObject'):
            f=getattr(self.k,name);f.argtypes=[w.HANDLE,c.c_int,c.c_void_p,w.DWORD,c.c_void_p] if name.startswith('Query') else [w.HANDLE,c.c_int,c.c_void_p,w.DWORD]
        self.k.AssignProcessToJobObject.argtypes=[w.HANDLE,w.HANDLE]
        self.k.TerminateJobObject.argtypes=[w.HANDLE,w.UINT]
        self.k.CloseHandle.argtypes=[w.HANDLE]
        self.handle=self.k.CreateJobObjectW(None,None)
        if not self.handle:raise c.WinError(c.get_last_error())
        info=EXTENDED();info.BasicLimitInformation.LimitFlags=0x200|0x2000|0x20
        info.BasicLimitInformation.PriorityClass=0x4000;info.JobMemoryLimit=limit
        if not self.k.SetInformationJobObject(self.handle,9,c.byref(info),c.sizeof(info)):
            self.close();raise ResourceLimit('Cannot set Job Object memory limit')
    def assign(self,p):
        if not self.k.AssignProcessToJobObject(self.handle,w.HANDLE(int(p._handle))):
            p.terminate();raise ResourceLimit('Cannot assign research to memory-limited job')
    def peak(self):
        info=EXTENDED()
        if not self.k.QueryInformationJobObject(self.handle,9,c.byref(info),c.sizeof(info),None):raise c.WinError(c.get_last_error())
        return info.PeakJobMemoryUsed
    def working_set(self):
        import struct
        buffer=c.create_string_buffer(8+8*256)
        if not self.k.QueryInformationJobObject(self.handle,3,buffer,len(buffer),None):raise c.WinError(c.get_last_error())
        count=struct.unpack_from('II',buffer.raw)[1]
        if count>256:raise ResourceLimit('Too many research children')
        class COUNTERS(c.Structure):
            _fields_=[('cb',w.DWORD),('faults',w.DWORD)]+[(n,c.c_size_t) for n in ('peakWorking','working','peakPaged','paged','peakNonpaged','nonpaged','pagefile','peakPagefile','private')]
        self.k.OpenProcess.restype=w.HANDLE;self.k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD]
        getmem=c.windll.psapi.GetProcessMemoryInfo;getmem.argtypes=[w.HANDLE,c.c_void_p,w.DWORD]
        total=0
        for i in range(count):
            pid=struct.unpack_from('Q',buffer.raw,8+8*i)[0];handle=self.k.OpenProcess(0x410,False,pid)
            if not handle:continue # Process may have exited between job enumeration and query.
            try:
                m=COUNTERS();m.cb=c.sizeof(m)
                if getmem(handle,c.byref(m),m.cb):total+=m.working
            finally:self.k.CloseHandle(handle)
        return total
    def stop(self):self.k.TerminateJobObject(self.handle,90)
    def close(self):
        if self.handle:self.k.CloseHandle(self.handle);self.handle=None
    def __enter__(self):return self
    def __exit__(self,*args):self.close()

class Rate:
    def __init__(self,bytes_per_second):
        self.rate=bytes_per_second;self.last=time.monotonic();self.capacity=256*1024;self.tokens=float(self.capacity)
    def consume(self,n):
        while n:
            chunk=min(n,self.capacity);now=time.monotonic()
            self.tokens=min(self.capacity,self.tokens+(now-self.last)*self.rate);self.last=now
            if self.tokens<chunk:
                time.sleep((chunk-self.tokens)/self.rate)
                later=time.monotonic();self.tokens=max(0,min(self.capacity,self.tokens+(later-self.last)*self.rate)-chunk);self.last=later
            else:self.tokens-=chunk
            n-=chunk

READ_RATE=Rate(20*1024**2);WRITE_RATE=Rate(5*1024**2)
def limited_read(path):
    import io
    result=io.BytesIO()
    with Path(path).open('rb') as f:
        while True:
            chunk=f.read(256*1024)
            if not chunk:break
            READ_RATE.consume(len(chunk));result.write(chunk)
            if result.tell()>256*1024**2:raise ResourceLimit('Read exceeds bounded single file')
    result.seek(0);return result

def limited_write(path,data,root):
    Budget(root).reserve(len(data))
    with Path(path).open('wb') as f:
        for start in range(0,len(data),256*1024):
            chunk=memoryview(data)[start:start+256*1024]
            WRITE_RATE.consume(len(chunk));f.write(chunk)

class Budget:
    def __init__(self,root,max_bytes=5*GIB,max_file=256*1024**2,reserve_free=30*GIB):
        self.root=Path(root).resolve();self.max_bytes=max_bytes;self.max_file=max_file;self.reserve_free=reserve_free
    def usage(self):
        size=count=0
        def scan_error(error):
            if not isinstance(error,FileNotFoundError):raise ResourceLimit('Quota scan unavailable: '+str(error))
        for directory,subdirs,files in os.walk(self.root,onerror=scan_error,followlinks=False):
            for name in files:
                try:info=(Path(directory)/name).stat()
                except FileNotFoundError:continue
                size+=info.st_size;count+=1
        return size,count
    def reserve(self,n,current_file=0):
        size,count=self.usage()
        if n+current_file>self.max_file or size+n>self.max_bytes or count>=200:raise ResourceLimit('Research file quota exceeded')
        if shutil.disk_usage(self.root).free-n<self.reserve_free:raise ResourceLimit('C drive reserve violated')
    def check(self):self.reserve(0)

def safe_cleanup(root,paths):
    root=Path(root).resolve();checked=[]
    for value in paths:
        p=Path(value)
        if p==root or not p.resolve().is_relative_to(root):raise ResourceLimit('Cleanup outside experiment')
        for q in [p,*p.parents]:
            if q==root:break
            if q.exists() and q.lstat().st_file_attributes&0x400:raise ResourceLimit('Cleanup through reparse point')
        if p.exists() and p.is_dir():
            for q in p.rglob('*'):
                if q.lstat().st_file_attributes&0x400:raise ResourceLimit('Reparse child rejected')
        checked.append(p)
    for p in checked:
        if p.is_dir():shutil.rmtree(p)
        elif p.exists():p.unlink()

def guarded_run(command,root):
    """Child waits on stdin until assigned; descendants inherit the Job Object."""
    import subprocess
    root=Path(root).resolve();root.mkdir(parents=True,exist_ok=True)
    if available_ram()<8*GIB:raise ResourceLimit('Available RAM below 8GiB')
    budget=Budget(root);budget.check()
    temp=root/'scratch';temp.mkdir(exist_ok=True)
    env=dict(os.environ,TEMP=str(temp),TMP=str(temp),MPLCONFIGDIR=str(temp),PYTHONDONTWRITEBYTECODE='1',OMP_NUM_THREADS='2',MKL_NUM_THREADS='2',OPENBLAS_NUM_THREADS='2',NUMEXPR_NUM_THREADS='2',CUDA_VISIBLE_DEVICES='')
    report={'memoryLimitBytes':4*GIB,'readMiBps':20,'writeMiBps':5,'cpuThreads':2,'peakJobCommitBytes':0,'outcome':'running'}
    # Logs are bounded by worker's sparse progress; not per-row/step output.
    log=root/'progress.log'
    with Job(4*GIB) as job,log.open('w',encoding='utf-8') as output:
        wrapper="import sys,runpy,os;sys.stdin.readline();sys.argv=sys.argv[1:];sys.path.insert(0,os.path.dirname(sys.argv[0]));runpy.run_path(sys.argv[0],run_name='__main__')"
        p=subprocess.Popen([command[0],'-B','-c',wrapper,*command[1:]],cwd=root,env=env,stdin=subprocess.PIPE,stdout=output,stderr=subprocess.STDOUT,text=True)
        try:
            job.assign(p);p.stdin.write('\n');p.stdin.flush();p.stdin.close()
            while p.poll() is None:
                report['peakJobCommitBytes']=max(report['peakJobCommitBytes'],job.peak())
                working=job.working_set();report['peakWorkingSetBytes']=max(report.get('peakWorkingSetBytes',0),working)
                if working>3*GIB:raise ResourceLimit('Research working set exceeds 3GiB')
                budget.check()
                if available_ram()<8*GIB:raise ResourceLimit('System RAM reserve violated')
                time.sleep(2)
            report['returnCode']=p.returncode
            report['outcome']='completed' if p.returncode==0 else 'failed'
        except BaseException as e:
            job.stop();p.wait(timeout=10);report['outcome']='stopped';report['reason']=str(e)
        finally:
            report['peakJobCommitBytes']=max(report['peakJobCommitBytes'],job.peak())
            safe_cleanup(root,[temp,root/'cache',root/'checkpoints',root/'replay-temp',root/'resume-cache'])
    report['retainedBytes']=budget.usage()[0]
    if report['retainedBytes']>100*1024**2:
        # Keep compact metrics/manifests; avoid silently leaving excess binaries.
        safe_cleanup(root,[root/'models',root/'runtime',root/'deps'])
        report['outcome']='retention-limit-failed';report['retainedBytes']=budget.usage()[0]
    (root/'resource-summary.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    return report
