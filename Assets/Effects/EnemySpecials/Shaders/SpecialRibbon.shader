// 적 필살기 이펙트(게임과 미리보기 도구가 함께 쓴다): 빛나는 띠·고리·불티·부적 문양용 가산 혼합 셰이더.
// 가운데가 하얗게 달아오르고(_Hot), 흐르는 노이즈가 띠를 갉아먹어(_Erode) 기운이 흐르는 것처럼 보인다.
// _Soft 를 켜면 바닥이나 벽에 닿는 곳이 칼로 자른 듯 끊기지 않고 부드럽게 사라진다.
Shader "VisionStriker/EnemySpecial/Ribbon"
{
    Properties
    {
        _MainTex ("Profile", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Flow ("Noise speed (xy), noise tiling (zw)", Vector) = (0, 0, 1, 1)
        _Erode ("Erode", Range(0, 1)) = 0
        _Hot ("White core", Range(0, 1)) = 0
        _Soft ("Depth fade per metre (0 = off)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Name "PreviewRibbon"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NoiseTex_ST;
                half4 _Color;
                half _Intensity;
                float4 _Flow;
                half _Erode;
                half _Hot;
                half _Soft;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 noiseUV : TEXCOORD1; float eyeDepth : TEXCOORD2; half4 color : COLOR; };
            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.noiseUV = i.uv * _Flow.zw + _Time.y * _Flow.xy;
                o.eyeDepth = -TransformWorldToView(ws).z;
                o.color = i.color;
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.noiseUV).r;
                half a = t.a * i.color.a * _Color.a;
                a *= saturate(lerp(1.0, n * 2.4 - 0.5, _Erode));
                if (_Soft > 0.0)
                {
                    float scene = LinearEyeDepth(SampleSceneDepth(i.positionCS.xy / _ScaledScreenParams.xy), _ZBufferParams);
                    a *= saturate((scene - i.eyeDepth) * _Soft);
                }
                half3 c = t.rgb * i.color.rgb * _Color.rgb;
                half peak = max(c.r, max(c.g, c.b));
                c = lerp(c, half3(peak, peak, peak), _Hot * t.a * t.a * t.a);
                return half4(c * (_Intensity * a), a);
            }
            ENDHLSL
        }
    }
}
