Shader "VisionStriker/Story Stage Water"
{
 Properties {
  _BaseColor("Deep water",Color)=(.025,.11,.105,1)
  _FoamColor("Foam",Color)=(.78,.88,.86,1)
  _Speed("Flow",Float)=.5
  _Fall("Waterfall",Float)=0
  _FoamPatch("Basin foam",Float)=0
 }
 SubShader {
  Tags {"RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor,_FoamColor;float _Speed,_Fall,_FoamPatch;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;float4 screen:TEXCOORD2;float fog:TEXCOORD3;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.screen=ComputeScreenPos(o.p);o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
   float hash(float2 p){float3 p3=frac(float3(p.xyx)*.1031);p3+=dot(p3,p3.yzx+33.33);return frac((p3.x+p3.y)*p3.z);}
   float noise(float2 p){float2 b=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(b),hash(b+float2(1,0)),f.x),lerp(hash(b+float2(0,1)),hash(b+1),f.x),f.y);}
   float fbm(float2 p){return noise(p)*.56+noise(p*2.07+3.1)*.28+noise(p*4.13+8.3)*.16;}
   float waves(float2 p,float t){
    float flow=fbm(p*.17+float2(-t*.07,t*.025));
    float longWave=sin(dot(p,float2(.82,1.31))-t*1.6+flow)*.16;
    float crossWave=sin(dot(p,float2(-1.73,.62))-t*1.1)*.08;
    float fine=fbm(p*float2(.9,1.7)+float2(t*.16,t*.29))*.35;
    float chop=fbm(p.yx*float2(2.2,.8)+float2(-t*.24,t*.18))*.20;
    return longWave+crossWave+fine+chop;
   }
   half4 frag(V i):SV_Target {
    float t=_Time.y*_Speed;float2 screen=i.screen.xy/i.screen.w;
    float sceneZ=LinearEyeDepth(SampleSceneDepth(screen),_ZBufferParams);
    float surfaceZ=-TransformWorldToView(i.world).z;
    float depth=max(0,sceneZ-surfaceZ);
    float3 view=GetWorldSpaceNormalizeViewDir(i.world);
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
    if(_FoamPatch>.5){
      float2 q=(i.uv-.5)*2;
      float r=length(q),angle=atan2(q.y,q.x);
      float f=fbm(i.world.xz*1.45+float2(t*.3,-t*.55));
      float rings=sin(r*24-t*5+f*3+sin(angle*5))*.5+.5;
      float center=1-smoothstep(.12,.70,r);
      float edge=(1-smoothstep(.55,1,r+f*.12));
      float foam=(smoothstep(.25,.63,f)*center*.88+pow(rings,5)*.30)*edge;
      return half4(MixFog(_FoamColor.rgb*(.78+sun.color*.24),i.fog),foam*.85);
    }
    if(_Fall>.5){
      float2 p=i.uv*float2(23,3.5);p.y+=t*3.2;
      float streak=fbm(p+float2(fbm(i.uv*5+t*.2)*1.3,0));
      float fine=fbm(i.uv*float2(91,18)+float2(t*.13,t*10));
      float ribbons=fbm(float2(i.uv.x*32+sin(i.uv.y*7+t)*.3,t*.20));
      float aeration=1-smoothstep(.15,.95,i.uv.y);
      float edge=min(i.uv.x,1-i.uv.x);
      float mask=smoothstep(.015,.11,edge-fbm(i.uv*float2(11,32)+t)*.045);
      float weight=smoothstep(.20,.78,streak*.52+fine*.28+ribbons*.20);
      float bottom=1-smoothstep(.0,.07,i.uv.y);
      float alpha=mask*(.16+.62*weight+ aeration*.12)*smoothstep(0,.45,depth);
      alpha*=1-bottom*.55;
      float3 color=lerp(_BaseColor.rgb,_FoamColor.rgb,.15+weight*.63+aeration*.16);
      color*=.7+sun.color*.38;
      return half4(MixFog(color,i.fog),alpha);
    }
    float2 p=i.world.xz;
    float e=.055,w=waves(p,t);
    float dx=(waves(p+float2(e,0),t)-w)/e;
    float dz=(waves(p+float2(0,e),t)-w)/e;
    float3 n=normalize(float3(-dx*.145,1,-dz*.145));
    float3 reflectDir=reflect(-view,n);
    float3 reflection=GlossyEnvironmentReflection(reflectDir,i.world,.14,1);
    float fresnel=.025+.88*pow(1-saturate(dot(view,n)),5);
    float3 halfDir=normalize(view+sun.direction);
    float spec=pow(saturate(dot(n,halfDir)),170)*sun.shadowAttenuation;
    float3 water=lerp(float3(.105,.21,.19),_BaseColor.rgb,saturate(depth*.7));
    float3 refractColor=SampleSceneColor(clamp(screen+n.xz*.008*saturate(depth),.002,.998));
    water=lerp(refractColor,water,.55+.25*saturate(depth));
    float3 color=lerp(water,reflection*.92,fresnel)+sun.color*spec*.45;
    // Foam gathers only at intersections and broken crests, not repeating white stripes.
    float shore=(1-smoothstep(.04,.65,depth))*smoothstep(.005,.08,depth);
    float foam=shore*smoothstep(.38,.67,fbm(p*3+float2(t*.1,t*.4)));
    color=lerp(color,_FoamColor.rgb,foam*.7);
    return half4(MixFog(color,i.fog),smoothstep(0,.13,depth)*.97);
   }
   ENDHLSL
  }
 }
}
