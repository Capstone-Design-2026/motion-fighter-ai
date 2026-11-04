import unittest
from redbool_demo.probe import guard_check

def packet(count,active=False):
    return {'type':'ack','ack':{'guards_started':count,'game_guard_active':active}}

class GuardProbeTests(unittest.TestCase):
    def test_one_guard_and_finish_pass(self):
        self.assertTrue(guard_check(4,[packet(4),packet(5,True),packet(5)])['passed'])

    def test_held_pose_repeated_guard_fails(self):
        self.assertFalse(guard_check(4,[packet(5,True),packet(6)])['passed'])

    def test_missing_old_receiver_ack_or_still_guarding_fails(self):
        for base,packets in [(None,[]),(0,[packet(0)]),(0,[packet(1,True)])]:
            with self.subTest(base=base,packets=packets):
                self.assertFalse(guard_check(base,packets)['passed'])
