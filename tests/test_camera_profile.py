"""Camera format negotiation with deterministic fake driver clocks; no devices."""
import json
from types import SimpleNamespace
import threading
import unittest

from redbool_demo.camera_profile import (YUY2, MJPG, eligible, format_name,
                                         select_startup_profile)

CV=SimpleNamespace(CAP_PROP_FOURCC=1,CAP_PROP_FRAME_WIDTH=2,
                   CAP_PROP_FRAME_HEIGHT=3,CAP_PROP_FPS=4)


class FakeCamera:
    def __init__(self,base_ms=100,candidate_ms=33,*,stop=None):
        self.format=YUY2;self.initial_format=YUY2;self.base_ms=base_ms;self.candidate_ms=candidate_ms
        self.ns=1_000_000_000;self.reads=0;self.sets=[];self.stop=stop
        self.reject_candidate=False;self.invalid_candidate=False;self.fail_candidate=False
        self.fail_restore=False;self.raise_candidate=False;self.cancel_at=None;self.base_shape=(480,640,3)
    def clock(self):return self.ns
    def getBackendName(self):return 'DSHOW'
    def get(self,prop):return self.format if prop==CV.CAP_PROP_FOURCC else 0
    def set(self,prop,value):
        self.sets.append((prop,value))
        if prop==CV.CAP_PROP_FOURCC:
            if value==MJPG and self.reject_candidate:return False
            self.format=value
        return True
    def read(self):
        self.reads+=1
        if self.cancel_at and self.reads>=self.cancel_at:self.stop.set()
        candidate=self.format==MJPG
        interval=self.candidate_ms if candidate else self.base_ms
        if isinstance(interval,list):interval=interval[(self.reads-1)%len(interval)]
        self.ns+=int(interval*1e6)
        if candidate and self.raise_candidate:raise RuntimeError('candidate driver read failed')
        if (candidate and self.fail_candidate) or (not candidate and self.fail_restore and self.sets):return False,None
        shape=(1,1,3) if candidate and self.invalid_candidate else self.base_shape
        return True,SimpleNamespace(shape=shape,dtype='uint8')


class CameraProfileTests(unittest.TestCase):
    def run_profile(self,cap,stop=None,system='Windows'):
        return select_startup_profile(CV,cap,stop or threading.Event(),clock_ns=cap.clock,system=system)

    def test_mac_and_non_dshow_do_not_read_or_set_anything(self):
        cap=FakeCamera();self.assertIsNone(self.run_profile(cap,system='Darwin'))
        cap.getBackendName=lambda:'MSMF';self.assertIsNone(self.run_profile(cap))
        self.assertEqual(cap.reads,0);self.assertEqual(cap.sets,[])

    def test_healthy_windows_default_is_unchanged_and_actual_timestamps_are_used(self):
        cap=FakeCamera(base_ms=40);r=self.run_profile(cap)
        self.assertFalse(r['changed']);self.assertEqual(r['decision'],'healthy_default_unchanged')
        self.assertEqual(r['acquisition_fps'],25.);self.assertEqual(cap.sets,[])
        self.assertEqual(r['probe_reads'],14);self.assertEqual(r['probe_frames_published'],0)
        self.assertFalse(r['camera_slow']);json.dumps(r,allow_nan=False)

    def test_only_measured_improved_mjpg_is_retained_on_same_device(self):
        cap=FakeCamera();r=self.run_profile(cap)
        self.assertTrue(r['changed']);self.assertEqual(r['selected'],'DSHOW/MJPG')
        self.assertEqual(cap.format,MJPG);self.assertAlmostEqual(r['acquisition_fps'],1000/33)
        self.assertEqual(r['attempts'][0]['measured_fps'],10.)
        self.assertEqual({p for p,_ in cap.sets},{1,2,3,4})
        self.assertFalse(r['exposure_gain_written']);self.assertEqual(r['probe_reads'],28)

    def test_insufficient_gain_and_worse_tail_restore_default_and_validate(self):
        for candidate in [95,85,[20]*10+[220]]:
            cap=FakeCamera(candidate_ms=candidate);r=self.run_profile(cap)
            self.assertFalse(r['changed']);self.assertEqual(cap.format,YUY2)
            self.assertEqual(r['decision'],'candidate_rejected_default_restored')
            self.assertEqual(r['attempts'][-1]['profile'],'DSHOW/YUY2')
            self.assertEqual(r['attempts'][-1]['measured_frames'],5)
            self.assertTrue(r['camera_slow'])

    def test_unsupported_malformed_and_failed_candidate_restore_default(self):
        for flag in ['reject_candidate','invalid_candidate','fail_candidate','raise_candidate']:
            cap=FakeCamera();setattr(cap,flag,True);r=self.run_profile(cap)
            self.assertFalse(r['changed']);self.assertEqual(cap.format,YUY2)
            self.assertTrue(r['attempts'][-1]['valid']);self.assertTrue(r['camera_slow'])

    def test_failed_restore_is_explicit_instead_of_publishing_unknown_format(self):
        cap=FakeCamera(candidate_ms=100);cap.fail_restore=True
        with self.assertRaisesRegex(RuntimeError,'could not be restored'):self.run_profile(cap)

    def test_unknown_format_and_malformed_baseline_are_never_changed(self):
        cap=FakeCamera();cap.format=int.from_bytes(b'NV12','little')
        r=self.run_profile(cap);self.assertFalse(r['changed']);self.assertEqual(cap.sets,[])
        cap=FakeCamera();cap.base_shape=(720,1280,3);r=self.run_profile(cap)
        self.assertEqual(r['decision'],'baseline_not_measurable');self.assertEqual(cap.sets,[])
        self.assertIsNone(r['acquisition_fps'])

    def test_trial_budget_and_read_limit_are_bounded(self):
        cap=FakeCamera(base_ms=1000);r=self.run_profile(cap)
        self.assertLessEqual(cap.reads,3);self.assertFalse(r['attempts'][0]['valid'])
        self.assertEqual(r['attempts'][0]['reason'],'time_limit')
        self.assertEqual(cap.sets,[])

    def test_cancel_during_candidate_restores_without_more_reads(self):
        stop=threading.Event();cap=FakeCamera(stop=stop);cap.cancel_at=17
        r=self.run_profile(cap,stop);self.assertTrue(r['cancelled']);self.assertEqual(cap.format,YUY2)
        self.assertEqual(cap.reads,17);self.assertFalse(r['changed'])

    def test_cancel_during_restoration_validation_is_not_a_recovery_error(self):
        stop=threading.Event();cap=FakeCamera(candidate_ms=100,stop=stop);cap.cancel_at=31
        r=self.run_profile(cap,stop)
        self.assertTrue(r['cancelled']);self.assertFalse(r['changed'])
        self.assertEqual(r['decision'],'cancelled_restored');self.assertEqual(cap.format,YUY2)
        self.assertEqual(cap.reads,31);self.assertEqual(r['attempts'][-1]['reason'],'cancelled')
        self.assertIsNone(r['acquisition_fps'])
        self.assertEqual(cap.sets[-4:],[(CV.CAP_PROP_FOURCC,YUY2),
            (CV.CAP_PROP_FRAME_WIDTH,640),(CV.CAP_PROP_FRAME_HEIGHT,480),(CV.CAP_PROP_FPS,30)])

    def test_pre_cancelled_does_not_read_or_change_format(self):
        stop=threading.Event();stop.set();cap=FakeCamera();r=self.run_profile(cap,stop)
        self.assertTrue(r['cancelled']);self.assertEqual(cap.reads,0);self.assertEqual(cap.sets,[])

    def test_failed_baseline_read_is_explicit(self):
        cap=FakeCamera();cap.read=lambda:(False,None)
        with self.assertRaisesRegex(RuntimeError,'read failed'):self.run_profile(cap)

    def test_format_decoder_handles_unsupported_values(self):
        self.assertEqual(format_name(YUY2),'YUY2');self.assertEqual(format_name(MJPG),'MJPG')
        for x in [None,0,float('nan'),-1,2**40]:self.assertEqual(format_name(x),'UNKNOWN')


if __name__=='__main__':unittest.main()
