"""Calendar context; absent economic schedule stays Unknown, never 'no event'."""
import math
from datetime import datetime,timedelta,time,timezone
from zoneinfo import ZoneInfo

TAIPEI=ZoneInfo('Asia/Taipei');NY=ZoneInfo('America/New_York');CHICAGO=ZoneInfo('America/Chicago')
# Published NYSE regular closures; experimental historic dates are versioned separately.
NYSE_2026={'2026-01-01','2026-01-19','2026-02-16','2026-04-03','2026-05-25','2026-06-19','2026-07-03','2026-09-07','2026-11-26','2026-12-25'}
EARLY_2026={'2026-11-27','2026-12-24'}

def context_features(at,calendar):
    local=at.astimezone(TAIPEI);et=at.astimezone(NY);ct=at.astimezone(CHICAGO)
    def at_local(date,h,m=0):return datetime.combine(date,time(h,m),TAIPEI)
    day_open=at_local(local.date(),7,45);day_close=at_local(local.date(),14,45)
    night_date=local.date()-timedelta(days=1) if local.hour<5 else local.date()
    night_open=at_local(night_date,16);night_close=at_local(night_date+timedelta(days=1),5)
    date=et.date().isoformat();us_valid=et.weekday()<5 and date not in NYSE_2026
    us_open=datetime.combine(et.date(),time(9,30),NY)
    us_close=datetime.combine(et.date(),time(13 if date in EARLY_2026 else 16),NY)
    regular=us_valid and us_open<=et<us_close
    # Generic Globex weekday/maintenance context. Holiday overrides are mandatory
    # before calling this a formally verified CME calendar.
    electronic=(ct.weekday()<4 or ct.weekday()==4 and ct.hour<16 or ct.weekday()==6 and ct.hour>=17) and ct.hour!=16
    before=[];after=[];available=date in calendar
    for stamp in calendar.get(date,[]):
        event=datetime.fromisoformat(stamp)
        minutes=(event-at).total_seconds()/60
        (before if minutes>=0 else after).append(abs(minutes))
    until=min(before,default=1440);since=min(after,default=1440)
    minutes=local.hour*60+local.minute+local.second/60
    def elapsed(open):return (at-open).total_seconds()/60
    def remaining(close):return (close-at).total_seconds()/60
    flags=[0<=elapsed(day_open)<15,0<remaining(day_close)<=15,
           0<=elapsed(night_open)<15,0<remaining(night_close)<=15,
           available and (until<=5 or since<15),us_valid and 0<=elapsed(us_open)<30,
           regular,electronic and not regular]
    distances=[elapsed(day_open),remaining(day_close),elapsed(night_open),remaining(night_close),elapsed(us_open),remaining(us_close)]
    return [*map(float,flags),math.sin(2*math.pi*minutes/1440),math.cos(2*math.pi*minutes/1440),*[max(-1,min(1,x/1440)) for x in distances],min(1,until/1440),min(1,since/1440),float(available)]

def session(at):
    at=at.astimezone(TAIPEI);t=at.time().replace(tzinfo=None)
    if time(7,45)<=t<time(14,40):return at.date().isoformat()+'-day'
    if t>=time(16) or t<time(4,55):
        date=at.date()-timedelta(days=1) if t<time(5) else at.date()
        return date.isoformat()+'-night'
    return None
