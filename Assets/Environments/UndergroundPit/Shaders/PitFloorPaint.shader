// Worn paint on the pit floor. Drawn over the lit floor with a 2x multiply, so the paint takes the same light,
// shadows and torch flicker as the stone under it. In the picture 0.5 means "no paint" (linear texture).
Shader "VisionStriker/Pit/Floor Paint"
{
    Properties
    {
        _MainTex("Paint (0.5 = none)", 2D) = "gray" {}
        _Strength("Strength", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend DstColor SrcColor
        ZWrite Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Strength;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.uv = TRANSFORM_TEX(a.uv, _MainTex); return o; }
            half4 frag(V i) : SV_Target
            {
                half3 paint = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                return half4(lerp(half3(.5, .5, .5), paint, _Strength), 1);
            }
            ENDHLSL
        }
    }
}
