using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Deterministic dressing for the scout checkpoint. Never edits character assets.</summary>
public static class ScoutStageDressing
{
 const string Root="Assets/Environments/ScoutPass";
 static Transform detail;
 static Material wood,iron;
 public static void BuildAndReview(){Build();ScoutPassVerification.Run();}
 [MenuItem("Tools/VisionStriker/Dress Scout Checkpoint")]
 public static void Build()
 {
  if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the current scene first.");
  AssetDatabase.Refresh();var scene=EditorSceneManager.OpenScene(ScoutPassBuilder.ScenePath);
  ApplyToScene();EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
 }
 public static void ApplyToScene()
 {
  var environment=GameObject.Find("Scout Pass · 청계역 감시 길목");if(!environment)throw new InvalidOperationException("Scout arena required");
  var previous=environment.transform.Find("Checkpoint Life");if(previous)Object.DestroyImmediate(previous.gameObject);
  detail=new GameObject("Checkpoint Life").transform;detail.SetParent(environment.transform,false);
  wood=Mat("SupplyWeatheredWood",new Color(.32f,.24f,.15f));iron=Mat("SupplyDarkIron",new Color(.12f,.13f,.12f));
  wood.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/PathGround.png"));wood.SetTextureScale("_BaseMap",new Vector2(.6f,2));
  // Make the checkpoint the visual destination, with provisions clustered beside it.
  var gate=environment.transform.Find("ConvoyCheckpoint");if(gate)PlaceExisting(gate,1.45f,19.5f);
  var cart=environment.transform.Find("ConvoySupplies");if(cart){cart.localScale=Vector3.one*.72f;cart.rotation=Quaternion.Euler(0,62,0);PlaceExisting(cart,7.1f,10.5f);}
  foreach(var flag in environment.GetComponentsInChildren<ScoutPassWind>())
  {
   var t=flag.transform.parent;
   if(t.name=="CultSignalFlag")PlaceExisting(t,t.position.x<0?-2.2f:5.1f,t.position.x<0?19f:18.2f);
  }
  Prop("Rice_Chest",new Vector3(6.8f,0,8.3f),1.05f,-12);
  Prop("Straw_Bag",new Vector3(8.0f,0,9.2f),.65f,19);
  Prop("Straw_Bag",new Vector3(8.6f,0,9.8f),.55f,-16);
  Prop("Jar01a",new Vector3(6.1f,0,9.5f),.72f,20);
  Prop("Water_Jar",new Vector3(9.4f,0,11.2f),.8f,0);
  Prop("Low_Wooden_Bench",new Vector3(-6.8f,0,9.1f),.6f,-20);
  Crate(new Vector3(-7.6f,0,9.9f),new Vector3(.85f,.65f,.75f),-12);
  Crate(new Vector3(-8.4f,0,10.4f),new Vector3(.75f,.55f,.65f),17);
  Crate(new Vector3(-7.6f,.65f,9.9f),new Vector3(.65f,.5f,.62f),4);
  for(int i=0;i<4;i++)
  {
   var go=Prefab(Root+"/Prefabs/TimberFence.prefab","Checkpoint fence",new Vector3(i<2?-8.8f-i*2.6f:9.8f+(i-2)*2.6f,0,12.5f+i*.7f),.85f,i<2?20:-15);
   Ground(go.transform);
  }
  Fire(new Vector3(-5.6f,0,7.5f));
  // Near banks frame the fight but stay low enough for the full orbit camera.
  var rng=new System.Random(10172026);
  for(int i=0;i<46;i++)
  {
   float x=(i%2==0?-1:1)*(7.5f+(float)rng.NextDouble()*5.5f),z=-5+(float)rng.NextDouble()*15;
   float scale=.07f+(float)rng.NextDouble()*.12f;
   var rock=Prefab(Root+"/Prefabs/LowBoulder.prefab","Path bank stone",new Vector3(x,0,z),scale,(float)rng.NextDouble()*360);Ground(rock.transform);
  }
  for(int i=0;i<6;i++)
  {
   float x=i<3?-11.5f-i*.8f:10.2f+(i-3)*.9f,z=4.8f+(i%3)*3.1f;
   var shrub=Prefab(Root+"/Prefabs/BentPine.prefab","Bank pine scrub",new Vector3(x,0,z),.13f+(i%3)*.025f,i*69);Ground(shrub.transform);
  }
  ScoutCheckpointExpansion.Apply(detail);
  foreach(var r in detail.GetComponentsInChildren<Renderer>())
  {if(r is ParticleSystemRenderer)continue;r.receiveShadows=true;}
  AddHulls(detail);
  if(cart)AddHulls(cart);
  var sun=RenderSettings.sun;if(!sun)sun=Object.FindObjectsByType<Light>().First(l=>l.type==LightType.Directional);
  sun.transform.rotation=Quaternion.Euler(28,-38,0);sun.color=new Color(1,.90f,.76f);sun.intensity=1.45f;sun.shadowStrength=.84f;
  RenderSettings.ambientMode=AmbientMode.Trilight;
  RenderSettings.ambientSkyColor=new Color(.46f,.57f,.68f);RenderSettings.ambientEquatorColor=new Color(.34f,.38f,.38f);RenderSettings.ambientGroundColor=new Color(.22f,.19f,.15f);
  RenderSettings.fogStartDistance=35;RenderSettings.fogEndDistance=240;
  StageAtmosphereInstaller.ApplyCurrent("scout",environment.transform);
  AssetDatabase.SaveAssets();
 }
 static float Height(float x,float z)
 {float rise=Mathf.SmoothStep(0,1,(new Vector2(x,z).magnitude-18)/16f);return -.015f+rise*(.5f+Mathf.PerlinNoise(x*.055f+28,z*.055f+6)*2.7f);}
 static Bounds Bounds(Transform t)
 {var rs=t.GetComponentsInChildren<Renderer>().Where(r=>!(r is ParticleSystemRenderer)).ToArray();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
 static void PlaceExisting(Transform t,float x,float z)
 {var b=Bounds(t);t.position+=new Vector3(x-b.center.x,Height(x,z)-b.min.y,z-b.center.z);}
 static void Ground(Transform t)
 {var b=Bounds(t);t.position+=Vector3.up*(Height(b.center.x,b.center.z)-b.min.y-.015f);}
 static GameObject Prefab(string path,string name,Vector3 pos,float scale,float yaw)
 {
  var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!asset)throw new FileNotFoundException(path);
  var g=(GameObject)PrefabUtility.InstantiatePrefab(asset,detail);g.name=name;g.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,yaw,0));g.transform.localScale=Vector3.one*scale;
  foreach(var c in g.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
  foreach(var c in g.GetComponentsInChildren<Collider2D>())Object.DestroyImmediate(c);
  return g;
 }
 static void Prop(string name,Vector3 position,float maxSize,float yaw)
 {
  var g=Prefab("Assets/Naganeupseong/Prefabs/Prop/"+name+".prefab","Supply · "+name,position,1,yaw);
  var size=Bounds(g.transform).size;g.transform.localScale*=maxSize/Mathf.Max(size.x,size.y,size.z);Ground(g.transform);
 }
 static Material Mat(string name,Color color,string shader="Universal Render Pipeline/Lit")
 {
  string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(!m){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}m.SetColor("_BaseColor",color);m.enableInstancing=true;if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.12f);EditorUtility.SetDirty(m);return m;
 }
 static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 size,Material mat)
 {
  var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(g.GetComponent<Collider>());return g;
 }
 static void Crate(Vector3 p,Vector3 s,float angle)
 {
  var c=new GameObject("Checkpoint supply crate").transform;c.SetParent(detail,false);c.position=p+Vector3.up*Height(p.x,p.z);c.rotation=Quaternion.Euler(0,angle,0);
  Box(c,"Wooden body",new Vector3(0,s.y*.5f,0),s,wood);
  foreach(float x in new[]{-.34f,.34f})
  {Box(c,"Iron hoop front",new Vector3(s.x*x,s.y*.5f,-s.z*.505f),new Vector3(.035f,s.y,.02f),iron);Box(c,"Iron hoop top",new Vector3(s.x*x,s.y,0),new Vector3(.035f,.022f,s.z),iron);}
  for(int i=1;i<4;i++)Box(c,"Plank seam",new Vector3(0,s.y*i/4,-s.z*.506f),new Vector3(s.x,.008f,.005f),iron);
 }
 static void Fire(Vector3 p)
 {
  Prop("Brazer",p,.65f,0);
  var smoke=Mat("CheckpointSmoke",new Color(.53f,.56f,.58f,.32f),"VisionStriker/Camp Smoke");
  var ember=Mat("CheckpointEmber",new Color(1,.44f,.08f,.75f),"VisionStriker/Camp Smoke");
  Particles("Brazier smoke",p+Vector3.up*.35f,smoke,false);
  Particles("Brazier embers",p+Vector3.up*.3f,ember,true);
  var light=new GameObject("Brazier warm light").AddComponent<Light>();light.transform.SetParent(detail,false);light.transform.position=p+Vector3.up*.65f;
  light.type=LightType.Point;light.range=3;light.intensity=1.2f;light.color=new Color(1,.42f,.10f);light.shadows=LightShadows.None;
 }
 static void Particles(string name,Vector3 p,Material mat,bool embers)
 {
  var go=new GameObject(name,typeof(ParticleSystem));go.transform.SetParent(detail,false);go.transform.position=p;
  var ps=go.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=embers?42u:68u;
  var m=ps.main;m.loop=true;m.prewarm=true;m.duration=12;m.startLifetime=new ParticleSystem.MinMaxCurve(embers?.7f:3.5f,embers?1.4f:6);m.startSpeed=new ParticleSystem.MinMaxCurve(.12f,embers?.8f:.42f);
  m.startSize=new ParticleSystem.MinMaxCurve(embers?.025f:.35f,embers?.06f:.65f);m.maxParticles=embers?32:56;m.simulationSpace=ParticleSystemSimulationSpace.World;m.startColor=Color.white;
  var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Cone;sh.angle=8;sh.radius=.16f;sh.rotation=new Vector3(-90,0,0);
  var em=ps.emission;em.rateOverTime=embers?6:7;
  var vel=ps.velocityOverLifetime;vel.enabled=true;vel.space=ParticleSystemSimulationSpace.World;vel.x=.13f;vel.y=embers?.12f:.3f;vel.z=.05f;
  var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.4f,1,embers?0:2.5f));
  var fade=ps.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.15f),new GradientAlphaKey(.45f,.55f),new GradientAlphaKey(0,1)});fade.color=gradient;
  var r=ps.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;
 }
 static void AddHulls(Transform parent)
 {
  // Only larger new props need camera hulls; very small stones stay below the lens.
  foreach(Transform t in parent)
  {
   var rs=t.GetComponentsInChildren<Renderer>().Where(r=>!(r is ParticleSystemRenderer)).ToArray();if(rs.Length==0)continue;
   var b=Bounds(t);if(b.size.y<.4f)continue;
   var old=t.Find("CameraHull");if(old)Object.DestroyImmediate(old.gameObject);
   var hull=new GameObject("CameraHull",typeof(BoxCollider),typeof(BattleCameraObstacle));hull.layer=2;hull.transform.position=b.center;hull.GetComponent<BoxCollider>().size=b.size;hull.GetComponent<BoxCollider>().isTrigger=true;hull.transform.SetParent(t,true);
  }
 }
}
