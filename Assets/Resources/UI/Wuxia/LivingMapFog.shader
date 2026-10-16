Shader "VisionStriker/UI/LivingMapFog"
{
 Properties {
  [PerRendererData] _MainTex("Texture",2D)="white"{}
  _FlowTime("Time",Float)=0 _DriftSpeed("Drift",Float)=22 _Ambient("Ambient",Float)=0
  _StencilComp("Stencil Comparison",Float)=8 _Stencil("Stencil ID",Float)=0
  _StencilOp("Stencil Operation",Float)=0 _StencilWriteMask("Stencil Write Mask",Float)=255
  _StencilReadMask("Stencil Read Mask",Float)=255 _ColorMask("Color Mask",Float)=15
  [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Alpha Clip",Float)=0
 }
 SubShader {
  Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="False"}
  Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
   struct v2f {float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 world:TEXCOORD1;};
   float4 _ClipRect; float _FlowTime,_DriftSpeed,_Ambient;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;o.uv=v.uv;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   fixed4 frag(v2f i):SV_Target {
    float2 p=i.uv*float2(4200,2800);float t=_FlowTime*_DriftSpeed;
    float bank=noise((p-float2(t,t*.17))*.004);
    float billow=noise((p+float2(t*.62,-t*.26))*.011+37);
    float wisps=noise((p-float2(t*1.2,-t*.4))*.025+73);
    float cloud=smoothstep(.24,.78,bank*.52+billow*.34+wisps*.14);
    float3 rgb=lerp(float3(.86,.89,.86),float3(.98,.965,.925),cloud);
    float alpha=i.color.a*lerp(lerp(.79,.94,cloud),cloud*.19,_Ambient);
    #ifdef UNITY_UI_CLIP_RECT
    alpha*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    #ifdef UNITY_UI_ALPHACLIP
    clip(alpha-.001);
    #endif
    return fixed4(rgb,alpha);
   }
   ENDCG
  }
 }
}
