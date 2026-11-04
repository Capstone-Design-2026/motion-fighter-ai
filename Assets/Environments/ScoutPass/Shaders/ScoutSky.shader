Shader "VisionStriker/Scout 360 Sky"
{
 Properties
 {
  _CloudTex("Separated clouds",2D)="black"{}
  _SkyTop("Zenith",Color)=(.24,.41,.61,1)
  _SkyHorizon("Horizon",Color)=(.70,.77,.79,1)
  _CloudSpeed("Cloud drift",Float)=.0035
  _CloudStrength("Cloud opacity",Float)=.8
 }
 SubShader
 {
  Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_CloudTex);SAMPLER(sampler_CloudTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _SkyTop,_SkyHorizon;float _CloudSpeed,_CloudStrength;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;};
   struct V{float4 positionCS:SV_POSITION;float3 dir:TEXCOORD0;};
   V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.dir=i.positionOS.xyz;return o;}
   float4 cloud(float2 uv)
   {
    uv.x=frac(uv.x);float mask=smoothstep(0,.035,uv.x)*(1-smoothstep(.965,1,uv.x))*smoothstep(0,.04,uv.y)*(1-smoothstep(.92,1,uv.y));
    float4 c=SAMPLE_TEXTURE2D(_CloudTex,sampler_CloudTex,uv);c.a*=mask;c.rgb=lerp(c.rgb,dot(c.rgb,float3(.2126,.7152,.0722)).xxx,.48);return c;
   }
   half4 frag(V i):SV_Target
   {
    float3 d=normalize(i.dir);float h=saturate(d.y);
    float3 color=lerp(_SkyHorizon.rgb,_SkyTop.rgb,pow(h,.45));
    float longitude=atan2(d.x,d.z)/6.2831853+.5;
    float elevation=asin(clamp(d.y,-1,1))/1.5707963;
    float t=_Time.y*_CloudSpeed;
    float4 a=cloud(float2(longitude*3-t,elevation*1.7+.12));
    float4 b=cloud(float2(longitude*4+t*.31+.28,elevation*2.4-.22));
    float horizon=smoothstep(-.015,.06,d.y);
    color=lerp(color,a.rgb,saturate(a.a*_CloudStrength)*horizon);
    color=lerp(color,b.rgb,saturate(b.a*_CloudStrength*.32)*horizon);
    return half4(color,1);
   }
   ENDHLSL
  }
 }
}
