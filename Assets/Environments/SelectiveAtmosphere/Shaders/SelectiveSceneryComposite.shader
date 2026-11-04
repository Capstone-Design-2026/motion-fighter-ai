Shader "Hidden/VisionStriker/SelectiveSceneryComposite"
{
    Properties { _Haze("Haze",Color)=(.52,.55,.53,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D_X(_SelectiveSceneryMask);
            float4 _Treatment;
            float4 _Haze;
            float _ShowMask;
            float Weight(int p) { return p == 0 ? 6 : (abs(p) == 1 ? 4 : 1); }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                // Point sampling keeps fighter silhouettes completely outside the treatment.
                float mask = SAMPLE_TEXTURE2D_X(_SelectiveSceneryMask, sampler_PointClamp, uv).r;
                if (_ShowMask > .5) return half4(mask,mask,mask,1);
                if (mask < .003) return original;
                float3 sum = 0; float total = 0;
                [unroll] for (int x=-2; x<=2; x++) [unroll] for (int y=-2; y<=2; y++)
                {
                    float2 tap = uv + float2(x,y) * _BlitTexture_TexelSize.xy * (_Treatment.x*.5);
                    float m = SAMPLE_TEXTURE2D_X(_SelectiveSceneryMask, sampler_PointClamp, tap).r;
                    float w = m * Weight(x) * Weight(y);
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, tap).rgb * w; total += w;
                }
                float3 soft = lerp(original.rgb, sum/max(total,.001), mask*_Treatment.y);
                float gray = dot(soft,float3(.2126,.7152,.0722));
                soft = lerp(soft,gray.xxx,mask*_Treatment.z);
                soft = lerp(soft,_Haze.rgb,mask*_Treatment.w);
                return half4(soft,original.a);
            }
            ENDHLSL
        }
    }
}
