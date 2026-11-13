Shader "VisionStriker/Hybrid Leaf"
{
 // 흩날리는 꽃잎·잎·재 입자. 주광 색을 살짝 받아 무대 빛과 어울리게 한다.
 Properties
 {
  _MainTex("Leaf",2D)="white"{}
  _BaseColor("Tint",Color)=(1,1,1,1)
  _Lit("How much the key light tints it",Range(0,1))=.6
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _MainTex_ST;half4 _BaseColor;float _Lit;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   V vert(A a)
   {
    V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;
    Light light=GetMainLight();
    half3 lit=lerp(half3(1,1,1),light.color*.9+half3(.25,.25,.30),_Lit);
    o.color=a.color*_BaseColor;o.color.rgb*=lit;return o;
   }
   half4 frag(V i):SV_Target
   {
    half4 t=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
    return half4(t.rgb*i.color.rgb,t.a*i.color.a);
   }
   ENDHLSL
  }
 }
}
