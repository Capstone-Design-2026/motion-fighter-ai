"""Two actual Mac games, synthetic camera inputs, UDP impairment; never opens a webcam."""
from pathlib import Path
import os,subprocess,time,json,uuid,collections,threading,socket,random,heapq,struct,sys
from redbool_demo.transport import DemoSender
from action_engine.types import ActionEvent
W=Path(os.environ['REDBOOL_CHECK_OUTPUT']).resolve();W.mkdir(parents=True,exist_ok=True)
R=Path(os.environ['REDBOOL_RELEASE']).resolve();PY=sys.executable
# Only the display channel is impaired; camera commands retain the existing direct path.
class Proxy(threading.Thread):
 def __init__(self):
  super().__init__(daemon=True);self.sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);self.sock.bind(('127.0.0.1',15067));self.sock.settimeout(.003);self.peer=None;self.stop=False;self.mode='normal';self.queue=[];self.rng=random.Random(28);self.dropped=0;self.counter=0
 def run(self):
  while not self.stop:
   now=time.monotonic()
   while self.queue and self.queue[0][0]<=now:
    _,_,data,to=heapq.heappop(self.queue);self.sock.sendto(data,to)
   try:data,addr=self.sock.recvfrom(65535)
   except socket.timeout:continue
   to=self.peer if addr[1]==5067 else ('127.0.0.1',5067)
   if addr[1]!=5067:self.peer=addr
   if not to:continue
   if self.mode=='cut' or self.mode=='impaired' and self.rng.random()<.05:self.dropped+=1;continue
   delay=0 if self.mode=='normal' else .012+self.rng.random()*.025
   self.counter+=1;heapq.heappush(self.queue,(now+delay,self.counter,data,to))
   if self.mode=='impaired' and self.rng.random()<.03:
    self.counter+=1;heapq.heappush(self.queue,(now+delay+.05,self.counter,data,to))
  self.sock.close()
for port in (5065,5066,5067,15067):
 with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as probe:probe.bind(('0.0.0.0',port))
proxy=Proxy();proxy.start();apps=[];clients=[DemoSender('127.0.0.1',5065+i,uuid.uuid4().hex,'P'+str(i+1)) for i in range(2)]
last=[None,None];sent=[0.,0.];feedback=[{},{}];actions=collections.Counter();rtts=[[],[]];start=time.monotonic();fight_start=None;load=None;load_log=None;mode_events=[]
try:
 for role,extra in [('host',['--redbool-pvp','--redbool-require-view']),('guest',['--redbool-pvp-client','127.0.0.1','--redbool-view-port','15067'])]:
  env={**os.environ,'REDBOOL_VIEW_TRACE_FILE':str(W/(role+'.jsonl')),'REDBOOL_VIEW_CAPTURE_DIR':str(W/'screens')}
  apps.append(subprocess.Popen([str(R/('REDBOOL.exe' if os.name=='nt' else 'REDBOOL.app/Contents/MacOS/Vision Striker')),*extra,'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',str(W/(role+'.log'))],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL))
  time.sleep(1)
 while time.monotonic()-start<120:
  t=time.monotonic();now=time.monotonic_ns()
  assert all(p.poll() is None for p in apps),'Game exited early'
  elapsed=t-fight_start if fight_start else 0
  desired='cut' if 24<elapsed<25.2 else 'impaired' if 12<elapsed<24 or 25.2<elapsed<32 else 'normal'
  if desired!=proxy.mode:proxy.mode=desired;mode_events.append({'elapsed':elapsed,'mode':desired})
  for i,s in enumerate(clients):
   for row in s.service():
    if row['type']=='ack':
     a=row['ack'];feedback[i]=a
     if fight_start and a.get('event_id'):actions[(i,a.get('status'),a.get('applied_action'))]+=1
     if fight_start and a.get('sender_ns'):rtts[i].append((row['ack_receive_ns']-a['sender_ns'])/1e6)
   context=s.tutorial_context() or {};target=context.get('action','');phase=context.get('phase','');block=False;movement='NEUTRAL';gesture=None
   if phase=='PRACTICE':
    if last[i]!=target:last[i]=target;sent[i]=t
    if target=='BLOCK':block=(t-sent[i])%1>.5
    elif target.startswith('MOVE_'):movement=target.removeprefix('MOVE_') if (t-sent[i])%1>.5 else 'NEUTRAL'
    elif t-sent[i]>.8:gesture={'JAB':'LEFT_PUNCH','STRAIGHT':'RIGHT_PUNCH','HIGH_KICK':'LEFT_RAISE','LOW_KICK':'RIGHT_RAISE','SPECIAL':'SPECIAL'}.get(target);sent[i]=t
   elif fight_start:
    if elapsed<2:movement='RIGHT' if i==0 else 'LEFT'
    elif feedback[i].get('game_can_act') and t-sent[i]>1.4:
     gesture='SPECIAL' if feedback[i].get('special_ready') else ('LEFT_PUNCH' if int(elapsed)%2==0 else 'RIGHT_PUNCH');sent[i]=t
    if 10<elapsed<11:block=True
   if s.clock:s.send('STATE',ttl_ms=150,calibration_version=1,calibration_generation=1,calibrated=True,tracking='VALID',fsm_state='READY',block_active=block,movement=movement,movement_strength=.75 if movement!='NEUTRAL' else 0.)
   if gesture:
    side='BOTH' if gesture=='SPECIAL' else 'LEFT' if gesture.startswith('LEFT') else 'RIGHT'
    s.offer(ActionEvent(uuid.uuid4().hex,s.actor_id,s.stream_id,1,now,now,now+150_000_000,'ATTACK',side,{'gesture':gesture},'synthetic-dual-check'))
  if not fight_start and all(f.get('game_can_act') and f.get('tutorial_phase')=='COMPLETE' for f in feedback):
   fight_start=t;load_log=(W/'native_rgb_load.log').open('w');load=subprocess.Popen([PY,str(Path(__file__).with_name('native_rgb_load.py'))],stdout=load_log,stderr=subprocess.STDOUT)
  if fight_start and elapsed>=43:break
  time.sleep(.015)
 assert fight_start,'Both participants did not reach combat'
 if load:assert load.wait(timeout=10)==0
finally:
 for s in clients:s.close()
 proxy.stop=True;proxy.join(timeout=2)
 for p in apps:
  if p.poll() is None:p.terminate()
  try:p.wait(timeout=10)
  except subprocess.TimeoutExpired:p.kill();p.wait()
 if load and load.poll() is None:load.terminate();load.wait(timeout=10)
 if load_log:load_log.close()
import numpy as np
def read_rows(path):
 lines=path.read_text().splitlines();rows=[]
 for i,line in enumerate(lines):
  try:rows.append(json.loads(line))
  except json.JSONDecodeError:
   assert i==len(lines)-1,'Only an interrupted final diagnostic row may be incomplete'
 return rows
host={row['seq']:row for row in read_rows(W/'host.jsonl')}
guest=read_rows(W/'guest.jsonl')
assert guest,'No guest frames'
errors=[];posmax=rotmax=0
for g in guest:
 h=host[g['seq']]
 for key in ['round','winner','remaining','paused','resolved','fighting']:assert g['world'][key]==h['world'][key],key
 for x,y in zip(h['actors'],g['actors']):
  for key in ['hp','maxHp','charge','step','hits','attackSerial','phase','live']:assert x[key]==y[key],key
  for key in ['root','visual','hips','scale']:
   posmax=max(posmax,max(abs(x[key][c]-y[key][c]) for c in 'xyz'))
  for qa,qb in zip(x['bones'],y['bones']):rotmax=max(rotmax,max(abs(qa[c]-qb[c]) for c in 'xyzw'))
assert posmax<1e-5 and rotmax<.0001,(posmax,rotmax)
assert all(int(b['seq'])>int(a['seq']) for a,b in zip(guest,guest[1:])), 'state rewound'
report={'scope':'two native builds on '+sys.platform+' + saved RGB inference + synthetic direct commands; not physical LAN/camera latency', 'frames_compared':len(guest),'position_max_error':posmax,'quaternion_component_max_error':rotmax,'mode_events':mode_events,'dropped_datagrams':proxy.dropped,'applied':{str(k):v for k,v in actions.items()},'ack_ms_p50_p95':[np.percentile(v,[50,95]).tolist() if v else [] for v in rtts],'rgb':json.loads((W/'native_rgb_load.json').read_text()),'perf':{role:[l for l in (W/(role+'.log')).read_text(errors='replace').splitlines() if '[REDBOOL PERF]' in l or '[REDBOOL VIEW METRIC]' in l] for role in ('host','guest')}}
(W/'native_dual_result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False,indent=2))
