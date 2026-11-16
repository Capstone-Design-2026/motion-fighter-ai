"""Stress the new speed bound on reviewed multi-person captures, not accuracy."""
from dataclasses import replace
from unittest.mock import patch
from redbool_demo.personal import PersonalGestureEngine
import test_cross_recovery_recordings as reviewed


class MinimumPersonalSpeed(PersonalGestureEngine):
    def _components(self):
        super()._components()
        for unit in self.attack.units.values():
            unit.cfg = replace(unit.cfg, onset_speed=.65)


class PersonalFieldSafetyTests(reviewed.CrossRecoveryRecordingTests):
    @classmethod
    def setUpClass(cls):
        with patch.object(reviewed, 'SixGestureEngine', MinimumPersonalSpeed):
            super().setUpClass()
