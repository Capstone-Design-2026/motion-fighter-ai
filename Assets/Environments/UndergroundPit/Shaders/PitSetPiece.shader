// Painted set pieces of the Underground Pit (walls, galleries, gate, props).
// Unlit picture with soft edges. Bright saturated texels (flames, lamps, lantern glass) glow and flicker,
// and each layer is mixed with the cave haze so far layers sit back.
Shader "VisionStriker/Pit/Set Piece"
{
    Properties
    {
        _MainTex("Picture", 2D) = "white" {}
        _Tint("Tint", Color) = (1,1,1,1)
        _FogColor("Haze colour", Color) = (.10,.07,.05,1)
        _Fog("Haze amount", Range(0,1)) = 0
        _BaseFog("Extra haze at the base", Range(0,1)) = 0
        _BaseFogHeight("Base haze height (m)", Float) = 2
        _TopShade("Darken toward the top", Range(0,1)) = 0
        _TopShadeFrom("Top shade starts at (m)", Float) = 3
        _TopShadeTo("Top shade is full at (m)", Float) = 9
        _Glow("Glow of flames and lamps", Range(0,6)) = 1.2
        _GlowThreshold("Glow threshold", Range(0,1)) = .6
        _Flicker("Flicker", Range(0,1)) = .3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint, _FogColor;
                half _Fog, _BaseFog, _BaseFogHeight, _TopShade, _TopShadeFrom, _TopShadeTo, _Glow, _GlowThreshold, _Flicker;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                c.rgb *= _Tint.rgb;
                half peak = max(c.r, max(c.g, c.b)), low = min(c.r, min(c.g, c.b));
                half saturation = (peak - low) / max(peak, .001);
                half lit = saturate((peak - _GlowThreshold) / max(1 - _GlowThreshold, .001));
                half glow = lit * lit * smoothstep(.22, .55, saturation);
                // each torch flickers on its own: the phase comes from where it stands
                float2 cell = floor(i.positionWS.xz * .7 + i.positionWS.y * .31);
                float phase = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453) * 6.2832;
                float t = _Time.y;
                half flick = 1 - _Flicker * (.5 + .5 * sin(t * 9.3 + phase) * sin(t * 5.1 + phase * 1.7));
                half shade = 1 - _TopShade * smoothstep(_TopShadeFrom, _TopShadeTo, i.positionWS.y);
                half haze = saturate(_Fog + _BaseFog * saturate(1 - i.positionWS.y / max(_BaseFogHeight, .01)));
                half3 colour = lerp(c.rgb * shade, _FogColor.rgb, haze * (1 - glow * .75));
                colour += c.rgb * glow * _Glow * flick;
                return half4(colour, c.a * _Tint.a);
            }
            ENDHLSL
        }
    }
}
