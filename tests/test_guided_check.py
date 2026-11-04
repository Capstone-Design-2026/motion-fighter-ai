import unittest
from redbool_demo.guided import GuidedCheck

class Recorder:
    def __init__(self): self.rows=[]
    def record_annotation(self,**row): self.rows.append(row)

class GuidedCheckTests(unittest.TestCase):
    def test_movement_check_records_each_direction_and_center_returns(self):
        g=GuidedCheck(movement=True);r=Recorder()
        for i in range(1600):
            g.update(i+1,i*100_000_000,True,r)
            if g.completed:break
        self.assertTrue(g.completed);self.assertEqual(len(r.rows),45)
        for direction in ('MOVE_LEFT','MOVE_RIGHT'):
            self.assertEqual(sum(a['label']==direction for a in r.rows),3)
        for i,a in enumerate(r.rows):
            if a['label'].startswith('MOVE_'):
                self.assertEqual(r.rows[i+1]['label'],'GUARD')
        self.assertTrue(all(a['notes']['protocol']=='raised-kicks-movement-check-v4' for a in r.rows))

    def test_rest_and_guard_prompts_are_distinct(self):
        g=GuidedCheck();r=Recorder()
        self.assertIn('fists by chin',g.update(1,0,True,r))
        g.index=len(g.steps)-2
        self.assertIn('hands relaxed, body centered',g.update(2,100_000_000,True,r))

    def test_no_prompt_intervals_until_calibrated_and_all_marked_unreviewed(self):
        g=GuidedCheck();r=Recorder()
        for i in range(30):g.update(i+1,i*100_000_000,False,r)
        self.assertFalse(r.rows)
        for i in range(30,1400):
            g.update(i+1,i*100_000_000,True,r)
            if g.completed:break
        self.assertTrue(g.completed)
        self.assertEqual(len(r.rows),33)
        self.assertTrue(all(x['source']=='prompt' and x['notes']['ground_truth_status']=='NOT_REVIEWED' for x in r.rows))
        self.assertTrue(all(a['end_frame_id']<b['start_frame_id'] for a,b in zip(r.rows,r.rows[1:])))
        self.assertEqual(sum(x['label']=='RIGHT_RAISE' for x in r.rows),3)
        self.assertFalse(any(x['label']=='LEFT_LOWER' for x in r.rows))

    def test_pause_closes_partial_interval_without_duplicate_annotation(self):
        g=GuidedCheck();r=Recorder()
        g.update(1,0,True,r);g.update(2,33_333_333,False,r);g.finish(r)
        self.assertTrue(g.interrupted);self.assertEqual(len(r.rows),1)
        self.assertEqual(r.rows[0]['notes']['outcome'],'interrupted')
