Shader "VisionStriker/Scout Pine Wind"
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
   #pragma vertex PineVertex
   #pragma fragment LitPassFragment
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #pragma multi_compile_instancing
   #define _NORMALMAP 1
   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
   float _WindStrength, _WindHeight;
   Varyings PineVertex(Attributes input)
   {
    UNITY_SETUP_INSTANCE_ID(input);
    float3 world=TransformObjectToWorld(input.positionOS.xyz);
    float weight=saturate((world.y-unity_ObjectToWorld._m13)/max(.1,_WindHeight));
    float phase=unity_ObjectToWorld._m03*.4+unity_ObjectToWorld._m23*.25;
    world.x+=sin(_Time.y*.8+world.y*.8+phase)*_WindStrength*weight*weight;
    world.z+=cos(_Time.y*.65+world.x*.6+phase)*_WindStrength*.4*weight;
    input.positionOS.xyz=TransformWorldToObject(world);
    return LitPassVertex(input);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
