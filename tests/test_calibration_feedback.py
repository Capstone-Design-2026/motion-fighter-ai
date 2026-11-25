import unittest
import numpy as np
from redbool_demo.calibration_feedback import CalibrationFeedback, calibration_reasons, MESSAGES


class CalibrationFeedbackTests(unittest.TestCase):
    def arms(self, **changes):
        arm={'position':np.array([.1,.1,0.]),'speed_xy':.1,'angle':.4,'angle3d':1.8}
        arm.update(changes)
        return {'LEFT':dict(arm),'RIGHT':dict(arm)}

    def test_steady_guard_explains_automatic_progress(self):
        self.assertEqual(calibration_reasons(self.arms(),[],True),['HOLD_GUARD'])
        feedback=CalibrationFeedback()
        feedback.observe(['HOLD_GUARD'],1_000_000_000)
        self.assertIn('NO NEED TO PRESS C AGAIN',feedback.message)

    def test_joint_failure_and_movement_are_not_reported_as_success(self):
        self.assertIn('JOINTS_OUTSIDE',calibration_reasons({},['joint_15_outside'],True))
        self.assertIn('JOINTS_UNCERTAIN',calibration_reasons({},['joint_16_visibility'],True))
        self.assertIn('ARMS_MOVING',calibration_reasons(self.arms(speed_xy=1.2),[],True))
        self.assertIn('STALE_FRAME',calibration_reasons(self.arms(),[],False))

    def test_height_extension_and_guard_shift_explanations(self):
        self.assertIn('FISTS_LOW',calibration_reasons(self.arms(position=np.array([.1,.5,0.])),[],True))
        self.assertIn('FISTS_HIGH',calibration_reasons(self.arms(position=np.array([.1,-.9,0.])),[],True))
        self.assertIn('ARMS_EXTENDED',calibration_reasons(self.arms(angle3d=2.5),[],True))
        self.assertIn('GUARD_SHIFTED',calibration_reasons(self.arms(),[],True,True))

    def test_histogram_is_bounded_and_reports_only_every_two_seconds(self):
        feedback=CalibrationFeedback();reports=[]
        for i in range(301):
            row=feedback.observe(['ARMS_MOVING','unknown-'+str(i)],1_000_000_000+i*33_333_334)
            if row:reports.append(row)
        self.assertEqual(len(reports),6)
        self.assertEqual(set(feedback.counts),set(MESSAGES))
        self.assertEqual(set(feedback.window_counts),set(MESSAGES))
        self.assertEqual(feedback.counts['ARMS_MOVING'],301)
        self.assertLessEqual(sum(feedback.window_counts.values()),60)

    def test_completion_has_a_final_summary_even_before_interval(self):
        feedback=CalibrationFeedback();feedback.observe(['HOLD_GUARD'],1_000_000_000)
        row=feedback.observe(['HOLD_GUARD'],1_650_000_000,complete=True)
        self.assertTrue(row['complete']);self.assertEqual(row['frames'],2)
        self.assertEqual(row['elapsed_seconds'],.65)

    def test_diagnostics_do_not_modify_observed_arm_values(self):
        arms=self.arms();old=arms['LEFT']['position'].copy()
        calibration_reasons(arms,[],True)
        np.testing.assert_array_equal(arms['LEFT']['position'],old)
        self.assertEqual(arms['LEFT']['speed_xy'],.1)


if __name__=='__main__':unittest.main()
