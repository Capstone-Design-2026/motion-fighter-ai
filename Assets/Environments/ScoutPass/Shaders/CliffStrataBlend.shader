// Cliff / boulder shader for the ScoutPass plateau.
// World-space triplanar layered rock (no UV stretching), moss and earth on upward faces,
// and a fade into the cloud-sea colour at the bottom so the cliff sinks into mist.
Shader "VisionStriker/Cliff Strata Blend"
{
    Properties
    {
        _RockMap ("Rock Albedo (tileable)", 2D) = "white" {}
        [Normal] _RockNormal ("Rock Normal", 2D) = "bump" {}
        _RockTint ("Rock Tint", Color) = (1,1,1,1)
        _Scale ("Texture Size (m)", Float) = 9
        _DetailScale ("Detail Size (m)", Float) = 2.2
        _NormalStrength ("Normal Strength", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.06

        [Header(Top Cover)]
        _MossColor ("Moss Colour", Color) = (0.30,0.33,0.15,1)
        _GrassColor ("Dry Grass Colour", Color) = (0.50,0.44,0.22,1)
        _EarthColor ("Earth Colour", Color) = (0.46,0.33,0.20,1)
        _TopAmount ("Top Cover Amount", Range(0,1)) = 0.8
        _TopSharp ("Top Cover Sharpness", Range(0.05,0.6)) = 0.22
        _TopFadeDepth ("Cover Fades Below Rim By (m)", Float) = 26
        _RimY ("Rim Height (m)", Float) = 0

        [Header(Mist)]
        _MistColor ("Mist Colour", Color) = (0.66,0.62,0.53,1)
        _MistTop ("Mist Starts (y)", Float) = -9
        _MistBottom ("Mist Full (y)", Float) = -21
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _RockMap_ST;
            half4 _RockTint;
            float _Scale;
            float _DetailScale;
            half _NormalStrength;
            half _Smoothness;
            half4 _MossColor;
            half4 _GrassColor;
            half4 _EarthColor;
            half _TopAmount;
            half _TopSharp;
            float _TopFadeDepth;
            float _RimY;
            half4 _MistColor;
            float _MistTop;
            float _MistBottom;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_RockMap);    SAMPLER(sampler_RockMap);
            TEXTURE2D(_RockNormal); SAMPLER(sampler_RockNormal);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1,0)), f.x),
                            lerp(hash21(i + float2(0,1)), hash21(i + float2(1,1)), f.x), f.y);
            }
            float fbm(float2 p) { return 0.5 * vnoise(p) + 0.3 * vnoise(p * 2.13 + 7.1) + 0.2 * vnoise(p * 4.71 + 3.3); }

            half4 frag (Varyings i) : SV_Target
            {
                float3 P = i.positionWS;
                float3 N = normalize(i.normalWS);

                float3 w = pow(abs(N), 4.0);
                w /= (w.x + w.y + w.z);

                float2 uvX = P.zy / _Scale;
                float2 uvZ = P.xy / _Scale;
                float2 uvY = P.xz / _Scale;

                half3 aX = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvX).rgb;
                half3 aZ = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvZ).rgb;
                half3 aY = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvY).rgb;
                half3 rock = aX * w.x + aZ * w.z + aY * w.y;

                // close-up detail so the rock does not blur near the camera
                float ds = _Scale / _DetailScale;
                half3 dX = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvX * ds + 0.37).rgb;
                half3 dZ = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvZ * ds + 0.37).rgb;
                half3 det = dX * w.x + dZ * w.z + aY * w.y;
                rock *= lerp(half3(1,1,1), det / 0.55, 0.45);

                // large-scale tone variation + vertical weathering streaks
                float macro = fbm(float2(P.x + P.z, P.y * 0.35) * 0.09);
                rock *= lerp(0.78, 1.14, macro);
                float streak = fbm(float2((P.x - P.z) * 0.9, P.y * 0.06));
                rock *= lerp(1.0, 0.8, smoothstep(0.55, 0.8, streak) * (1.0 - w.y));
                rock *= _RockTint.rgb;

                // moss / dry grass / earth on faces that look up
                float nz  = fbm(P.xz * 0.55);
                float nz2 = fbm(P.xz * 2.3 + 11.0);
                float up  = smoothstep(1.0 - _TopSharp * 2.0, 1.0 - _TopSharp * 0.6, N.y + (nz2 - 0.5) * 0.35);
                float depthFade = saturate(1.0 - (_RimY - P.y) / _TopFadeDepth);
                float cover = up * _TopAmount * lerp(0.35, 1.0, depthFade);
                half3 veg = lerp(_MossColor.rgb, _GrassColor.rgb, smoothstep(0.35, 0.7, nz));
                veg = lerp(veg, _EarthColor.rgb, smoothstep(0.55, 0.85, nz2) * 0.7);
                veg *= lerp(0.8, 1.15, nz2);
                half3 albedo = lerp(rock, veg, cover);

                // normals
                half3 tX = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvX), _NormalStrength);
                half3 tZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvZ), _NormalStrength);
                half3 tY = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvY), _NormalStrength);
                float3 nWS = N + (float3(0, tX.y, tX.x) * w.x + float3(tZ.x, tZ.y, 0) * w.z + float3(tY.x, 0, tY.y) * w.y) * (1.0 - cover * 0.8);
                nWS = normalize(nWS);

                InputData d = (InputData)0;
                d.positionWS = P;
                d.positionCS = i.positionCS;
                d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
                d.shadowCoord = TransformWorldToShadowCoord(P);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(nWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1,1,1,1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.smoothness = _Smoothness * (1.0 - cover);
                s.occlusion = 1;
                s.normalTS = half3(0,0,1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);

                // sink into the cloud sea
                float mist = smoothstep(_MistTop, _MistBottom, P.y);
                float mn = fbm(float2(P.x + P.z, P.y * 2.0) * 0.12);
                mist = saturate(mist * (0.75 + 0.6 * mn));
                c.rgb = lerp(c.rgb, _MistColor.rgb, mist);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex sv
            #pragma fragment sf
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 sv (A i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 ws  = TransformObjectToWorld(i.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - ws);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 sf () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex dv
            #pragma fragment df
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 dv (A i) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); return TransformObjectToHClip(i.positionOS.xyz); }
            half df () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex nv
            #pragma fragment nf
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V nv (A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 nf (V i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
