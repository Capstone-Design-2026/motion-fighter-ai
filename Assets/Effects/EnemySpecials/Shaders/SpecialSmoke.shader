// 적 필살기 이펙트(게임과 미리보기 도구가 함께 쓴다): 연기·안개·먼지·먹구름용 알파 혼합 셰이더.
// 텍스처 R = 위에서 받은 빛(덩어리 안쪽 명암), G = 짙은 정도, A = 모양.
// _Glow 는 번쩍이는 순간 연기 속이 밝아지게 하는 값이고, _Soft 는 바닥에 닿는 곳을 부드럽게 지운다.
Shader "VisionStriker/EnemySpecial/Smoke"
{
    Properties
    {
        _MainTex ("Texture (R shade, G density, A shape)", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        [HDR] _Glow ("Glow", Color) = (0, 0, 0, 0)
        _Shade ("Shade floor", Range(0, 1)) = 0.4
        _Soft ("Depth fade per metre (0 = off)", Float) = 0
        _GroundFade ("Fade in over this height above y = 0 (0 = off)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "PreviewSmoke"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _Glow;
                half _Shade;
                half _Soft;
                half _GroundFade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float eyeDepth : TEXCOORD1; float worldY : TEXCOORD2; half4 color : COLOR; };
            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.eyeDepth = -TransformWorldToView(ws).z;
                o.worldY = ws.y;
                o.color = i.color;
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half a = t.a * i.color.a * _Color.a;
                // 카메라가 낮아 깊이 차이만으로는 바닥과 만나는 선이 날카롭게 남는다. 바닥(y = 0)에서부터 높이에 따라 서서히 나타나게 한다.
                if (_GroundFade > 0.0) a *= saturate(i.worldY / _GroundFade);
                if (_Soft > 0.0)
                {
                    float scene = LinearEyeDepth(SampleSceneDepth(i.positionCS.xy / _ScaledScreenParams.xy), _ZBufferParams);
                    a *= saturate((scene - i.eyeDepth) * _Soft);
                }
                half3 c = i.color.rgb * _Color.rgb * lerp(_Shade, 1.0, t.r) + _Glow.rgb * (0.25 + 0.75 * t.g);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
