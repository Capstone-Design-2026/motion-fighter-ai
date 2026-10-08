using System.Collections.Generic;
using UnityEngine;
/// <summary>Presentation coordinates only: saved topology, combat and story identities never change.</summary>
public static class StoryMapLayout {
 public static readonly Vector2 Size=new Vector2(4200,2800);
 static readonly Dictionary<string,Vector2> locations=new Dictionary<string,Vector2> {
 {"cave",new Vector2(.075f,.17f)},{"scout",new Vector2(.145f,.37f)},{"inn",new Vector2(.29f,.505f)},
 {"river",new Vector2(.37f,.47f)},{"gate",new Vector2(.455f,.49f)},{"herb",new Vector2(.605f,.55f)},
 {"ridge",new Vector2(.20f,.64f)},{"watch",new Vector2(.27f,.74f)},{"ridge_rest",new Vector2(.37f,.84f)},{"ridge_end",new Vector2(.33f,.92f)},
 {"bridge",new Vector2(.24f,.435f)},{"old_ferry",new Vector2(.35f,.30f)},{"ferry_lookout",new Vector2(.45f,.20f)},
 {"quarry",new Vector2(.705f,.745f)},{"tunnel",new Vector2(.79f,.79f)},{"entrance",new Vector2(.835f,.87f)},{"shelter",new Vector2(.90f,.78f)},{"final",new Vector2(.94f,.90f)},
 {"east",new Vector2(.73f,.555f)},{"pass",new Vector2(.835f,.54f)},{"rescue",new Vector2(.925f,.47f)},{"supply",new Vector2(.915f,.36f)},
 {"camp",new Vector2(.69f,.32f)},{"camp_record",new Vector2(.745f,.235f)},{"camp_shelter",new Vector2(.82f,.16f)}
 };
 public static bool TryPosition(string id,out Vector2 position){if(locations.TryGetValue(id,out var uv)){position=new Vector2((uv.x-.5f)*Size.x,uv.y*Size.y);return true;}position=Vector2.zero;return false;}
 public static Vector2 Point(Vector2 from,Vector2 to,float t) {
     // Stable reversal ensures walking back uses exactly the same visible route.
     if(from.x>to.x || (Mathf.Approximately(from.x,to.x)&&from.y>to.y))return Point(to,from,1-t);
     TryPosition("scout",out var scout);TryPosition("inn",out var inn);
     if(Vector2.Distance(from,scout)<.1f && Vector2.Distance(to,inn)<.1f) {
         var control=new Vector2((.15f-.5f)*Size.x,.59f*Size.y);
         return (1-t)*(1-t)*from+2*(1-t)*t*control+t*t*to;
     }
     Vector2 delta=to-from;Vector2 normal=new Vector2(-delta.y,delta.x).normalized;
     return Vector2.Lerp(from,to,t)+normal*Mathf.Sin(t*Mathf.PI*2)*Mathf.Min(55,delta.magnitude*.095f);
 }
}
