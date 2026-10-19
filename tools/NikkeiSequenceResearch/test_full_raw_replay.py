import csv,json,subprocess,sys,tempfile,hashlib
from pathlib import Path
from datetime import datetime,timedelta

def main(root):
    root=Path(root);temp=root/'raw-risk-test';temp.mkdir(exist_ok=True)
    quotes=temp/'quotes.csv';tape=temp/'trades.csv';raw=temp/'raw.jsonl'
    t=datetime.fromisoformat('2026-10-06T08:00:00+08:00')
    with quotes.open('w',newline='') as f:
        w=csv.writer(f);w.writerow(['time','bid','ask','valid','down','neutral','up','delta','trend60','trend300'])
        w.writerow([t.timestamp(),10000,10005,1,.1,.1,.8,10,1,1]);w.writerow([(t+timedelta(seconds=5)).timestamp(),10010,10015,1,.1,.1,.8,10,1,1])
    tape.write_text('time,price,sequence\n')
    def trade(seconds,seq,price):return dict(Time=(t+timedelta(seconds=seconds)).isoformat(),Sequence=seq,Kind='trade',Product='225MC',Last=price,TradeQty=1)
    def book(seconds,seq,bid,ask):return dict(Time=(t+timedelta(seconds=seconds)).isoformat(),Sequence=seq,Kind='depth',Product='225MC',Bid=[bid-i*5 for i in range(5)],Ask=[ask+i*5 for i in range(5)],BidQty=[10]*5,AskQty=[10]*5)
    rows=[trade(.002,1,9999),book(2,2,8990,9020),book(3,3,10000,10005),trade(3.002,4,10010)]
    cmd=['dotnet',str(root/'runtime/NikkeiSequenceResearch.dll'),str(quotes),str(tape),'0','.6','True','150','True','.25','.5','30',str(raw)]
    raw.write_text('\n'.join(json.dumps(r) for r in rows)+'\n')
    result=subprocess.run(cmd,capture_output=True,text=True,check=True,timeout=30);r=json.loads(result.stdout)
    assert r['completedTrades']==1 and r['netPnl']==28 and r['maximumIntradayLoss']>10000 and r['dailyStopped'],r
    assert r['rawSourceSha256']==hashlib.sha256(raw.read_bytes()).hexdigest(),r
    valid=dict(r)
    rows[1]['Ask'][0]=0;raw.write_text('\n'.join(json.dumps(r) for r in rows)+'\n')
    invalid=subprocess.run(cmd,capture_output=True,text=True,timeout=30)
    assert invalid.returncode!=0 and 'Invalid raw risk book' in invalid.stderr,invalid
    (root/'full-raw-risk-test.json').write_text(json.dumps(dict(passed=True,valid=valid,invalidBookRejected=True),indent=2))
    for path in (quotes,tape,raw):path.unlink()
    temp.rmdir();print('PASS full raw route: unsampled wide book dip retained, invalid book rejected')
if __name__=='__main__':main(sys.argv[1])