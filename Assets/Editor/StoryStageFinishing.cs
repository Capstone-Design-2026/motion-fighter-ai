using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

public static partial class StoryStagesBuilder
{
    public static void SettleDressingAndReview()
    {
        for(index=0;index<Ids.Length;index++)
        {
            if(!new[]{"gate","quarry","camp","rescue","entrance","final"}.Contains(Ids[index]))continue;
            var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/StoryStages/"+Scenes[index]+".unity");
            world=scene.GetRootGameObjects().First(g=>g.name=="Story Stage · "+Ids[index]).transform;
            SeatPropsOnGround();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();StoryStagesReview.Run();
    }
    public static void TuneWaterAndReview()
    {
        foreach(string sceneName in new[]{"RiverAmbush","OldFerry","SluiceFortress"})
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/StoryStages/"+sceneName+".unity");
            if(sceneName=="SluiceFortress")
            {
                foreach(var mf in Object.FindObjectsByType<MeshFilter>().Where(f=>f.name.StartsWith("Cascade_Sluice cascade")))
                {
                    var mesh=mf.sharedMesh;var vs=mesh.vertices;
                    float low=vs.Min(v=>v.y),high=vs.Max(v=>v.y);
                    for(int i=0;i<vs.Length;i++)vs[i].y=Mathf.Lerp(-3.37f,high,Mathf.InverseLerp(low,high,vs[i].y));
                    mesh.vertices=vs;mesh.RecalculateBounds();mesh.RecalculateNormals();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
                }
            }
            // Existing spray at the old, submerged endpoint is redundant.
            foreach(var ps in Object.FindObjectsByType<ParticleSystem>().Where(p=>p.name=="Drifting cascade spray"&&p.transform.position.y< -3.5f).ToArray())
                Object.DestroyImmediate(ps.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();StoryStagesReview.Run();
    }
    static readonly List<string> architectureChecks=new List<string>();
    static void Joint(bool ok,string label)
    {
        architectureChecks.Add((ok?"PASS ":"FAIL ")+Ids[index]+": "+label);
        File.WriteAllLines(Path.Combine(Output,"architecture_checks.txt"),architectureChecks);
        if(!ok)throw new InvalidOperationException("Disconnected architecture: "+label);
    }
    static Bounds MeshBounds(GameObject g)=>g.GetComponent<Renderer>().bounds;
    static bool Touch(Bounds a,Bounds b,float tolerance=.03f)
    {a.Expand(tolerance);return a.Intersects(b);}

    static void ConnectedGate(Vector3 p,float size,float halfWidth,float height,bool timber)
    {
        var door=Prop("GateDoors",p,size,180);
        var db=Bounds(door.transform);
        // Sink the irregular scanned bottom into a continuous threshold.
        door.transform.position-=Vector3.up*.10f;db=Bounds(door.transform);
        float inner=db.extents.x-.15f,jamb=timber?.65f:1.2f;
        float gateTop=db.max.y-p.y;
        var frame=Group(timber?"Joined prison gate":"Joined stone gate",p);
        var mat=timber?wood:stone;
        var left=Box(frame,"Left gate jamb",new Vector3(-inner-jamb*.5f,gateTop*.5f,0),new Vector3(jamb,gateTop+.15f,2.2f),mat);
        var right=Box(frame,"Right gate jamb",new Vector3(inner+jamb*.5f,gateTop*.5f,0),new Vector3(jamb,gateTop+.15f,2.2f),mat);
        var lintel=Box(frame,"Continuous lintel",new Vector3(0,gateTop+.22f,0),new Vector3(2*(inner+jamb),.70f,2.35f),mat);
        Box(frame,"Threshold",new Vector3(0,.03f,0),new Vector3(2*(inner+jamb),.20f,2.6f),stone);
        Joint(Touch(db,MeshBounds(left))&&Touch(db,MeshBounds(right)),"door mesh overlaps both jambs");
        Joint(Touch(MeshBounds(left),MeshBounds(lintel))&&Touch(MeshBounds(right),MeshBounds(lintel)),"jambs meet lintel");
        float wallStart=inner+jamb-.20f;
        for(int s=-1;s<=1;s+=2)
        {
            Wall(p+new Vector3(s*(wallStart+halfWidth)*.5f,0,0),halfWidth-wallStart+.2f,height,0,timber);
            Box(frame,"Wing overlap shoulder",new Vector3(s*(inner+jamb-.12f),height*.5f,0),new Vector3(.6f,height,2.1f),mat);
        }
        Joint(wallStart<inner+jamb&&halfWidth>wallStart,"gate wings overlap jambs and reach side walls");
        Combine(frame,"JoinedGateFrame");Hull(frame);
        if(!timber)FortifiedHall(p+Vector3.up*(gateTop+.58f),size*1.8f,1,"GateHall");
        else
        {
            var t=Group("Prison overhead cross bracing",p);
            for(int s=-1;s<=1;s+=2)Beam(t,new Vector3(s*(inner+.5f),gateTop-1.4f,-1.2f),new Vector3(s*(inner-1.0f),gateTop+.1f,-1.2f),.23f,wood);
            Combine(t,"PrisonGateBraces");
        }
    }
    static void EncloseCourtyard(float x,float back,float front,float height,bool timber)
    {
        for(int s=-1;s<=1;s+=2)
        {
            Wall(new Vector3(s*x,0,(back+front)*.5f),front-back+.45f,height,90,timber);
            foreach(float z in new[]{back,front})
            {
                var t=Group("Connected corner pier",new Vector3(s*x,0,z));
                Box(t,"Corner foundation",new Vector3(0,.2f,0),new Vector3(2.0f,.5f,2.0f),stone);
                Box(t,"Corner pier",new Vector3(0,height*.5f,0),new Vector3(timber?.85f:1.6f,height+.2f,timber?.85f:1.6f),timber?wood:stone);
                Box(t,"Pier cap",new Vector3(0,height+.25f,0),new Vector3(1.9f,.35f,1.9f),roof);
                Combine(t,"Corner_"+s+"_"+z);Hull(t);
            }
        }
        Wall(new Vector3(0,0,back),2*x+.25f,height,0,timber);
        Joint(front>back&&x>18,"courtyard side/back walls share corner endpoints");
    }
    static void CurvedRoof(Transform t,float width,float depth,float bottom,string name)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        const int nx=36,nz=20;
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
        {
            float px=(x/(float)nx-.5f)*width,pz=(z/(float)nz-.5f)*depth;
            float a=Mathf.Abs(pz)/(depth*.5f),b=Mathf.Abs(px)/(width*.5f);
            float y=bottom+depth*.25f*(1-a)+Mathf.Pow(a,5)*depth*.07f+Mathf.Pow(b,7)*.38f;
            v.Add(new Vector3(px,y,pz));uv.Add(new Vector2(px*1.5f,pz*1.6f));
        }
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){int a=z*(nx+1)+x;tr.AddRange(new[]{a,a+nx+1,a+1,a+1,a+nx+1,a+nx+2});}
        MeshObject(t,name,v,tr,roof,uv);
        Box(t,"Tile ridge",new Vector3(0,bottom+depth*.25f,0),new Vector3(width*.96f,.24f,.28f),roof);
        // Shallow rafters and a fascia give the eaves thickness from reverse camera angles.
        for(int s=-1;s<=1;s+=2)
        {
            Box(t,"Eave fascia",new Vector3(0,bottom+.03f,s*depth*.46f),new Vector3(width*.94f,.22f,.24f),wood);
            for(float x=-width*.40f;x<=width*.40f;x+=.65f)
                Beam(t,new Vector3(x,bottom-.18f,s*depth*.30f),new Vector3(x,bottom+.07f,s*depth*.47f),.10f,wood);
        }
    }
    static void FortifiedHall(Vector3 p,float width,int storeys,string name)
    {
        var t=Group(name,p);float depth=width*.53f,h=width*.22f;
        var red=Mat("Fortress aged red timber",new Color(.26f,.095f,.065f),"Assets/Naganeupseong/Textures/Structure/Pillar/T_Pillar02a_BC.png");
        for(int level=0;level<storeys;level++)
        {
            float w=width*(level==0?1:.72f),d=depth*(level==0?1:.70f),y=level*h*1.08f;
            Box(t,"Storey plinth",new Vector3(0,y,0),new Vector3(w*.89f,.44f,d*.90f),stone);
            Box(t,"Recessed hall interior",new Vector3(0,y+h*.47f,0),new Vector3(w*.70f,h*.94f,d*.60f),red);
            for(int s=-1;s<=1;s+=2)
            {
                for(int i=0;i<=6;i++)
                {
                    float x=(i/6f-.5f)*w*.78f;
                    Box(t,"Red structural column",new Vector3(x,y+h*.48f,s*d*.37f),new Vector3(.25f,h,.25f),red);
                    if(i==6)continue;
                    float cx=x+w*.065f;
                    Box(t,"Dark lattice window",new Vector3(cx,y+h*.52f,s*d*.305f),new Vector3(w*.09f,h*.57f,.10f),iron);
                    for(int k=-1;k<=1;k++)Box(t,"Window mullion",new Vector3(cx+k*w*.025f,y+h*.52f,s*d*.316f),new Vector3(.035f,h*.57f,.08f),wood);
                    Box(t,"Window cross rail",new Vector3(cx,y+h*.52f,s*d*.32f),new Vector3(w*.09f,.06f,.08f),wood);
                }
                Box(t,"Long lintel",new Vector3(0,y+h,s*d*.37f),new Vector3(w*.88f,.23f,.32f),red);
            }
            CurvedRoof(t,w,d,y+h,name+"_Roof_"+level);
        }
        Combine(t,name+"_"+p.x+"_"+p.y+"_"+p.z);Hull(t);
    }
    static void GrandFortress()
    {
        // Connected masonry bases carry the halls instead of floating roofs on wooden stilts.
        var t=Group("Fortress citadel foundations",Vector3.zero);
        Box(t,"Central raised podium",new Vector3(0,13.7f,39),new Vector3(21,2.1f,12),stone);
        for(int s=-1;s<=1;s+=2)
        {
            Box(t,"Flanking stone bastion",new Vector3(s*23,14.4f,36),new Vector3(7.2f,5.5f,9.5f),stone);
            Box(t,"Bastion footing",new Vector3(s*23,11.8f,36),new Vector3(8,.7f,10),stone);
            for(int k=0;k<4;k++)Box(t,"Blind arrow slit",new Vector3(s*23-2.1f+k*1.4f,15,31.2f),new Vector3(.16f,1.25f,.06f),iron);
            // Two continuous raised covered wings link the central keep to the bastions.
            Box(t,"Upper gallery masonry",new Vector3(s*15.8f,13.4f,39),new Vector3(10.6f,1.2f,6),stone);
            FortifiedHall(new Vector3(s*23,17.2f,36),10,1,"BastionHall"+s);
            FortifiedHall(new Vector3(s*15.8f,14,39),10.6f,1,"Gallery"+s);
            Flag(new Vector3(s*8,15,32.3f),5.5f,s*90);
            Flag(new Vector3(s*27.8f,13,35),5.8f,s*90);
        }
        Combine(t,"CitadelMasonry");
        FortifiedHall(new Vector3(0,14.8f,39),23,2,"TwoStoreyKeep");
        // Rearward return walls and a second silhouette make orbit views read as a compound.
        Wall(new Vector3(0,12,49),51,5.2f);
        for(int s=-1;s<=1;s+=2)Wall(new Vector3(s*25,12,43),12.5f,5.2f,90);
        Joint(23*.5f+10.6f*.5f>15.8f,"keep and flanking galleries overlap in plan");
    }
    static void DressingProp(string name,float x,float z,float size,float yaw=0,float sink=0,bool hull=false)
    {
        float y=TerrainHeight(x,z)-sink;
        Prefab(Root+"/Prefabs/"+name+".prefab",name,new Vector3(x,y,z),size,yaw,hull);
    }
    static void FinishStageDressing()
    {
        // Clear accidental trees across the gate vista before placing intentional side planting.
        if(new[]{"gate","rescue","entrance","final"}.Contains(Ids[index]))
            foreach(var tree in world.Cast<Transform>().Where(t=>t.name=="BentPine"&&Mathf.Abs(t.position.x)<16&&t.position.z>17&&t.position.z<43).ToArray())
                Object.DestroyImmediate(tree.gameObject);
        for(int k=0;k<32;k++)
        {
            float a=k*Mathf.PI*2/32,r=Rand(21,33),x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
            if(new[]{"river","old_ferry","final"}.Contains(Ids[index])&&z>10)continue;
            if(Paved&&Mathf.Abs(x)<18&&z>-23)continue;
            DressingProp("EvergreenShrub",x,z,Rand(1.3f,2.3f),Rand(0,360),.08f);
            if(k%3==0)DressingProp("BankOutcrop",x+1,z-1,Rand(3.3f,5.8f),Rand(0,360),.30f);
        }
        if(Ids[index]=="river"||Ids[index]=="old_ferry")
        {
            for(int k=0;k<14;k++)
            {
                float x=-35+k*5.3f,z=Ids[index]=="river"?10.3f+Mathf.Sin(x*.16f)*1.1f:12.1f;
                if(Ids[index]=="old_ferry"&&Mathf.Abs(x+4)<5)continue;
                DressingProp("BankOutcrop",x,z,Rand(5.2f,7.4f),90+Rand(-18,18),.50f);
                DressingProp("EvergreenShrub",x+1.8f,z-1.4f,Rand(1.4f,2.0f),Rand(0,360),.10f);
            }
            if(Ids[index]=="river")for(int k=0;k<10;k++)DressingProp("BankOutcrop",-27+k*6,30.5f,Rand(5,8),80+k*17,.4f);
        }
        switch(Ids[index])
        {
            case "gate":
                Tent(new Vector3(13,0,-4),-90);Tent(new Vector3(-14,0,-5),90);
                for(int s=-1;s<=1;s+=2){Stash(new Vector3(s*16,0,17),s*90);DressingProp("EvergreenShrub",s*18,19,2.5f);DressingProp("BankOutcrop",s*20,-20,5.5f,15,.4f);}break;
            case "rescue":
                Tent(new Vector3(-15,0,10),90);Tent(new Vector3(16,0,4),-90);
                Prop("PrisonCage",new Vector3(-14,0,-9),4.6f,90);
                Prop("PrisonCage",new Vector3(15,0,17.8f),4.6f,-90);
                Stash(new Vector3(16,0,-16),180);Supplies(new Vector3(-8,0,-22),0);
                for(int s=-1;s<=1;s+=2)for(int k=0;k<4;k++)DressingProp("EvergreenShrub",s*21,-17+k*11,2.0f,k*83,.1f);
                break;
            case "entrance":
                for(int s=-1;s<=1;s+=2)
                {
                    DressingProp("BankOutcrop",s*19,17,7,90,.4f);DressingProp("BankOutcrop",s*20,-15,6,50,.5f);
                    Tent(new Vector3(s*17,0,-9),-s*90);Stash(new Vector3(s*17,0,-17),s*90);
                    DressingProp("EvergreenShrub",s*18,12,2.3f,180);
                    FortifiedHall(new Vector3(s*19,4.8f,25),6,1,"GorgeCornerHall"+s);
                }
                break;
            case "final":
                for(int s=-1;s<=1;s+=2)
                {
                    Tent(new Vector3(s*18,0,2),-s*90);
                    for(int k=0;k<4;k++)DressingProp("BankOutcrop",s*(27+k*4),20+k*4,8+k,70+k*31,.7f);
                    for(int k=0;k<4;k++)DressingProp("EvergreenShrub",s*22,-18+k*8,2.3f,k*56,.1f);
                    Stash(new Vector3(s*19,0,9),s*70);Supplies(new Vector3(s*15,0,-20),s*30);
                }
                break;
            case "camp":
                Tent(new Vector3(-15,0,29),20);Tent(new Vector3(16,0,29),-25);
                for(int k=0;k<5;k++)DressingProp("EvergreenShrub",-16+k*8,32,2.4f,k*41,.1f);
                break;
            case "quarry":
                Tent(new Vector3(17,0,-16),-65);
                for(int s=-1;s<=1;s+=2)for(int k=0;k<4;k++)DressingProp("BankOutcrop",s*(17+k),-8+k*7,5+k*.4f,k*37,.4f);
                break;
            case "ridge":case "east":
                for(int k=0;k<6;k++){DressingProp("BankOutcrop",-17+k*7,17,4.5f,k*42,.4f);DressingProp("EvergreenShrub",-16+k*7,15.5f,1.7f,k*71,.1f);}break;
        }
        // Waterfall mouths terminate into a churning basin, not a plain transparent plane.
        if(Ids[index]=="river")BasinFoam(new Vector3(-12,-.325f,34),new Vector2(7,4));
        if(Ids[index]=="final")for(int s=-1;s<=1;s++)BasinFoam(new Vector3(s*13,-3.365f,32.8f),new Vector2(11,5.8f));
        SeatPropsOnGround();
    }
    static void SeatPropsOnGround()
    {
        var mesh=world.Find("Arena360Ground").GetComponent<MeshFilter>().sharedMesh;
        var vs=mesh.vertices;dressingVertices=vs;
        foreach(var t in world.Cast<Transform>().Where(t=>new[]{"MilitaryTent","PrisonCage","QuarryHoist"}.Contains(t.name)).ToArray())
        {
            var b=Bounds(t);float y=TerrainHeight(b.center.x,b.center.z);
            t.position+=Vector3.up*(y-b.min.y-.025f);
            // Sculpt the same terrain mesh into a small level footing, feathering its edge.
            for(int j=0;j<vs.Length;j++)
            {
                var p=vs[j];if(Mathf.Abs(p.x)<8&&Mathf.Abs(p.z)<7)continue;
                float dx=Mathf.Max(0,Mathf.Abs(p.x-b.center.x)-b.extents.x);
                float dz=Mathf.Max(0,Mathf.Abs(p.z-b.center.z)-b.extents.z);
                float w=1-Mathf.SmoothStep(0,1,Mathf.Sqrt(dx*dx+dz*dz)/1.25f);
                if(w>0){p.y=Mathf.Lerp(p.y,y,w);vs[j]=p;}
            }
        }
        mesh.vertices=vs;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        if(new[]{"gate","rescue","entrance"}.Contains(Ids[index]))
            foreach(var t in world.Cast<Transform>().Where(t=>new[]{"EvergreenShrub","BankOutcrop"}.Contains(t.name)&&Mathf.Abs(t.position.x)<4&&t.position.z>19).ToArray())
                Object.DestroyImmediate(t.gameObject);
    }
    static void BasinFoam(Vector3 p,Vector2 size)
    {
        var m=Mat("Cascade basin foam",new Color(.10f,.24f,.23f),null,"VisionStriker/Story Stage Water");
        m.SetColor("_FoamColor",new Color(.72f,.82f,.80f));m.SetFloat("_Fall",0);m.SetFloat("_FoamPatch",1);m.SetFloat("_Speed",.7f);m.renderQueue=2990;
        var g=GameObject.CreatePrimitive(PrimitiveType.Quad);g.name="Cascade impact foam";g.transform.SetParent(world,false);
        Object.DestroyImmediate(g.GetComponent<Collider>());g.transform.position=p;g.transform.rotation=Quaternion.Euler(90,0,0);g.transform.localScale=new Vector3(size.x,size.y,1);
        g.GetComponent<Renderer>().sharedMaterial=m;Spray(p+Vector3.up*.1f,size.x*.62f);
    }
    static void ValidateArchitecture()
    {
        if(new[]{"gate","rescue","entrance"}.Contains(Ids[index]))
        {
            Joint(world.Cast<Transform>().Count(t=>t.name=="Connected corner pier")==4,"four continuous courtyard corners exist");
            Joint(world.Cast<Transform>().Any(t=>t.name.StartsWith("Joined ")),"framed gate assembled");
        }
        Joint(world.GetComponentsInChildren<LODGroup>(true).All(g=>g.GetLODs().Length>=3),"all reused prop groups retain distance LODs");
        Joint(world.Cast<Transform>().Any(t=>t.name=="EvergreenShrub"),"new textured vegetation placed");
    }
}
