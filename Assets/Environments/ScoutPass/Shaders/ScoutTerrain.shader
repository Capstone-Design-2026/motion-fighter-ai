Shader "VisionStriker/Scout Terrain"
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
    float n=noise2(p*1.4)*.50+noise2(p*4.2)*.18;
    float center=p.y<19.5?lerp(.3,1.45,smoothstep(3,19.5,p.y)):1.45+sin((p.y-19.5)*.16)*4.2;
    float width=lerp(3.7,1.3,saturate((p.y-3.5)/36));
    float trail=1-smoothstep(width-.6,width+.65,abs(p.x-center)+n-.3);
    float lane=(1-smoothstep(2.4,4.5,abs(p.y)+n))*(1-smoothstep(7.7,10.5,abs(p.x)+n));
    float path=max(trail,lane);
    float large=noise2(p*.22);
    // Disturbed road, soft moss islands, and paired convoy wheel ruts.
    float islands=smoothstep(.36,.67,noise2(p*.48+7))*smoothstep(1.3,4.6,abs(p.y));
    path*=1-islands*.8;
    half3 moss=lerp(half3(.28,.34,.18),half3(.52,.55,.33),large);
    half3 soil=lerp(half3(.64,.59,.48),half3(.89,.83,.69),large);
    surfaceData.albedo*=lerp(moss,soil,path);
    float rutDistance=abs(abs(p.x-center)-.68);
    float rut=(1-smoothstep(.045,.13,rutDistance+noise2(p*7)*.035))*smoothstep(1.8,4,p.y)*(1-smoothstep(22,28,p.y));
    surfaceData.albedo*=1-rut*.28;
    float gravel=smoothstep(.65,.85,noise2(p*11))*smoothstep(.48,.7,noise2(p*.8));
    surfaceData.albedo=lerp(surfaceData.albedo,surfaceData.albedo*.76+half3(.06,.065,.06),gravel*.45);
    float scuff=(1-smoothstep(.02,.07,abs(sin(p.x*3.7+p.y*.5))))*noise2(p*2.8)*lane;
    surfaceData.albedo*=1-scuff*.09;
    surfaceData.smoothness=.03;
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
