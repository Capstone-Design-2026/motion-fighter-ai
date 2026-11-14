// Flagstones that pick up the arena ground: dust on sides/base, dirt patches on top,
// and a hue shift toward the ground colour underneath. Ground is sampled in world XZ
// from the same texture the floor overlay uses, so stones and floor share one palette.
Shader "VisionStriker/Flagstone Ground Blend"
{
    Properties
    {
        _BaseMap ("Stone Albedo", 2D) = "white" {}
        _BaseColor ("Stone Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Stone Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 0.7
        _Smoothness ("Smoothness", Range(0,1)) = 0.1

        [Header(Ground Blend)]
        _GroundMap ("Ground Texture (floor overlay)", 2D) = "white" {}
        _GroundRect ("Ground Rect (minX, minZ, sizeX, sizeZ)", Vector) = (-20, -16.5, 40, 40)
        _GroundBoost ("Ground Brightness", Float) = 1.3
        _GroundTintAmount ("Hue Match To Ground", Range(0,1)) = 0.35
        _DustAmount ("Dirt Patches On Top", Range(0,1)) = 0.4
        _EdgeDust ("Dust On Sides", Range(0,1)) = 0.9
        _BaseHeight ("Dust Creep Height (m)", Float) = 0.06
        _PlateauY ("Ground Y (m)", Float) = 0
        _NoiseScale ("Dirt Noise Scale", Float) = 1.4
        _EdgeDarken ("Grime In Low Spots", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _BumpScale;
            half   _Smoothness;
            float4 _GroundMap_ST;
            float4 _GroundRect;
            half   _GroundBoost;
            half   _GroundTintAmount;
            half   _DustAmount;
            half   _EdgeDust;
            float  _BaseHeight;
            float  _PlateauY;
            float  _NoiseScale;
            half   _EdgeDarken;
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
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);   SAMPLER(sampler_BumpMap);
            TEXTURE2D(_GroundMap); SAMPLER(sampler_GroundMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS   = n.normalWS;
                o.tangentWS  = float4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                o.uv         = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fogFactor  = ComputeFogFactor(p.positionCS.z);
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
                half3 stone = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;

                float2 guv     = (i.positionWS.xz - _GroundRect.xy) / _GroundRect.zw;
                half3 ground   = SAMPLE_TEXTURE2D(_GroundMap, sampler_GroundMap, guv).rgb * _GroundBoost;
                half3 groundLo = SAMPLE_TEXTURE2D_LOD(_GroundMap, sampler_GroundMap, guv, 7).rgb * _GroundBoost;

                // shift the stone's hue toward the local ground colour (keeps stone brightness)
                half  gl = max(dot(groundLo, half3(0.333, 0.333, 0.333)), 0.05);
                stone = lerp(stone, stone * (groundLo / gl), _GroundTintAmount);

                float3 N = normalize(i.normalWS);
                float side = 1.0 - saturate(N.y * 1.3);                                   // vertical faces
                float low  = 1.0 - saturate((i.positionWS.y - _PlateauY) / _BaseHeight);  // near the ground
                float nz   = fbm(i.positionWS.xz * _NoiseScale);
                float nzf  = fbm(i.positionWS.xz * _NoiseScale * 6.0 + 17.0);
                float top  = smoothstep(1.0 - _DustAmount, 1.0 - _DustAmount + 0.22, nz);

                float dust = saturate(side * _EdgeDust + low * 0.85 + top * 0.85);
                dust = saturate(dust * (0.7 + 0.6 * nzf));

                half3 albedo = lerp(stone, ground, dust);
                albedo *= 1.0 - _EdgeDarken * low * (1.0 - side * 0.5);                     // grime where stone meets dirt

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale * (1.0 - dust * 0.8));
                float3 bitangent = i.tangentWS.w * cross(N, i.tangentWS.xyz);
                float3 nWS = normalize(TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bitangent, N)));

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(nWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1,1,1,1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.metallic = 0;
                s.specular = 0;
                s.smoothness = _Smoothness * (1.0 - dust);
                s.occlusion = 1;
                s.normalTS = nTS;

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);
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
