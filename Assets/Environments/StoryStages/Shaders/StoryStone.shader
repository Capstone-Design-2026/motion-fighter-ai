Shader "VisionStriker/Story Stone"
{
 Properties {_BaseMap("Stone",2D)="white"{} _BaseColor("Tint",Color)=(.65,.65,.65,1) _Tile("Texture scale",Float)=.4 _Blocks("Masonry joints",Float)=1 }
 SubShader {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
  Pass {
   Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST,_BaseColor;float _Tile,_Blocks;
   CBUFFER_END
   struct A{float4 p:POSITION;float3 n:NORMAL;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float fog:TEXCOORD2;};
   V vert(A a){V o; o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.fog=ComputeFogFactor(o.p.z);return o;}
   float hash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
   float noise(float3 p){float3 b=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(lerp(hash(b),hash(b+float3(1,0,0)),f.x),lerp(hash(b+float3(0,1,0)),hash(b+float3(1,1,0)),f.x),f.y),
    lerp(lerp(hash(b+float3(0,0,1)),hash(b+float3(1,0,1)),f.x),lerp(hash(b+float3(0,1,1)),hash(b+1),f.x),f.y),f.z);}
   float block(float2 p){float row=floor(p.y*1.5);float2 f=frac(float2(p.x*.75+fmod(row,2)*.5,p.y*1.5));float edge=min(min(f.x,1-f.x),min(f.y,1-f.y));return smoothstep(.01,.035,edge);}
   half4 frag(V i):SV_Target{
    float3 n=normalize(i.n),w=pow(abs(n),4);w/=max(dot(w,1),.001);float3 p=i.world*_Tile;
    float2 a=p.zy;
    float2 b=p.xz;
    float2 c=p.xy;
    half3 sampleColor=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,a).rgb*w.x+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,b).rgb*w.y+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,c).rgb*w.z;
    float textureDetail=saturate(dot(sampleColor,float3(.2126,.7152,.0722)));
    float weather=noise(i.world*.6),grain=noise(i.world*24);
    half3 tex=sampleColor*(.85+weather*.3)+half3(.035,.04,.035)+(grain-.5)*.035;
    float joint=block(i.world.zy)*w.x+block(i.world.xz)*w.y+block(i.world.xy)*w.z;
    tex*=lerp(1,lerp(.55,1,joint),_Blocks);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
    half3 lighting=SampleSH(n)+light.color*(saturate(dot(n,light.direction))*.8+.08)*light.shadowAttenuation;
    half3 col=tex*_BaseColor.rgb*lighting;
    return half4(MixFog(col,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
