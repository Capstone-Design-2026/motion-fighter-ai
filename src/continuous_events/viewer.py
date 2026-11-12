"""RGB/pose diagnostic viewer. Colors describe estimates, never actual occlusion."""
import argparse
import hashlib
import json
from pathlib import Path
import cv2
import numpy as np
from .quality import JOINTS, joint_states, load_frames

COLORS={'VALID':(30,210,30),'LOW_VISIBILITY':(0,220,255),'OUTSIDE':(220,30,220),
        'POSE_MISSING':(40,40,255),'INVALID_COORDINATE':(150,150,150),'NOT_PROCESSED':(150,150,150)}
BONES=((11,12),(11,13),(13,15),(12,14),(14,16))


def overlay(bgr, frame, *, name='stored', mirror=False, unprocessed=False):
    # Always copy: overlays must never contaminate the encoded source pixels.
    # Flip the copied preview before drawing so labels remain readable.
    out=cv2.flip(bgr,1) if mirror else bgr.copy()
    h,w=out.shape[:2];states=joint_states(frame);points=frame.get('image_landmarks')
    if unprocessed:
        states={j:['NOT_PROCESSED'] for j in JOINTS};points=None
    def xy(j):
        if not points or len(points)<=j or any(v is None or not np.isfinite(v) for v in points[j][:2]):return None
        x,y = (int(np.clip(v,0,1)*(size-1)) for v,size in zip(points[j][:2],(w,h)))
        return (w-1-x if mirror else x),y
    for a,b in BONES:
        pa,pb=xy(a),xy(b)
        if pa and pb:cv2.line(out,pa,pb,(170,170,170),1)
    for j,flags in states.items():
        pos=xy(j)
        if pos:
            cv2.circle(out,pos,6,COLORS[flags[0]],-1)
            cv2.putText(out,str(j),pos,cv2.FONT_HERSHEY_SIMPLEX,.4,(255,255,255),1)
    # Text is unmirrored and anatomical LEFT/RIGHT are never swapped.
    panel=np.zeros((170,w,3),dtype=np.uint8)
    cv2.putText(panel,f'{name} frame {frame["frame_id"]} capture_ns {frame["capture_return_monotonic_ns"]}',(5,18),0,.42,(255,255,255),1)
    for i,(j,label) in enumerate(JOINTS.items()):
        cv2.putText(panel,f'{j} {label}: {" + ".join(states[j])}',(5,38+i*18),0,.43,COLORS[states[j][0]],1)
    cv2.putText(panel,'ESTIMATE diagnostics only; OUTSIDE is clamped at image border',(5,155),0,.4,(255,255,255),1)
    return np.concatenate([out,panel],axis=0)


class Viewer:
    def __init__(self, session, lite=None, full=None):
        self.session=session;self.manifest=json.loads((session/'manifest.json').read_text())
        rows=[json.loads(s) for s in (session/'frames.jsonl').read_text().splitlines()]
        self.source={r['frame_id']:r for r in rows}
        self.mapping=[json.loads(s) for s in (session/'video_frames.jsonl').read_text().splitlines()]
        self.variants={'stored':self.source}
        for name,path in [('Lite',lite),('Full',full)]:
            if path:self.variants[name]={r['frame_id']:r for r in load_frames(path,self.source,self.manifest['session_id'])}
        self.cap=cv2.VideoCapture(str(session/self.manifest['video']['path']))
        if not self.cap.isOpened():raise ValueError('Video unavailable')

    def frame(self,index,name='stored',mirror=False):
        m=self.mapping[index];source=self.source[m['frame_id']]
        if m['capture_return_monotonic_ns']!=source['capture_return_monotonic_ns']:raise ValueError('Video mapping clock mismatch')
        self.cap.set(cv2.CAP_PROP_POS_FRAMES,m['video_frame_index']);ok,bgr=self.cap.read()
        if not ok or hashlib.sha256(cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).tobytes()).hexdigest()!=m['source_rgb_sha256']:
            raise ValueError('RGB SHA mismatch')
        row=self.variants[name].get(m['frame_id'])
        unprocessed=row is None
        if row is None:
            row={**source,'pose_success':False,'image_landmarks':None,'pose_validity':{'pose_present':False}}
            name+=' (NOT PROCESSED: not a pose detection failure)'
        return overlay(bgr,row,name=name,mirror=mirror,unprocessed=unprocessed)

    def close(self):self.cap.release()


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--session',type=Path,required=True)
    p.add_argument('--lite-frames',type=Path);p.add_argument('--full-frames',type=Path);p.add_argument('--mirror-preview',action='store_true')
    p.add_argument('--frame-id',type=int);p.add_argument('--export',type=Path);p.add_argument('--variant',choices=['stored','Lite','Full'],default='stored')
    a=p.parse_args();v=Viewer(a.session,a.lite_frames,a.full_frames)
    try:
        if a.export:
            if a.export.exists():raise ValueError('New export required')
            index=next(i for i,m in enumerate(v.mapping) if m['frame_id']==a.frame_id)
            a.export.parent.mkdir(parents=True,exist_ok=True)
            if not cv2.imwrite(str(a.export),v.frame(index,a.variant,a.mirror_preview)):raise ValueError('Image export failed')
            return
        title='RGB Pose diagnostic | 1 stored / 2 Lite / 3 Full | space play | a/d step | q exit'
        cv2.namedWindow(title);cv2.createTrackbar('video index',title,0,len(v.mapping)-1,lambda x:None)
        playing=False;name=a.variant
        while True:
            index=cv2.getTrackbarPos('video index',title)
            cv2.imshow(title,v.frame(index,name,a.mirror_preview))
            # Playback delay is derived from capture mapping, not container FPS.
            delay=max(1,min(2000,int((v.mapping[min(index+1,len(v.mapping)-1)]['capture_return_monotonic_ns']-v.mapping[index]['capture_return_monotonic_ns'])/1e6))) if playing else 30
            key=cv2.waitKey(delay)&255
            if key==ord('q'):break
            if key==ord(' '):playing=not playing
            if key in (ord('1'),ord('2'),ord('3')):
                candidate=['stored','Lite','Full'][key-ord('1')]
                if candidate in v.variants:name=candidate
            if key in (ord('a'),ord('d')):playing=False;index+=-1 if key==ord('a') else 1
            elif playing:index+=1
            if index>=len(v.mapping):playing=False
            cv2.setTrackbarPos('video index',title,max(0,min(index,len(v.mapping)-1)))
    finally:v.close();cv2.destroyAllWindows()

if __name__=='__main__':main()
