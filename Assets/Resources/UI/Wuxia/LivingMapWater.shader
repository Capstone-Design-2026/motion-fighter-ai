Shader "VisionStriker/UI/LivingMapWater"
{
 Properties {
  [PerRendererData] _MainTex("Map Painting",2D)="white"{} _FlowTime("Time",Float)=0
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
   sampler2D _MainTex;float4 _ClipRect;float _FlowTime;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;o.uv=v.uv;return o;}
   float water(float3 rgb){
    #ifndef UNITY_COLORSPACE_GAMMA
    rgb=LinearToGammaSpace(rgb);
    #endif
    // The original jade water is cooler than the parchment, rocks, trees and buildings.
    return smoothstep(.012,.055,min(rgb.g,rgb.b)-rgb.r)*smoothstep(.25,.48,rgb.g);
   }
   fixed4 frag(v2f i):SV_Target {
    float2 uv=i.uv;float t=_FlowTime*2.0;float3 original=tex2D(_MainTex,uv).rgb;
    float mask=water(original);
    // Left-to-right local meanders; overall flow is from the high fortress to the lower valley.
    float reservoir=step(.77,uv.x)*step(.89,uv.y);
    float falls=step(.83,uv.x)*step(.70,uv.y)*(1-reservoir);
    float2 flow=normalize(float2(sin(uv.y*21+1.2)*.75,-1));
    flow=lerp(flow,float2(-1,-.12),reservoir);flow=lerp(flow,float2(.12,-1),falls);
    float phase=frac(t*.22);float other=frac(phase+.5);
    float2 ripple=float2(sin(uv.y*250-t*2.2),cos(uv.x*240-t*1.7))*.00065;
    float2 a=uv-flow*(phase-.5)*.008+ripple;
    float2 b=uv-flow*(other-.5)*.008+ripple;
    float3 ca=tex2D(_MainTex,a).rgb,cb=tex2D(_MainTex,b).rgb;
    // Both shifted samples must remain in water; shorelines and bridges stay completely still.
    mask*=min(water(ca),water(cb));
    float blend=abs(phase-.5)*2;
    float3 rgb=lerp(ca,cb,blend);
    float glint=pow(saturate(sin(uv.x*280+uv.y*470-t*3.6)),12)*.035;
    rgb+=glint;
    float alpha=mask*.85*i.color.a;
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


