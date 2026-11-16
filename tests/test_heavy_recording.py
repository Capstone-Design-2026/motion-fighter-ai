from dataclasses import asdict
import copy
import json
import unittest
from test_chest_special import pose
from redbool_demo.backend_assets import POSE_SHA256
from redbool_demo.heavy_auxiliary import FrameKey
from redbool_demo.landmark_recording import _encode
from redbool_demo.landmark_replay import observation


class HeavyRecordingTests(unittest.TestCase):
    def row(self,video=True,image=True,heavy=None):
        p=pose('cross',1,1_000_000_000)
        model=dict(landmarks=p.landmarks,visibility=p.visibility,presence=[1.]*33)
        models=dict(cross_pose=model if video else None,cross_recovery=model if image else None)
        if heavy is not False:models['cross_refine']=heavy
        return json.loads(_encode(dict(kind='process',has_auxiliary=video or image,clocks=[p.capture_ns+30_000_000],
            observation=p,frame=p.as_frame(),raw_models=models))),p

    def payload(self):
        p=pose('cross',1,1_000_000_000)
        return dict(variant='Heavy',mode='IMAGE',sha256=POSE_SHA256['Heavy'],
            start_ns=p.capture_ns+1_000_000,end_ns=p.capture_ns+20_000_000,
            source=asdict(FrameKey(p.actor_id,p.capture_epoch,p.frame_id,p.capture_ns,p.width,p.height,p.source_mirrored,3)),
            worker_generation=2,landmarks=p.landmarks,visibility=p.visibility,presence=[1.]*33,world=[[0.,0.,0.]]*33)

    def test_absent_or_skipped_heavy_preserves_none_single_and_two_slot_shape(self):
        for video,image in ((False,False),(True,False),(False,True),(True,True)):
            with self.subTest(video=video,image=image):
                old,_=self.row(video,image,False);new,_=self.row(video,image,None)
                self.assertNotIn('heavy_image',old['frame'])
                self.assertIn('heavy_image',new['frame'])
                self.assertEqual(observation(old),observation(new))
        row,_=self.row(False,False,None)
        self.assertIsNone(observation(row)[1])

    def test_actual_fresh_heavy_is_third_slot_and_raw_provenance_is_lossless(self):
        payload=self.payload();row,p=self.row(heavy=payload)
        self.assertEqual(row['frame']['heavy_image']['source'],payload['source'])
        self.assertEqual(row['frame']['heavy_image']['world'],payload['world'])
        primary,aux=observation(row)
        self.assertEqual(primary,p);self.assertEqual(aux,(p,p,p))

    def test_wrong_frame_identity_dimensions_mirror_and_model_are_rejected(self):
        for field,value in [('actor_id','other'),('capture_epoch','other'),('frame_id',2),('capture_ns',1),
                            ('width',320),('height',240),('source_mirrored',True)]:
            with self.subTest(field=field):
                data=self.payload();data['source'][field]=value;row,_=self.row(heavy=data)
                with self.assertRaises(ValueError):observation(row)
        data=self.payload();data['sha256']='unknown';row,_=self.row(heavy=data)
        with self.assertRaises(ValueError):observation(row)

    def test_late_or_after_processing_result_is_rejected(self):
        for finish in (1_121_000_000,1_031_000_000):
            data=self.payload();data['end_ns']=finish;row,_=self.row(heavy=data)
            with self.subTest(finish=finish),self.assertRaises(ValueError):observation(row)

    def test_empty_fresh_detection_is_not_replaced_with_a_previous_skeleton(self):
        data=self.payload();data['landmarks']=[];data['visibility']=[]
        row,_=self.row(heavy=data);aux=observation(row)[1][2]
        self.assertFalse(aux.pose_present);self.assertIsNone(aux.landmarks)

    def test_previous_calibration_generation_cannot_masquerade_as_current(self):
        row,_=self.row(heavy=self.payload());row['expected']={'calibration_generation':4}
        with self.assertRaisesRegex(ValueError,'generation'):observation(row)


if __name__=='__main__':unittest.main()
