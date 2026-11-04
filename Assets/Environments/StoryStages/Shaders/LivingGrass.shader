Shader "VisionStriker/Living Grass"
{
 Properties {_BaseColor("Leaf color",Color)=(.23,.31,.17,1)}
 SubShader{
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Off
  Pass{
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;
   CBUFFER_END
   struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float2 uv:TEXCOORD1;float fog:TEXCOORD2;float shade:TEXCOORD3;};
   V vert(A a){V o;float3 p=TransformObjectToWorld(a.p.xyz);p.x+=sin(p.z*.7+p.x*.3+_Time.y*1.8)*.075*a.uv.y*a.uv.y;o.p=TransformWorldToHClip(p);o.n=TransformObjectToWorldNormal(a.n);o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);o.shade=.88+sin(p.x*3.1+p.z*7.4)*.12;return o;}
   half4 frag(V i):SV_Target{Light l=GetMainLight();float bright=.44+abs(dot(normalize(i.n),l.direction))*.50;float vein=1-abs(i.uv.x-.5)*.35;float3 c=_BaseColor.rgb*lerp(.55,1.45,i.uv.y)*i.shade*vein*(SampleSH(float3(0,1,0))+l.color*bright);return half4(MixFog(c,i.fog),1);}
   ENDHLSL
  }
 }
}
