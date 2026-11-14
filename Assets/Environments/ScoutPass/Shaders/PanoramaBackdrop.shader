// Unlit backdrop for the painted valley panorama. Ignores scene fog (the painting already
// has its own atmosphere) and fades its top edge into the sky through alpha.
Shader "VisionStriker/Panorama Backdrop"
{
    Properties
    {
        _BaseMap ("Panorama", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Float) = 1
        _HazeColor ("Haze Colour", Color) = (0.75,0.72,0.64,1)
        _Haze ("Haze Amount", Range(0,1)) = 0
        _TopFadeStart ("Top Fade Start (uv.y, 1 = off)", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-200" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Backdrop"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Exposure;
                half4 _HazeColor;
                half _Haze;
                float _TopFadeStart;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert (A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); return o; }

            half4 frag (V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                c.rgb = lerp(c.rgb * _Tint.rgb * _Exposure, _HazeColor.rgb, _Haze);
                float tf = saturate((i.uv.y - _TopFadeStart) / max(1.0 - _TopFadeStart, 0.0001));
                c.a *= 1.0 - smoothstep(0.0, 1.0, tf);
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
