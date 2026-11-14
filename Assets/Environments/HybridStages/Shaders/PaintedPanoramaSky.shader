Shader "VisionStriker/Painted Panorama Sky"
{
 // 원경만 그린 배경 그림을 하늘에 두른다. 그림은 무대 뒤쪽(+Z)을 중심으로 _Arc 도를 덮고, 그 밖은 거울처럼 이어진다.
 // 3D 사물과 바닥은 그대로 두고 하늘과 먼 산만 그림으로 바꾸는 데 쓴다.
 Properties
 {
  _MainTex("Backdrop painting",2D)="white"{}
  _Arc("Azimuth covered by the painting (deg)",Float)=110
  _Center("Azimuth of the painting centre (deg, 0 = +Z)",Float)=0
  _GroundV("Painting height at eye level (0 bottom, 1 top)",Float)=.4
  _Lift("Raise the painting (deg)",Float)=0
  _Aspect("Painting width / height",Float)=1.7778
  _Zenith("Colour above the painting",Color)=(.2,.3,.5,1)
  _Below("Colour below the painting",Color)=(.5,.5,.5,1)
  _Exposure("Brightness",Float)=1
  _Glow("Extra light from the brightest parts (sun, moon, lit clouds)",Float)=0
 }
 SubShader
 {
  Tags{"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _MainTex_ST,_Zenith,_Below;
   float _Arc,_Center,_GroundV,_Lift,_Aspect,_Exposure,_Glow;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;};
   struct V{float4 positionCS:SV_POSITION;float3 dir:TEXCOORD0;};
   V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.dir=i.positionOS.xyz;return o;}
   // 0→0, 1→1, 2→0 으로 접히는 삼각파 (거울 반복)
   float Tri(float t){float f=frac(t*.5)*2;return f<=1?f:2-f;}
   half4 frag(V i):SV_Target
   {
    float3 d=normalize(i.dir);
    float azimuth=degrees(atan2(d.x,d.z))-_Center;
    float elevation=degrees(asin(clamp(d.y,-1,1)))-_Lift;
    float u=Tri(azimuth/_Arc+.5);
    float v=_GroundV+elevation/(_Arc/_Aspect);
    float2 uv=float2(clamp(u,.001,.999),clamp(v,.002,.998));
    // 확대해서 보는 그림이라 밉맵 없이 읽는다 (방위각이 뒤집히는 선에서 줄이 생기지 않게).
    half3 col=SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,uv,0).rgb;
    half3 softTop=SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,float2(uv.x,.98),6.5).rgb;
    half3 softBottom=SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,float2(uv.x,.02),6.5).rgb;
    col=lerp(col,softTop,smoothstep(0,.05,v-1));
    col=lerp(col,_Zenith.rgb,smoothstep(.04,.55,v-1));
    col=lerp(col,softBottom,smoothstep(0,.05,-v));
    col=lerp(col,_Below.rgb,smoothstep(.04,.4,-v));
    // 가장 밝은 곳은 1을 넘겨 빛 번짐이 걸리게 한다.
    half luma=dot(col,half3(.3,.59,.11));
    col+=col*_Glow*smoothstep(.70,1,luma);
    return half4(col*_Exposure,1);
   }
   ENDHLSL
  }
 }
}
