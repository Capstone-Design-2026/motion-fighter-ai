"""Display-only framing feedback; never relaxes the recognizer's joint gates."""
import math


def framing_status(obs, visibility=.5):
    points=obs.landmarks
    if not obs.pose_present or points is None or len(points)!=33:
        return {'code':'NO_POSE','message':'KEEP SHOULDERS, ELBOWS AND HANDS IN VIEW','joints':[]}
    ids=(13,14,15,16)
    outside=[];uncertain=[];edge=[]
    for j in ids:
        xy=points[j][:2]
        if len(xy)!=2 or not all(math.isfinite(x) for x in xy):
            uncertain.append(j);continue
        x,y=xy
        if x<0 or x>1 or y<0 or y>1:outside.append(j)
        elif x<.02 or x>.98 or y<.02 or y>.96:edge.append(j)
        if obs.visibility is None or len(obs.visibility)!=33 or not math.isfinite(obs.visibility[j]) or obs.visibility[j]<visibility:
            uncertain.append(j)
    if outside:return {'code':'OUTSIDE','message':'ARMS OUT OF VIEW - MOVE BACK / ADJUST CAMERA','joints':outside}
    if uncertain:return {'code':'UNCERTAIN','message':'KEEP BOTH ELBOWS AND HANDS VISIBLE','joints':uncertain}
    if edge:return {'code':'EDGE','message':'HANDS NEAR EDGE - LEAVE ROOM FOR LOW PUNCHES','joints':edge}
    return {'code':'OK','message':'','joints':[]}
