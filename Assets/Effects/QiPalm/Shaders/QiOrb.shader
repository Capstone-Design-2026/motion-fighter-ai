// 파운장 이펙트: 기운 구슬과 기파 몸체. 가장자리가 밝게 빛나고(프레넬), 두 겹 노이즈가 흐르며 표면이 출렁인다.
Shader "VisionStriker/QiOrb"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
        [HDR] _CoreColor ("Core Color", Color) = (1.6, 2.2, 3.0, 1)
        [HDR] _RimColor ("Rim Color", Color) = (0.25, 0.9, 3.0, 1)
        _RimPower ("Rim Power", Float) = 2.2
        _NoiseScale ("Noise Scale", Float) = 1.6
        _Speed ("Flow Speed (xy, zw)", Vector) = (0.35, 0.9, -0.6, 0.4)
        _Wobble ("Wobble", Float) = 0.06
        _Alpha ("Alpha", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Back
        Pass
        {
            Name "QiOrb"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half4 _CoreColor, _RimColor;
                half _RimPower, _NoiseScale, _Wobble, _Alpha;
                float4 _Speed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 viewWS : TEXCOORD2; };
            Varyings vert (Attributes i)
            {
                Varyings o;
                float t = _Time.y;
                float n = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, i.uv * _NoiseScale + t * _Speed.xy, 0).r;
                float3 p = i.positionOS.xyz + i.normalOS * (n - 0.5) * _Wobble * 2.0;
                float3 ws = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                o.uv = i.uv;
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                half n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv * _NoiseScale + t * _Speed.xy).r;
                half n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv * _NoiseScale * 2.3 + t * _Speed.zw).r;
                half fres = pow(1.0 - saturate(dot(normalize(i.normalWS), normalize(i.viewWS))), _RimPower);
                half swirl = saturate(n1 * n2 * 2.2);
                half3 core = _CoreColor.rgb * (0.35 + 0.9 * swirl) * (1.0 - fres * 0.6);
                half3 rim = _RimColor.rgb * fres * (0.7 + 0.9 * n2);
                return half4((core + rim) * _Alpha, _Alpha);
            }
            ENDHLSL
        }
    }
}
