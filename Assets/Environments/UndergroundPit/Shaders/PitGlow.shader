// Additive light sprites of the Underground Pit: flame glows, lamp halos, the light shaft, embers, dust and fire particles.
// With Billboard on, a quad always faces the camera (for single glows). Particle systems leave it off and bring their own colours.
Shader "VisionStriker/Pit/Glow"
{
    Properties
    {
        _MainTex("Shape", 2D) = "white" {}
        [HDR] _Color("Colour", Color) = (1,.6,.3,1)
        _Flicker("Flicker", Range(0,1)) = 0
        _FlickerSpeed("Flicker speed", Float) = 1
        [Toggle] _Billboard("Face the camera", Float) = 0
        _Drift("Slow shimmer (shaft)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+60" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Flicker, _FlickerSpeed, _Billboard, _Drift;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float phase : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                if (_Billboard > .5)
                {
                    float2 size = float2(length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                                         length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)));
                    float3 positionVS = TransformWorldToView(centre) + float3(a.positionOS.xy * size, 0);
                    o.positionCS = TransformWViewToHClip(positionVS);
                }
                else o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                o.color = a.color;
                o.phase = frac(sin(dot(centre.xz + centre.y, float2(12.9898, 78.233))) * 43758.5453) * 6.2832;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float t = _Time.y * _FlickerSpeed;
                half flick = 1 - _Flicker * (.5 + .5 * sin(t * 9.3 + i.phase) * sin(t * 5.1 + i.phase * 1.7));
                half drift = 1 - _Drift * (.5 + .5 * sin(_Time.y * .35 + i.uv.x * 9 + i.phase));
                half3 colour = s.rgb * _Color.rgb * i.color.rgb * (s.a * _Color.a * i.color.a * flick * drift);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
