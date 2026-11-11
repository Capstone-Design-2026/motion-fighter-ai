"""Reject an auxiliary skeleton which belongs to a different visible body."""
import numpy as np
from action_engine.experimental import joint_validity

def same_body(primary, auxiliary, config):
    if joint_validity(primary, config)['MOVEMENT'] or joint_validity(auxiliary, config)['MOVEMENT']:
        return False
    a=np.asarray(primary.as_frame()['image_landmarks'],float)[[11,12],:2].copy()
    b=np.asarray(auxiliary.as_frame()['image_landmarks'],float)[[11,12],:2].copy()
    a[:,1]*=primary.height/primary.width;b[:,1]*=auxiliary.height/auxiliary.width
    sa=np.linalg.norm(a[0]-a[1]);sb=np.linalg.norm(b[0]-b[1])
    return bool(.5<=sb/sa<=2. and np.linalg.norm(a.mean(axis=0)-b.mean(axis=0))<=.5*sa)
