Shader "Hidden/VoxelEngine/Rendering/ReflectionSpatialDenoise"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "SpatialReflection"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "VoxelGbufferEncoding.hlsl"

            sampler2D _VoxelEngineRawReflectionColor;
            sampler2D _VoxelEngineGbufferNormal;
            sampler2D_float _VoxelEngineGbufferDepth;
            float4 _VoxelEngineReflectionSpatialTexelSize;
            float _VoxelEngineReflectionSpatialNormalPower;
            float _VoxelEngineReflectionSpatialDepthScale;

            bool HasSurface(float2 uv)
            {
                float3 encodedNormal = tex2D(_VoxelEngineGbufferNormal, uv).rgb;
                return dot(encodedNormal, encodedNormal) > 1e-6;
            }

            float4 LoadNormalRoughness(float2 uv)
            {
                return VoxelUnpackNormalAndRoughness(tex2D(_VoxelEngineGbufferNormal, uv));
            }

            float ComputeSpatialWeight(int2 offset)
            {
                float2 offsetFloat = float2(offset.x, offset.y);
                float distSq = dot(offsetFloat, offsetFloat);
                return rcp(1.0 + distSq);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float3 centerReflection = tex2D(_VoxelEngineRawReflectionColor, input.uv).rgb;
                if (!HasSurface(input.uv))
                {
                    return float4(centerReflection, 1.0);
                }

                float centerDepth = tex2D(_VoxelEngineGbufferDepth, input.uv).r;
                float4 centerNormalRoughness = LoadNormalRoughness(input.uv);
                float3 centerNormal = centerNormalRoughness.xyz;
                float roughness = centerNormalRoughness.w;

                float3 weightedReflection = 0.0;
                float weightSum = 0.0;
                int radius = roughness > 0.45 ? 2 : 1;

                [loop]
                for (int y = -2; y <= 2; y++)
                {
                    [loop]
                    for (int x = -2; x <= 2; x++)
                    {
                        int2 offset = int2(x, y);
                        if (abs(x) > radius || abs(y) > radius)
                        {
                            continue;
                        }

                        float2 uv = input.uv + ((float2)offset * _VoxelEngineReflectionSpatialTexelSize.xy);
                        if (!HasSurface(uv))
                        {
                            continue;
                        }

                        float sampleDepth = tex2D(_VoxelEngineGbufferDepth, uv).r;
                        float4 sampleNormalRoughness = LoadNormalRoughness(uv);
                        float3 sampleNormal = sampleNormalRoughness.xyz;
                        float normalWeight = pow(
                            saturate(dot(centerNormal, sampleNormal)),
                            max(_VoxelEngineReflectionSpatialNormalPower, 1.0));
                        float depthWeight = exp2(-abs(sampleDepth - centerDepth) * max(_VoxelEngineReflectionSpatialDepthScale, 0.0));
                        float roughnessWeight = 1.0 - saturate(abs(sampleNormalRoughness.w - roughness) * 4.0);
                        float weight = ComputeSpatialWeight(offset) * normalWeight * depthWeight * roughnessWeight;

                        weightedReflection += tex2D(_VoxelEngineRawReflectionColor, uv).rgb * weight;
                        weightSum += weight;
                    }
                }

                float3 reflection = weightSum > 1e-5
                    ? weightedReflection / weightSum
                    : centerReflection;
                return float4(reflection, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
