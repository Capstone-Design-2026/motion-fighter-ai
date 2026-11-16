"""Bounded Windows startup format trial on the same already-open device.

Probe images are discarded before recognition starts. No camera is opened,
closed or reindexed here, and exposure/gain are never changed. Native read()
may block inside a driver; cancellation/deadlines are checked between reads.
"""
import math
import platform

from realtime_vision.clock import now_ns

YUY2 = int.from_bytes(b'YUY2', 'little')
MJPG = int.from_bytes(b'MJPG', 'little')


def format_name(value):
    try:
        n = int(value)
        raw = n.to_bytes(4, 'little')
        return raw.decode('ascii') if all(32 <= c <= 126 for c in raw) else 'UNKNOWN'
    except (TypeError, ValueError, OverflowError, UnicodeError):
        return 'UNKNOWN'


def eligible(cap, *, system=None):
    if (platform.system() if system is None else system) != 'Windows':
        return False
    try:
        return cap.getBackendName().upper() == 'DSHOW'
    except Exception:
        return False


def _read_fourcc(cv, cap):
    try:
        value = float(cap.get(cv.CAP_PROP_FOURCC))
        return int(value) if math.isfinite(value) and value > 0 else None
    except Exception:
        return None


def _measure(cap, stop, clock_ns, *, max_samples=12, budget_ns=2_000_000_000):
    start = clock_ns(); stamps=[]; reads=0; warmup=0; reason='sample_limit';error=None
    while reads < max_samples+2:
        if stop.is_set():reason='cancelled';break
        if clock_ns()-start >= budget_ns:reason='time_limit';break
        try:ok, image = cap.read()
        except Exception as exc:
            reads+=1;reason='read_error';error=repr(exc)[:200];break
        stamp=clock_ns(); reads+=1
        if not ok:reason='read_failed';break
        if (image is None or getattr(image,'shape',None)!=(480,640,3)
                or str(getattr(image,'dtype',''))!='uint8'):
            reason='invalid_bgr';break
        if warmup < 2:warmup+=1;continue
        stamps.append(stamp)
    if stop.is_set():reason='cancelled'
    gaps=[(b-a)/1e6 for a,b in zip(stamps,stamps[1:])]
    valid = len(stamps)>=5 and all(g>0 for g in gaps) and reason in ('sample_limit','time_limit')
    ordered=sorted(gaps)
    def q95():
        if not ordered:return None
        x=(len(ordered)-1)*.95;i=int(x)
        return ordered[i]+(ordered[min(i+1,len(ordered)-1)]-ordered[i])*(x-i)
    return dict(valid=valid,reason=reason,error=error,reads=reads,warmup_frames=warmup,
        measured_frames=len(stamps),first_capture_ns=stamps[0] if stamps else None,
        last_capture_ns=stamps[-1] if stamps else None,
        measured_fps=(len(stamps)-1)*1e9/(stamps[-1]-stamps[0]) if valid else None,
        gap_p95_ms=q95(),max_gap_ms=max(gaps) if gaps else None,
        elapsed_ms=(clock_ns()-start)/1e6)


def _set_profile(cv, cap, fourcc):
    accepted={}
    for name,prop,value in [('fourcc',cv.CAP_PROP_FOURCC,fourcc),
            ('width',cv.CAP_PROP_FRAME_WIDTH,640),('height',cv.CAP_PROP_FRAME_HEIGHT,480),
            ('fps',cv.CAP_PROP_FPS,30)]:
        try:accepted[name]=bool(cap.set(prop,value))
        except Exception as exc:
            accepted[name]=False;accepted.setdefault('errors',{})[name]=repr(exc)[:200]
    return accepted


def select_startup_profile(cv, cap, stop, *, clock_ns=now_ns, system=None):
    """Preserve healthy DSHOW; try MJPG only for measured slow YUY2 input.

    A profile is retained only with a measurable >=20% and >=3fps gain and
    no worse p95/max capture gap. At most three bounded trials occur: baseline,
    MJPG, and a restoration check. Return None without touching non-DSHOW or
    non-Windows devices. The caller owns final device cleanup on every exit.
    """
    if not eligible(cap,system=system):return None
    original=_read_fourcc(cv,cap)
    report=dict(version=1,backend='DSHOW',selected='DSHOW/'+format_name(original),
        changed=False,attempts=[],probe_reads=0,probe_frames_published=0,
        requested=dict(width=640,height=480,fps=30),
        limits=dict(samples=12,warmup=2,trial_budget_seconds=2,max_trials=3),
        native_read_timeout='Cancellation and trial budgets checked between driver reads',
        exposure_gain_written=False)
    def trial(name, **kwargs):
        data=_measure(cap,stop,clock_ns,**kwargs);report['attempts'].append(dict(profile=name,**data))
        report['probe_reads']+=data['reads'];return data
    baseline=trial(report['selected']); selected=baseline
    if baseline['reason'] in ('read_failed','read_error'):raise RuntimeError('Camera read failed during startup check')
    if baseline['reason']=='cancelled':report['cancelled']=True
    elif not baseline['valid']:report['decision']='baseline_not_measurable'
    elif baseline['measured_fps']>=18:report['decision']='healthy_default_unchanged'
    elif original!=YUY2:report['decision']='slow_default_format_not_trialled'
    else:
        accepted=_set_profile(cv,cap,MJPG); candidate=trial('DSHOW/MJPG')
        candidate_format=_read_fourcc(cv,cap)
        gain=(candidate['valid'] and candidate['measured_fps']>=baseline['measured_fps']*1.2
            and candidate['measured_fps']>=baseline['measured_fps']+3
            and candidate['gap_p95_ms']<=baseline['gap_p95_ms']
            and candidate['max_gap_ms']<=baseline['max_gap_ms'])
        report['candidate_setting_accepted']=accepted
        report['candidate_reported_fourcc']=format_name(candidate_format)
        if accepted['fourcc'] and candidate_format==MJPG and gain and not stop.is_set():
            selected=candidate;report.update(changed=True,selected='DSHOW/MJPG',decision='measured_improvement')
        else:
            report['restore_setting_accepted']=_set_profile(cv,cap,original)
            if stop.is_set():report.update(cancelled=True,decision='cancelled_restored')
            else:
                restored=trial('DSHOW/'+format_name(original),max_samples=5)
                selected=restored
                if restored['reason']=='cancelled' or stop.is_set():
                    # Settings were restored before this trial. User cancellation
                    # ends validation; the owner releases the device, no frame is
                    # published and no recovery failure is falsely reported.
                    report.update(cancelled=True,decision='cancelled_restored')
                elif not restored['valid'] or _read_fourcc(cv,cap)!=original:
                    raise RuntimeError('Camera format could not be restored; reconnect the camera')
                else:report['decision']='candidate_rejected_default_restored'
    report['acquisition_fps']=selected['measured_fps']
    report['camera_slow']=selected['measured_fps'] is not None and selected['measured_fps']<15
    return report
