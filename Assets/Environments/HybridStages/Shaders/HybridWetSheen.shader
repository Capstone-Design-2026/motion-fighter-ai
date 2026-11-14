Shader "VisionStriker/Hybrid Wet Sheen"
{
 // 싸우는 바닥 위에 얇게 덧씌우는 젖은 바닥 반사. 하늘 그림과 해·달빛이 바닥에 비쳐 보이게 한다.
 // 바닥 자체는 건드리지 않고 빛만 더하므로 원래 바닥 재질과 그림자는 그대로 남는다.
 Properties
 {
  _MainTex("Sky painting",2D)="black"{}
  _Arc("Azimuth covered by the painting (deg)",Float)=110
  _Center("Azimuth of the painting centre (deg)",Float)=0
  _GroundV("Painting height at eye level",Float)=.4
  _Lift("Raise the painting (deg)",Float)=0
  _Aspect("Painting width / height",Float)=1.7778
  _Strength("Sky reflection",Float)=.5
  _Spec("Sun or moon glint",Float)=1
  _Rough("Blur of the reflection (mip)",Float)=3.5
  _Radius("Fade radius (inner, outer)",Vector)=(6.5,10.5,0,0)
  _Patch("Wet patches (0 = even, 1 = puddles)",Range(0,1))=.6
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent"}
  Blend One One ZWrite Off Cull Off Offset -1,-1
  Pass
  {
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _MainTex_ST,_Radius;
   float _Arc,_Center,_GroundV,_Lift,_Aspect,_Strength,_Spec,_Rough,_Patch;
   CBUFFER_END
   struct A{float4 p:POSITION;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);return o;}
   float Tri(float t){float f=frac(t*.5)*2;return f<=1?f:2-f;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+float2(1,1)),f.x),f.y);}
   half4 frag(V i):SV_Target
   {
    float3 view=normalize(GetCameraPositionWS()-i.world);
    // 잔물결처럼 살짝 흔들린 바닥면
    float n1=noise(i.world.xz*1.7),n2=noise(i.world.xz*5.3+17);
    float3 normal=normalize(float3((n1-.5)*.10+(n2-.5)*.05,1,(n2-.5)*.10+(n1-.5)*.05));
    float3 r=reflect(-view,normal);
    float azimuth=degrees(atan2(r.x,r.z))-_Center;
    float elevation=degrees(asin(clamp(r.y,-1,1)))-_Lift;
    float2 uv=float2(clamp(Tri(azimuth/_Arc+.5),.001,.999),clamp(_GroundV+elevation/(_Arc/_Aspect),.002,.998));
    half3 sky=SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,uv,_Rough).rgb;
    float fresnel=pow(1-saturate(view.y),4);   // 낮은 각도로 볼수록 잘 비친다
    Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
    float glint=pow(saturate(dot(r,light.direction)),90)*light.shadowAttenuation;
    float wet=lerp(1,smoothstep(.15,.85,noise(i.world.xz*.33+5)*.6+noise(i.world.xz*1.4+11)*.4),_Patch);
    float fade=1-smoothstep(_Radius.x,_Radius.y,length(i.world.xz));
    half3 col=(sky*fresnel*_Strength*lerp(.5,1,light.shadowAttenuation)+light.color*glint*_Spec)*wet*fade;
    return half4(col,1);
   }
   ENDHLSL
  }
 }
}
