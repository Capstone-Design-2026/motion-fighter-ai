Shader "VisionStriker/Continuous Ground"
{
 Properties
 {
  _BaseMap("Albedo",2D)="white"{}
  _BaseColor("Tint",Color)=(1,1,1,1)
  _BumpMap("Normal",2D)="bump"{}
  _BumpScale("Normal scale",Float)=0.5
  _Smoothness("Smoothness",Range(0,1))=0.05
  _Metallic("Metallic",Range(0,1))=0
  _Cutoff("Alpha Cutoff",Range(0,1))=0.5
  _SpecColor("Specular",Color)=(0.1,0.1,0.1,1)
  _EmissionColor("Emission",Color)=(0,0,0,0)
  _EmissionMap("Emission",2D)="white"{}
  _OcclusionMap("Occlusion",2D)="white"{}
  _OcclusionStrength("Occlusion Strength",Float)=1
  _MetallicGlossMap("Metallic",2D)="white"{}
  _SpecGlossMap("Specular",2D)="white"{}
  _WindStrength("Wind Strength",Float)=0.055
  _WindHeight("Tree height",Float)=6.4
  [HideInInspector]_Cull("Cull",Float)=2
 }
 SubShader
 {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
  Pass
  {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   Cull [_Cull]
   HLSLPROGRAM
   #pragma vertex LitPassVertex
   #pragma fragment TerrainFragment
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #pragma multi_compile_instancing
   #define _NORMALMAP 1
   #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR 1
   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
   float hash21(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise2(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);}
   void TerrainFragment(Varyings input,out half4 outColor:SV_Target0)
   {
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SurfaceData surfaceData;InitializeStandardLitSurfaceData(input.uv,surfaceData);
    float2 p=input.positionWS.xz;
    float macro=noise2(p*.09)*.5+noise2(p*.33+4)*.5;
    surfaceData.albedo*=lerp(.86,1.10,macro);
    float rough=SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,input.uv).r;
    surfaceData.smoothness=_Smoothness*(1-rough*.7);
    surfaceData.occlusion=lerp(.8,1,rough);
    InputData inputData;InitializeInputData(input,surfaceData.normalTS,inputData);
    InitializeBakedGIData(input,inputData);
    half4 color=UniversalFragmentPBR(inputData,surfaceData);
    color.rgb=MixFog(color.rgb,inputData.fogCoord);outColor=half4(color.rgb,1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
