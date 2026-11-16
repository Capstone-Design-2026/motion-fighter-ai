import unittest
from redbool_demo.performance_policy import InferencePolicy


class InferencePolicyTests(unittest.TestCase):
    def sample(self,p,age=160,extension=80,ran=True,gap=33):
        capture=(p._previous_capture or 1_000_000_000)+int(gap*1e6)
        return p.observe(auxiliary_ran=ran,capture_ns=capture,
            primary_end_ns=capture+int((age-extension-2)*1e6),
            auxiliary_end_ns=capture+int((age-2)*1e6),
            processing_end_ns=capture+int(age*1e6))

    def test_healthy_and_transient_overload_keep_full_models(self):
        p=InferencePolicy(True)
        for i in range(50):self.assertIsNone(self.sample(p,age=150 if i%10<2 else 80,extension=30))
        self.assertFalse(p.lite_only);self.assertTrue(p.run_auxiliary(True))
        self.assertEqual(p.snapshot(True)['inference_mode'],'full')

    def test_sustained_overload_latches_after_complete_window(self):
        p=InferencePolicy(True)
        for _ in range(9):self.assertIsNone(self.sample(p))
        transition=self.sample(p)
        self.assertEqual(transition['bad_frames'],10)
        self.assertEqual(p.snapshot(True)['inference_mode'],'lite_overload')
        self.assertFalse(p.run_auxiliary(True))
        self.assertIsNone(self.sample(p))
        self.assertEqual(p.observed,10)

    def test_eight_of_ten_required_and_calibration_does_not_count(self):
        p=InferencePolicy(True)
        for _ in range(40):self.assertIsNone(self.sample(p,ran=False))
        for _ in range(8):self.assertIsNone(self.sample(p))
        self.assertIsNone(self.sample(p,age=80,extension=30))
        self.assertIsNotNone(self.sample(p,age=80,extension=30))
        self.assertEqual(p.observed,10)

    def test_slow_primary_without_auxiliary_cost_does_not_blame_auxiliary(self):
        p=InferencePolicy(True)
        for _ in range(30):self.assertIsNone(self.sample(p,age=300,extension=19))
        self.assertFalse(p.lite_only)
        for _ in range(30):self.assertIsNone(self.sample(p,age=120,extension=80,gap=20))
        self.assertFalse(p.lite_only)

    def test_sustained_watchdog_pressure_without_stale_frames_latches(self):
        p=InferencePolicy(True)
        for _ in range(19):self.assertIsNone(self.sample(p,age=90,extension=30,gap=80))
        transition=self.sample(p,age=90,extension=30,gap=80)
        self.assertEqual(transition['trigger'],'continuity_pressure')
        self.assertEqual(transition['bad_frames'],0)
        self.assertGreaterEqual(transition['pressure_frames'],8)

    def test_gap_pressure_requires_full_window_and_auxiliary_cost(self):
        for extension,expected in [(19,False),(20,True)]:
            p=InferencePolicy(True)
            for _ in range(19):self.assertIsNone(self.sample(p,age=40,extension=extension,gap=105))
            transition=self.sample(p,age=40,extension=extension,gap=105)
            self.assertEqual(transition is not None,expected)

    def test_recalibration_clears_pending_evidence_without_changing_existing_latch(self):
        p=InferencePolicy(True)
        for _ in range(9):self.sample(p)
        self.sample(p,ran=False)
        self.assertEqual(p.snapshot(False)['window'],0)
        self.assertIsNone(self.sample(p))

    def test_pause_and_c_retain_latch_but_new_process_starts_full(self):
        p=InferencePolicy(True)
        for _ in range(10):self.sample(p)
        for calibrated in [False,False,True]:
            self.assertFalse(p.run_auxiliary(calibrated))
            self.assertEqual(p.snapshot(calibrated)['inference_mode'],'lite_overload')
        fresh=InferencePolicy(True)
        self.assertFalse(fresh.run_auxiliary(False));self.assertTrue(fresh.run_auxiliary(True))
        self.assertFalse(fresh.lite_only)

    def test_unconfigured_and_invalid_clocks_cannot_latch(self):
        p=InferencePolicy(False)
        for _ in range(30):self.sample(p)
        self.assertEqual(p.observed,0);self.assertEqual(p.snapshot(True)['inference_mode'],'lite')
        p=InferencePolicy(True)
        for _ in range(30):
            self.assertIsNone(p.observe(auxiliary_ran=True,capture_ns=3,primary_end_ns=2,auxiliary_end_ns=4,processing_end_ns=5))
        self.assertEqual(p.observed,0)


if __name__=='__main__':unittest.main()
