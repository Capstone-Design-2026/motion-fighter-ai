Shader "VisionStriker/Remake Cutout"
{
 // 새로 그린 구조물(문루, 나무, 천막 등)을 배경을 걷어낸 채 무대 뒤에 세우는 판. 멀수록 안개색이 섞인다.
 // _Glow: 그림 속 불꽃·등롱처럼 밝고 따뜻한 부분을 실제 빛처럼 밝혀 빛 번짐이 생기게 한다.
 // _BaseFog: 바닥에 닿는 밑동 쪽에 안개를 더 섞어 바닥과의 경계를 부드럽게 한다.
 Properties
 {
  _MainTex("Painting",2D)="white"{}
  _Tint("Tint",Color)=(1,1,1,1)
  _FogColor("Haze colour",Color)=(.5,.5,.5,1)
  _Fog("Haze amount",Range(0,1))=0
  _Glow("Painted light glow",Range(0,6))=0
  _BaseFog("Haze at the base",Range(0,1))=0
  _BaseFogHeight("Base haze height (m)",Float)=3
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _MainTex_ST;half4 _Tint,_FogColor;float _Fog,_Glow,_BaseFog,_BaseFogHeight;
   CBUFFER_END
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float wy:TEXCOORD1;};
   V vert(A a){V o;float3 w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(w);o.uv=a.uv;o.wy=w.y;return o;}
   half4 frag(V i):SV_Target
   {
    half4 t=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
    half warm=smoothstep(.72,1,t.r)*smoothstep(.12,.45,t.r-t.b);
    half3 lit=t.rgb*_Tint.rgb*(1+warm*_Glow);
    half low=saturate(1-i.wy/max(_BaseFogHeight,.01));
    half fog=saturate(_Fog+_BaseFog*low*sqrt(low)*(1-warm));
    half3 col=lerp(lit,_FogColor.rgb,fog);
    return half4(col,t.a*_Tint.a);
   }
   ENDHLSL
  }
 }
}
