Shader "VisionStriker/Hybrid Light Shaft"
{
 // 해·달이 있는 쪽에서 비스듬히 내려오는 빛살. 가장자리가 부드러운 긴 판에 빛만 더한다.
 Properties
 {
  _BaseColor("Colour",Color)=(1,.9,.7,1)
  _Strength("Strength",Float)=.1
  _Speed("Drift",Float)=.03
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent"}
  Blend One One ZWrite Off Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;float _Strength,_Speed;
   CBUFFER_END
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+float2(1,1)),f.x),f.y);}
   half4 frag(V i):SV_Target
   {
    float across=sin(saturate(i.uv.x)*3.14159);across=across*across*across;
    float along=smoothstep(0,.25,i.uv.y)*(1-smoothstep(.55,1,i.uv.y));
    float streaks=.55+.45*noise(float2(i.uv.x*4+_Time.y*_Speed,i.uv.y*1.5-_Time.y*_Speed*.5));
    // 카메라가 빛살을 지나갈 때 화면이 번쩍이지 않게 가까우면 사라진다.
    float closeFade=smoothstep(2,8,distance(GetCameraPositionWS(),i.world));
    return half4(_BaseColor.rgb*_Strength*across*along*streaks*closeFade,1);
   }
   ENDHLSL
  }
 }
}
