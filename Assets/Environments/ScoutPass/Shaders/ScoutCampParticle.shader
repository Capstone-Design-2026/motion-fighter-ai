Shader "VisionStriker/Camp Smoke"
{
 Properties { _BaseColor("Color",Color)=(.6,.64,.65,.3) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
   struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
   CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
   V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;o.color=a.color*_BaseColor;return o;}
   half4 frag(V i):SV_Target
   {
    float2 p=(i.uv-.5)*2;float r=length(p);
    float wisps=.76+.13*sin(p.x*11+p.y*7)+.11*sin(p.y*17-p.x*8);
    half alpha=pow(saturate(1-r*r),2.5)*wisps;
    return half4(i.color.rgb,i.color.a*alpha);
   }
   ENDHLSL
  }
 }
}
