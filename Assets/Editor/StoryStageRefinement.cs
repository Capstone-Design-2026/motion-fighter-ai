using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Object=UnityEngine.Object;

public static partial class StoryStagesBuilder
{
    const string Pbr=Root+"/Textures/PBR/";
    static void ImportPbrTextures()
    {
        foreach(string f in Directory.GetFiles(Pbr))
        {
            if(f.EndsWith(".meta"))continue;
            var imp=AssetImporter.GetAtPath(f.Replace('\\','/')) as TextureImporter;if(!imp)continue;
            imp.maxTextureSize=2048;imp.mipmapEnabled=true;imp.wrapMode=TextureWrapMode.Repeat;imp.anisoLevel=8;
            imp.textureType=f.Contains("nor_gl")?TextureImporterType.NormalMap:TextureImporterType.Default;
            imp.sRGBTexture=f.Contains("Diffuse");imp.textureCompression=TextureImporterCompression.CompressedHQ;imp.SaveAndReimport();
        }
    }
    static bool Paved=>new[]{"gate","old_ferry","entrance","final"}.Contains(Ids[index]);
    static Vector3[] dressingVertices;
    static float TerrainHeight(float x,float z)
    {
        var vs=dressingVertices;
        // Source terrain is a regular 181 by 181 grid. Use its actual mesh for dressing height.
        int side=Mathf.RoundToInt(Mathf.Sqrt(vs.Length));
        float step=vs[1].x-vs[0].x;
        if(side*side!=vs.Length||Mathf.Abs(step)<.001f)return 0;
        int ix=Mathf.Clamp(Mathf.RoundToInt((x-vs[0].x)/step),0,side-1);
        int iz=Mathf.Clamp(Mathf.RoundToInt((z-vs[0].z)/step),0,side-1);
        return vs[iz*side+ix].y;
    }
    static Material ContinuousGround()
    {
        string tex=Paved?"cobblestone_floor_02":Ids[index]=="quarry"?"rock_face_03":"forest_ground_04";
        Color c=Paved?new Color(.65f,.70f,.72f):Ids[index]=="quarry"?new Color(.88f,.83f,.71f):
            Ids[index]=="ridge"?new Color(.95f,.82f,.60f):new Color(.80f,.83f,.75f);
        var m=Mat(Ids[index]+"_Ground",c,Pbr+tex+"_Diffuse.jpg","VisionStriker/Continuous Ground");
        m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Pbr+tex+"_nor_gl.png"));
        m.SetTexture("_OcclusionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Pbr+tex+"_Rough.png"));
        m.EnableKeyword("_NORMALMAP");m.SetFloat("_BumpScale",.9f);
        m.SetFloat("_Smoothness",Paved?.40f:.12f);
        m.SetFloat("_Metallic",0);m.SetFloat("_Cutoff",.5f);
        m.SetTextureScale("_BaseMap",Vector2.one*(Paved?1.15f:.72f));
        return m;
    }
    static void RefinedWater(string name,Vector3 center,Vector2 size,bool fall)
    {
        var m=Mat(fall?"FallingWater":"FlowingWater",fall?new Color(.34f,.45f,.46f):new Color(.025f,.11f,.105f),null,"VisionStriker/Story Stage Water");
        m.SetColor("_FoamColor",new Color(.78f,.88f,.86f));m.SetFloat("_Fall",fall?1:0);m.SetFloat("_Speed",fall?1.15f:.5f);
        // A slightly bowed, irregular-edged curtain avoids a rigid flat strip.
        if(fall)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();const int nx=24,ny=30;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
            {
                float u=x/(float)nx,t=y/(float)ny;
                float width=size.x*(.88f+t*.16f);
                v.Add(center+new Vector3((u-.5f)*width+Mathf.Sin(t*9)*.10f,(.5f-t)*size.y,-Mathf.Sin(t*Mathf.PI)*.45f));
                uv.Add(new Vector2(u,1-t));
            }
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int a=y*(nx+1)+x;tr.AddRange(new[]{a,a+1,a+nx+1,a+1,a+nx+2,a+nx+1});}
            MeshObject(world,"Cascade_"+name,v,tr,m,uv);
            Spray(center-new Vector3(0,size.y*.46f,.3f),size.x);
        }
        else
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Quad);g.name=name;g.transform.SetParent(world,false);
            Object.DestroyImmediate(g.GetComponent<Collider>());g.transform.position=center;g.transform.rotation=Quaternion.Euler(90,0,0);
            g.transform.localScale=new Vector3(size.x,size.y,1);g.GetComponent<Renderer>().sharedMaterial=m;
        }
    }
    static void Spray(Vector3 p,float width)
    {
        var m=Mat("Water mist",new Color(.67f,.80f,.78f,.14f),null,"VisionStriker/Camp Smoke");
        var ps=new GameObject("Drifting cascade spray",typeof(ParticleSystem)).GetComponent<ParticleSystem>();ps.transform.SetParent(world,false);ps.transform.position=p;
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=true;main.prewarm=true;main.startLifetime=2.3f;main.startSpeed=.45f;main.startSize=new ParticleSystem.MinMaxCurve(.45f,1.1f);main.maxParticles=45;main.startColor=Color.white;
        var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Box;sh.scale=new Vector3(width*.9f,.2f,.8f);
        var vel=ps.velocityOverLifetime;vel.enabled=true;vel.x=.25f;vel.y=.25f;vel.z=-.18f;
        var em=ps.emission;em.rateOverTime=14;
        var col=ps.colorOverLifetime;col.enabled=true;var gr=new Gradient();gr.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.2f),new GradientAlphaKey(0,1)});col.color=gr;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=m;
    }
    static void Pavilion(Vector3 p,float width)
    {
        var t=Group("Tiled gate pavilion",p);float h=width*.28f,d=width*.55f;
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box(t,"Dark red pillar",new Vector3(x*width*.34f,h*.5f,z*d*.32f),new Vector3(.38f,h,.38f),wood);
        Box(t,"Pavilion platform",Vector3.zero,new Vector3(width*.84f,.38f,d*.9f),stone);
        for(int s=-1;s<=1;s+=2){Box(t,"Eave beam",new Vector3(0,h,s*d*.32f),new Vector3(width*.90f,.32f,.32f),wood);Box(t,"Back rail",new Vector3(0,.85f,s*d*.34f),new Vector3(width*.72f,.15f,.15f),wood);}
        var verts=new List<Vector3>();var uvs=new List<Vector2>();var tri=new List<int>();
        for(int z=0;z<=16;z++)for(int x=0;x<=16;x++)
        {
            float px=(x/16f-.5f)*width,pz=(z/16f-.5f)*d;
            float k=Mathf.Abs(pz)/(d*.5f),end=Mathf.Pow(Mathf.Abs(px)/(width*.5f),6);
            float py=h+width*.16f*(1-k)+Mathf.Pow(k,5)*width*.055f+end*width*.035f;
            verts.Add(new Vector3(px,py,pz));uvs.Add(new Vector2(px*.6f,pz*.8f));
        }
        for(int z=0;z<16;z++)for(int x=0;x<16;x++){int a=z*17+x;tri.AddRange(new[]{a,a+17,a+1,a+1,a+17,a+18});}
        MeshObject(t,"Curved tiled roof "+p.x+"_"+p.z,verts,tri,roof,uvs);
        Box(t,"Roof ridge",new Vector3(0,h+width*.16f,0),new Vector3(width*.98f,.22f,.26f),roof);
        Combine(t,"Pavilion_"+p.x+"_"+p.z);Hull(t);
    }
    static void RockCluster(Vector3 p,float scale=1)
    {
        for(int k=0;k<3;k++)
        {
            Vector3 q=p+new Vector3(Rand(-1.1f,1.1f),0,Rand(-.8f,.8f))*scale;
            q.y=TerrainHeight(q.x,q.z)-.15f;
            var g=Nature("LowBoulder",q,Rand(1.2f,2.6f)*scale,Rand(0,360),false);
            g.transform.localScale=Vector3.Scale(g.transform.localScale,new Vector3(1,Rand(.42f,.80f),Rand(.7f,1.2f)));
        }
    }
    static void Stash(Vector3 p,float yaw=0)
    {
        Old("Rice_Chest",p,1.5f,yaw);Old("Straw_Bag",p+new Vector3(1.1f,0,.3f),.95f,yaw+20);
        Old("Water_Jar",p+new Vector3(-.9f,0,-.4f),.85f,yaw);Old("Wicker_Basket",p+new Vector3(.8f,0,-.8f),.6f,yaw);
    }
    static void RefineEnvironment()
    {
        // Keep a continuous single surface under both fighters and all surrounding scenery.
        // Tall dressing stays outside the swept combat camera envelope; low foliage can be closer.
        bool waterStage=new[]{"river","old_ferry","final"}.Contains(Ids[index]);
        var groundFilter=world.Find("Arena360Ground").GetComponent<MeshFilter>();var vs=groundFilter.sharedMesh.vertices;
        for(int j=0;j<vs.Length;j++)
        {
            var p=vs[j];float r=new Vector2(p.x,p.z).magnitude;
            if(Paved&&r<42&&(!waterStage||p.z<11))p.y=-.015f;
            vs[j]=p;
        }
        groundFilter.sharedMesh.vertices=vs;groundFilter.sharedMesh.RecalculateNormals();groundFilter.sharedMesh.RecalculateTangents();EditorUtility.SetDirty(groundFilter.sharedMesh);

        dressingVertices=vs;
        for(int k=0;k<42;k++)
        {
            float a=(k*360f/42+Rand(-3,3))*Mathf.Deg2Rad,r=Rand(23,31);
            float x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
            if(waterStage&&z>10)continue;
            if(Paved&&z>18&&Mathf.Abs(x)<14)continue;
            if(k%2==0)RockCluster(new Vector3(x,0,z),Rand(.8f,1.5f));
            if(!Paved&&k%3==0)Nature("BentPine",new Vector3(x,TerrainHeight(x,z),z),Rand(9,14),Rand(0,360),true);
        }
        // Mid-distance terrain overlaps into the far ring instead of isolated rocks on flat soil.
        for(int k=0;k<44;k++)
        {
            float a=k*Mathf.PI*2/44,r=Rand(43,58),x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
            if(waterStage&&z>10&&Mathf.Abs(x)<30)continue;
            if(Ids[index]=="ridge"&&z>10&&Mathf.Abs(x)<27)continue;
            bool forest=new[]{"camp","rescue","east"}.Contains(Ids[index]);
            Nature("CliffRock",new Vector3(x,TerrainHeight(x,z)-2,z),forest?Rand(6,11):Rand(11,19),Rand(0,360),false);
            if(k%2==0)Nature("BentPine",new Vector3(x-2,TerrainHeight(x,z)+2,z+1),Rand(13,19),Rand(0,360),false);
        }
        switch(Ids[index])
        {
            case "river":
                for(int k=0;k<48;k++)
                {
                    float x=-47+k*2.0f,z=k%2==0?10.5f+Mathf.Sin(x*.16f)*1.1f:29.2f+Mathf.Sin(x*.12f);
                    RockCluster(new Vector3(x,0,z),Rand(.8f,1.7f));
                }
                for(int s=-1;s<=1;s+=2)for(int k=0;k<6;k++)
                {
                    Nature("CliffRock",new Vector3(s*(19+k*2),-1,20+k*5),Rand(14,24),k*57+s*12,false);
                    Nature("BentPine",new Vector3(s*(18+k*3),2,19+k*5),Rand(10,17),k*43,false);
                }
                // Break the rectangular fall edges with rocks, and connect its basin to the stream.
                Nature("LowBoulder",new Vector3(-15,-.3f,34),5,48,false);Nature("LowBoulder",new Vector3(-9,-.3f,34),4.8f,170,false);
                RockCluster(new Vector3(-7,0,8.5f),.75f);RockCluster(new Vector3(8,0,9),.9f);
                for(int k=0;k<13;k++)RockCluster(new Vector3(-15+k*3,0,31.2f+Mathf.Sin(k)),1.6f);
                for(int k=0;k<11;k++)
                {
                    float x=-10+k*3.5f;
                    Nature("LowBoulder",new Vector3(x,.2f,34+Mathf.Sin(k)*2),Rand(3.5f,6),k*47,false);
                    if(k%3==0)Nature("BentPine",new Vector3(x,1.5f,38),Rand(10,15),k*62,false);
                }
                break;
            case "ridge":
                for(int k=0;k<7;k++)RockCluster(new Vector3(-15+k*5,0,16+Mathf.Sin(k)*2),.9f);
                Stash(new Vector3(-11,0,11));Stash(new Vector3(13,0,-12),50);
                break;
            case "old_ferry":
                House("Tavern_House_02",new Vector3(-18,0,2),13,90);House("House_By_The_West_Gate_01",new Vector3(16,0,-16),12,-45);
                House("Smithy",new Vector3(-8,0,-26),12,0);
                Stash(new Vector3(-7,0,9));Stash(new Vector3(8,0,9),-25);Stash(new Vector3(-15,0,-7),90);
                Prop("CargoBoat",new Vector3(-14,-1.0f,29),9,-12).AddComponent<StoryStageMotion>();
                for(int k=0;k<12;k++)RockCluster(new Vector3(-28+k*5,0,11),.85f);
                var quay=Group("Continuous quay retaining face",Vector3.zero);
                Box(quay,"Weathered quay edge",new Vector3(0,-1.5f,13.5f),new Vector3(65,2.8f,1.4f),stone);
                Combine(quay,"QuayRetainingFace");
                break;
            case "gate":
                House("House_By_The_West_Gate_02",new Vector3(16,0,-13),10,-90);
                House("Local_Clerks_House_02",new Vector3(-13,0,-19),11,0);
                Stash(new Vector3(11,0,8),-20);Stash(new Vector3(-11,0,9),30);
                for(int s=-1;s<=1;s+=2){Flag(new Vector3(s*9,0,15),6.3f,s*90);RockCluster(new Vector3(s*18,0,10),1.3f);}
                Pavilion(new Vector3(0,0,-27),10);break;
            case "quarry":
                for(int k=0;k<12;k++)
                {
                    int sign=k%2==0?-1:1;Nature("CliffRock",new Vector3(sign*Rand(19,28),-1,12+k*2.8f),Rand(10,19),Rand(0,360),false);
                    RockCluster(new Vector3(sign*Rand(11,17),0,Rand(9,24)),Rand(.65f,1.4f));
                }
                for(int k=0;k<8;k++)Nature("CliffRock",new Vector3(-24+k*7,-1,-25),Rand(9,15),k*48,false);
                Stash(new Vector3(9,0,12));Old("Cart",new Vector3(-11,0,12),4,20);
                Nature("CliffRock",new Vector3(-7,-.6f,32),8,62,false);
                Nature("CliffRock",new Vector3(7,-.6f,32),8,198,false);
                var tunnel=Group("Braced mine entrance",new Vector3(0,0,32));
                Box(tunnel,"Left shoring",new Vector3(-2.8f,2.8f,0),new Vector3(.35f,5.6f,.4f),wood);
                Box(tunnel,"Right shoring",new Vector3(2.8f,2.8f,0),new Vector3(.35f,5.6f,.4f),wood);
                Box(tunnel,"Top shoring",new Vector3(0,5.7f,0),new Vector3(6.2f,.4f,.5f),wood);
                Beam(tunnel,new Vector3(-2.8f,4.3f,-.1f),new Vector3(-1.5f,5.7f,-.1f),.23f,wood);
                Beam(tunnel,new Vector3(2.8f,4.3f,-.1f),new Vector3(1.5f,5.7f,-.1f),.23f,wood);
                Combine(tunnel,"TunnelShoring");
                break;
            case "east":
                for(int k=0;k<9;k++){RockCluster(new Vector3(-18+k*4.5f,0,14+Mathf.Sin(k)*2),1.2f);}
                for(int s=-1;s<=1;s+=2)Nature("CliffRock",new Vector3(s*22,-2,12),20,30*s,false);
                Stash(new Vector3(11,0,11),-40);Flag(new Vector3(-12,0,18),5);break;
            case "camp":
                Tent(new Vector3(-18,0,-5),85);Tent(new Vector3(18,0,-8),-80);Tent(new Vector3(3,0,-25),180);
                Stash(new Vector3(-8,0,12));Stash(new Vector3(9,0,12),-20);Stash(new Vector3(-13,0,-12),70);
                Old("Low_Wooden_Bench",new Vector3(2,0,13),2.8f);Old("Cart",new Vector3(14,0,18),4,-10);
                for(int k=0;k<6;k++)Nature("TimberFence",new Vector3(-16+k*6,0,30),5.3f,0);
                break;
            case "rescue":
                House("House_With_Wooden_Floored_Room_01",new Vector3(17,0,-9),11,-90);
                House("House_With_Calling_Windows_01",new Vector3(-13,0,-19),10,0);
                Prop("PrisonCage",new Vector3(8,0,-19),5,180);Stash(new Vector3(-9,0,11));Stash(new Vector3(12,0,10),70);
                Tower(new Vector3(-20,0,-22),8);Tower(new Vector3(20,0,-22),8);break;
            case "entrance":
                for(int s=-1;s<=1;s+=2)
                {
                    for(int k=0;k<4;k++)Nature("CliffRock",new Vector3(s*(27+k),-3,-17+k*11),Rand(22,33),k*61,false);
                    Stash(new Vector3(s*15,0,8),s*40);Flag(new Vector3(s*10,0,14),6.5f,s*90);
                }
                Pavilion(new Vector3(0,0,-27),11);break;
            case "final":
                // Dam masonry physically joins the riverbanks; no isolated bridge floating in a lake.
                foreach(int s in new[]{-1,1})
                {
                    Wall(new Vector3(s*34,-4,36),23,17);Wall(new Vector3(s*44,-4,19),35,14,90);
                    Nature("CliffRock",new Vector3(s*47,-7,30),30,s*75,false);
                    House("Local_Personnel_Clerks_House_01",new Vector3(s*18,0,-12),11,s*90);
                    Stash(new Vector3(s*15,0,6),s*30);
                }
                Pavilion(new Vector3(0,0,-27),13);
                for(int k=0;k<5;k++)RockCluster(new Vector3(-19+k*9,0,-18),.9f);
                break;
        }
        DenseVegetation();
        LowShrubs();
        DrapeTents();
        foreach(var tree in world.Cast<Transform>().Where(t=>t.name=="BentPine").ToArray())
        {
            Vector3 p=tree.position;float y=TerrainHeight(p.x,p.z);
            if(waterStage&&y<-.36f){Object.DestroyImmediate(tree.gameObject);continue;}
            tree.position=new Vector3(p.x,y,p.z);
        }
        if(waterStage)
        {
            var g=new GameObject("Water surroundings reflection",typeof(ReflectionProbe));g.transform.SetParent(world,false);
            g.transform.position=new Vector3(0,3,Ids[index]=="final"?27:18);
            var probe=g.GetComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.AllFacesAtOnce;probe.resolution=128;probe.size=new Vector3(230,100,230);
            probe.nearClipPlane=.3f;probe.farClipPlane=220;probe.intensity=1;probe.boxProjection=false;
        }
    }
    static void DenseVegetation()
    {
        var verts=new List<Vector3>();var tris=new List<int>();var uvs=new List<Vector2>();var colors=new List<Color>();
        // Individual tapered, bent leaves, grouped in clumps instead of isolated triangular spikes.
        for(int k=0;k<1600;k++)
        {
            float x=Rand(-36,36),z=Rand(-32,36);
            if(Mathf.Abs(x)<8&&Mathf.Abs(z)<7)continue;
            if(Paved&&Mathf.Abs(x)<19&&z>-21&&z<18)continue;
            if(new[]{"river","old_ferry","final"}.Contains(Ids[index])&&z>10)continue;
            if(Mathf.PerlinNoise(x*.19f,z*.19f)<.44f)continue;
            float y=TerrainHeight(x,z);
            for(int j=0;j<10;j++)
            {
                float a=Rand(0,Mathf.PI*2),h=Rand(.15f,.48f),w=Rand(.012f,.030f),lean=Rand(.12f,.34f);
                Vector3 b=new Vector3(x+Rand(-.35f,.35f),y,z+Rand(-.35f,.35f)),dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),side=Vector3.Cross(dir,Vector3.up)*w;
                int n=verts.Count;
                verts.AddRange(new[]{b-side,b+side,b+Vector3.up*h*.55f+dir*lean*.3f-side*.7f,b+Vector3.up*h*.55f+dir*lean*.3f+side*.7f,b+Vector3.up*h+dir*lean});
                uvs.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,.55f),new Vector2(1,.55f),new Vector2(.5f,1)});
                tris.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3,n+2,n+4,n+3});
            }
        }
        var mat=Mat(Ids[index]+"_GrassClumps",Ids[index]=="ridge"?new Color(.49f,.44f,.24f):new Color(.23f,.31f,.17f),null,"VisionStriker/Living Grass");
        MeshObject(world,"Layered grass clumps",verts,tris,mat,uvs);
    }
    static void LowShrubs()
    {
        if(Paved||Ids[index]=="quarry")return;
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int i=0;i<95;i++)
        {
            float x=Rand(-29,29),z=Rand(-28,29);
            if(Mathf.Abs(x)<8&&Mathf.Abs(z)<7)continue;
            if(Ids[index]=="river"&&z>9)continue;
            if(i<12){x=-19+i*3.4f;z=Ids[index]=="river"?9:14;}
            var center=new Vector3(x,TerrainHeight(x,z),z);
            float h=Rand(.3f,.65f),wide=Rand(.5f,1.1f);
            for(int j=0;j<80;j++)
            {
                float a=Rand(0,Mathf.PI*2),r=Rand(.08f,1),height=Mathf.Sqrt(1-r*r)*h;
                Vector3 p=center+new Vector3(Mathf.Cos(a)*wide*r,height,Mathf.Sin(a)*wide*r);
                Vector3 axis=new Vector3(Mathf.Cos(a),Rand(-.3f,.5f),Mathf.Sin(a)).normalized*Rand(.08f,.18f);
                Vector3 side=Vector3.Cross(axis,Vector3.up).normalized*axis.magnitude*.40f;
                int n=v.Count;v.AddRange(new[]{p-axis,p-side,p+Vector3.up*.015f,p+side,p+axis});
                uv.AddRange(new[]{new Vector2(.5f,0),new Vector2(0,.5f),new Vector2(.5f,.5f),new Vector2(1,.5f),new Vector2(.5f,1)});
                tr.AddRange(new[]{n,n+1,n+2,n,n+2,n+3,n+2,n+1,n+4,n+2,n+4,n+3});
            }
        }
        var m=Mat(Ids[index]+"_Shrubs",Ids[index]=="ridge"?new Color(.38f,.39f,.20f):new Color(.22f,.30f,.18f),null,"VisionStriker/Living Grass");
        MeshObject(world,"Low leafy understory",v,tr,m,uv);
    }
    static void DrapeTents()
    {
        foreach(var tent in world.Cast<Transform>().Where(t=>t.name=="Field canvas tent").ToArray())
        {
            // Replace only the flat canvas after the source build has combined it.
            foreach(var r in tent.GetComponentsInChildren<MeshRenderer>())
                if(r.sharedMaterial&&r.sharedMaterial.name=="WeatheredCanvas")Object.DestroyImmediate(r.gameObject);
            var m=Mat("WeatheredCanvas",new Color(.39f,.32f,.23f),Scout+"/Textures/PathGround.png");m.SetFloat("_Cull",0);
            var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
            for(int s=-1;s<=1;s+=2)
            {
                int start=v.Count;
                for(int y=0;y<=12;y++)for(int x=0;x<=16;x++)
                {
                    float u=x/16f,t=y/12f;
                    float dip=Mathf.Sin(u*Mathf.PI)*(.17f+.08f*Mathf.Sin(t*32));
                    v.Add(new Vector3(s*u*3,3.5f*(1-u)-dip,-2.5f+t*5));uv.Add(new Vector2(u,t));
                }
                for(int y=0;y<12;y++)for(int x=0;x<16;x++){int a=start+y*17+x;tr.AddRange(new[]{a,a+1,a+17,a+1,a+18,a+17});}
            }
            MeshObject(tent,"Draped canvas "+tent.position.x+"_"+tent.position.z,v,tr,m,uv);
            for(int s=-1;s<=1;s+=2)
            {
                var rope=Group("Tent ropes",Vector3.zero);rope.SetParent(tent,false);
                Beam(rope,new Vector3(s*2.8f,.3f,-2.5f),new Vector3(s*3.5f,.1f,-3.2f),.025f,wood);
                Beam(rope,new Vector3(s*2.8f,.3f,2.5f),new Vector3(s*3.5f,.1f,3.2f),.025f,wood);
                Combine(rope,"TentRope"+tent.position.x+"_"+tent.position.z+"_"+s);
            }
        }
    }
}
