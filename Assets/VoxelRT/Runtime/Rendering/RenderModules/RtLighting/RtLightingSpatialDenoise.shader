Shader "Hidden/VoxelRT/Rendering/RtLightingSpatialDenoise"
{
    Properties
    {
        _MainTex ("Lighting Temporal", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D_float _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D_float _VoxelRtDepth;
            sampler2D_float _VoxelRtNormal;
            float _SpatialDepthSigma;
            float _SpatialNormalPower;

            float3 DecodeLightingNormal(float3 encodedNormal)
            {
                return normalize((encodedNormal * 2.0) - 1.0);
            }

            float ComputeDepthWeight(float centerDepth, float sampleDepth)
            {
                float delta = abs(centerDepth - sampleDepth);
                return rcp(1.0 + (delta * _SpatialDepthSigma));
            }

            float ComputeNormalWeight(float3 centerNormal, float3 sampleNormal)
            {
                return pow(saturate(dot(centerNormal, sampleNormal)), max(_SpatialNormalPower, 1e-3));
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float3 centerLighting = tex2D(_MainTex, uv).rgb;
                float centerDepth = tex2D(_VoxelRtDepth, uv).r;
                if (centerDepth <= 0.0)
                {
                    return float4(centerLighting, 1.0);
                }

                float3 centerNormal = DecodeLightingNormal(tex2D(_VoxelRtNormal, uv).xyz);
                float2 texel = _MainTex_TexelSize.xy;

                float3 accumulatedLighting = centerLighting;
                float accumulatedWeight = 1.0;

                [unroll]
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    [unroll]
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        float2 sampleUv = uv + (float2(offsetX, offsetY) * texel);
                        float sampleDepth = tex2D(_VoxelRtDepth, sampleUv).r;
                        if (sampleDepth <= 0.0)
                        {
                            continue;
                        }

                        float3 sampleNormal = DecodeLightingNormal(tex2D(_VoxelRtNormal, sampleUv).xyz);
                        float spatialWeight = ComputeDepthWeight(centerDepth, sampleDepth) *
                                              ComputeNormalWeight(centerNormal, sampleNormal);
                        if (spatialWeight <= 1e-4)
                        {
                            continue;
                        }

                        accumulatedLighting += tex2D(_MainTex, sampleUv).rgb * spatialWeight;
                        accumulatedWeight += spatialWeight;
                    }
                }

                return float4(accumulatedLighting / max(accumulatedWeight, 1e-4), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
