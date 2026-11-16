"""Display-only framing feedback; never relaxes the recognizer's joint gates."""
import math


def framing_status(obs, visibility=.5):
    points=obs.landmarks
    if not obs.pose_present or points is None or len(points)!=33:
        return {'code':'NO_POSE','message':'KEEP SHOULDERS, ELBOWS AND HANDS IN VIEW','joints':[]}
    ids=(13,14,15,16)
    outside=[];outside_edges=set();uncertain=[];edge=[]
    for j in ids:
        xy=points[j][:2]
        if len(xy)!=2 or not all(math.isfinite(x) for x in xy):
            uncertain.append(j);continue
        x,y=xy
        if x<0 or x>1 or y<0 or y>1:
            outside.append(j)
            if x<0:outside_edges.add('LEFT')
            if x>1:outside_edges.add('RIGHT')
            if y<0:outside_edges.add('TOP')
            if y>1:outside_edges.add('BOTTOM')
        elif x<.02 or x>.98 or y<.02 or y>.96:edge.append(j)
        if obs.visibility is None or len(obs.visibility)!=33 or not math.isfinite(obs.visibility[j]) or obs.visibility[j]<visibility:
            uncertain.append(j)
    if outside:
        message=('ARMS BELOW VIEW - LIFT HANDS / ADJUST CAMERA'
            if outside_edges=={'BOTTOM'} else 'ARMS OUT OF VIEW - MOVE BACK / ADJUST CAMERA')
        return {'code':'OUTSIDE','message':message,'joints':outside,
                'outside_edges':sorted(outside_edges)}
    if uncertain:return {'code':'UNCERTAIN','message':'KEEP BOTH ELBOWS AND HANDS VISIBLE','joints':uncertain}
    if edge:return {'code':'EDGE','message':'HANDS NEAR EDGE - LEAVE ROOM FOR LOW PUNCHES','joints':edge}
    return {'code':'OK','message':'','joints':[]}
