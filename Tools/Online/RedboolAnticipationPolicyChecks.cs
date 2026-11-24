using System;
// Only the protocol data fields read by the production authority gate. Native
// Editor checks repeat these cases with the actual RedboolView protocol types.
public sealed class RedboolViewActor {public int hp,phase,attackSerial;public bool live;public byte character,specialFlags;}
public sealed class RedboolViewWorld {public int round;public bool fighting,paused,resolved;public Guid selectionRun;}
public sealed class RedboolViewFrame {
 public RedboolViewWorld world;public readonly RedboolViewActor[] actors=new RedboolViewActor[2];
 public bool Complete=>world!=null&&actors[0]!=null&&actors[1]!=null;
}
class Check {
 static int checks;
 static void Assert(bool value,string label) {checks++;if(!value)throw new Exception(label);}
 static void Main() {
  var p=new RedboolAnticipationWindow();
  Assert(RedboolAnticipationWindow.StateFor("LEFT_PUNCH")=="JAB","jab mapping");
  Assert(RedboolAnticipationWindow.StateFor("RIGHT_PUNCH")=="CROSS","cross mapping");
  Assert(RedboolAnticipationWindow.StateFor("LEFT_RAISE")=="HIGHKICK","high mapping");
  Assert(RedboolAnticipationWindow.StateFor("RIGHT_RAISE")=="LOWKICK","low mapping");
  Assert(!p.Begin("special","SPECIAL",0,1)&&!p.Active,"special never predicts");
  Assert(!p.Begin("","LEFT_PUNCH",0,1)&&!p.Begin(new string('x',201),"LEFT_PUNCH",0,1),"bounded IDs");
  Assert(p.Begin("a","LEFT_PUNCH",0,1),"starts");
  Assert(!p.Begin("a","LEFT_PUNCH",.01,1)&&p.Duplicates==1,"dedup");
  Assert(!p.Begin("b","RIGHT_PUNCH",.01,1),"no action queue");
  p.Result("a","APPLIED",.02);
  Assert(p.Weight(.1,1)==1&&!p.Returning,"accepted waits for serial");
  Assert(p.Weight(.11,2)==1&&p.Returning&&p.Handoffs==1,"new authoritative serial hands back");
  Assert(p.Weight(.171,2)==0&&!p.Active,"handoff terminates");
  Assert(!p.Begin("b","RIGHT_PUNCH",.2,2),"discarded queued ID cannot replay");
  Assert(p.Begin("c","RIGHT_PUNCH",1,2),"next event");
  Assert(p.Weight(1.15,2)>.99,"advance bounded edge");
  Assert(p.Weight(1.211,2)==0&&!p.Active&&p.Timeouts==1,"timeout fade bounded");
  Assert(!p.Begin("c","RIGHT_PUNCH",2,2),"timed out ID cannot replay");
  Assert(p.Begin("d","LEFT_RAISE",2,2),"reject setup");
  p.Result("d","GAME_BUSY",2.02);
  Assert(p.Returning&&p.Rejected==1&&p.Weight(2.03,2)>0,"reject begins quick fade");
  Assert(p.Weight(2.056,2)==0&&!p.Active,"reject ends in 35ms");
  p.Result("d","APPLIED",2.1);Assert(!p.Active,"late success cannot revive");
  Assert(p.Begin("e","RIGHT_RAISE",3,2),"late frame setup");
  Assert(p.Weight(3.3,2)==0&&!p.Active,"late rendering cannot replay expired motion");
  Assert(p.Begin("f","LEFT_PUNCH",4,2),"cancel setup");p.Cancel();
  Assert(!p.Begin("f","LEFT_PUNCH",4.1,2),"cancel preserves dedup");
  var bounded=new RedboolAnticipationWindow();
  for(int i=0;i<RedboolAnticipationWindow.MaxRememberedEvents;i++) {Assert(bounded.Begin("id"+i,"LEFT_PUNCH",i,1),"dedup capacity entry");bounded.Cancel();}
  Assert(bounded.DedupCapacityReached&&!bounded.Begin("overflow","LEFT_PUNCH",9999,1),"capacity fails closed");
  CheckNewestAuthority();
  Console.WriteLine("PASS "+checks+" anticipation policy checks (no Unity, camera or network)");
 }
 static void CheckNewestAuthority() {
  var shown=new RedboolViewActor {hp=100,live=true,character=1};
  var world=new RedboolViewWorld {round=2,fighting=true};
  var newest=new RedboolViewFrame {world=new RedboolViewWorld {round=2,fighting=true}};
  newest.actors[0]=new RedboolViewActor();newest.actors[1]=new RedboolViewActor {hp=100,live=true,character=1};
  Func<bool> allowed=()=>RedboolAnticipationAuthority.Allows(true,shown,world,newest);
  Assert(allowed(),"fresh same-round authority allows presentation");
  Assert(!RedboolAnticipationAuthority.Allows(false,shown,world,newest),"stale display cancels");
  Assert(!RedboolAnticipationAuthority.Allows(true,shown,world,null),"missing authority cancels");
  newest.world.paused=true;Assert(!allowed(),"latest pause cancels while display still fighting");newest.world.paused=false;
  newest.world.resolved=true;Assert(!allowed(),"latest resolution cancels older display");newest.world.resolved=false;
  newest.world.fighting=false;Assert(!allowed(),"latest stopped fight cancels");newest.world.fighting=true;
  newest.world.round++;Assert(!allowed(),"latest round cancels older round");newest.world.round--;
  newest.world.selectionRun=Guid.NewGuid();Assert(!allowed(),"latest selection run cancels older run");newest.world.selectionRun=Guid.Empty;
  newest.actors[1].character++;Assert(!allowed(),"latest avatar cancels older rig");newest.actors[1].character--;
  newest.actors[1].hp=99;Assert(!allowed(),"new damage cancels before interpolation reaches it");newest.actors[1].hp=100;
  newest.actors[1].hp=0;Assert(!allowed(),"new death cancels");newest.actors[1].hp=100;
  newest.actors[1].live=false;Assert(!allowed(),"new tracking loss cancels");newest.actors[1].live=true;
  newest.actors[1].phase=1;Assert(!allowed(),"new special phase cancels");newest.actors[1].phase=0;
  newest.actors[1].specialFlags=1;Assert(!allowed(),"new enemy special cancels");newest.actors[1].specialFlags=0;
  newest.actors[1].attackSerial++;Assert(allowed(),"attack serial change is handled by bounded handoff policy");
  shown.live=false;Assert(!allowed(),"invalid displayed actor also cancels");shown.live=true;
  var saved=newest.actors[1];newest.actors[1]=null;Assert(!allowed(),"incomplete newest snapshot cancels");newest.actors[1]=saved;
  Assert(allowed()&&shown.hp==100&&world.round==2&&!world.paused,"authority gate does not mutate displayed state");
 }
}
