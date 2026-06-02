Shader "Hidden/VoxelEngine/Rendering/RtaoFrameAverage"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "ComposeRawLight"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D_float _VoxelEngineRtaoHitDistanceSource;
            sampler2D _VoxelEngineSunLight;
            float _VoxelEngineRtaoHitDistanceMax;
            float _VoxelEngineRtaoAmbientVisibility;
            float _VoxelEngineRtaoAoStepSize;
            float _VoxelEngineRtaoAoMinVisibility;
            float4 _VoxelEngineAmbientLightColor;

            float ResolveAmbientVisibility(float hitDistance)
            {
                float stepSize = max(_VoxelEngineRtaoAoStepSize, 0.0);
                if (stepSize > 1e-5)
                {
                    hitDistance = (floor(hitDistance / stepSize) + 0.5) * stepSize;
                }

                float visibility = saturate(hitDistance / max(_VoxelEngineRtaoHitDistanceMax, 1e-6));
                return max(visibility, saturate(_VoxelEngineRtaoAoMinVisibility));
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float currentHitDistance = tex2D(_VoxelEngineRtaoHitDistanceSource, input.uv).r;
                float normalizedAo = ResolveAmbientVisibility(currentHitDistance);
                float3 sunLight = max(tex2D(_VoxelEngineSunLight, input.uv).rgb, 0.0);
                float3 ambientLight = max(_VoxelEngineAmbientLightColor.rgb, 0.0) *
                    (saturate(_VoxelEngineRtaoAmbientVisibility) * normalizedAo);
                float3 rawLight = sunLight + ambientLight;
                return float4(rawLight, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "SpatialLight"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "VoxelGbufferEncoding.hlsl"

            sampler2D _VoxelEngineRtaoSpatialSource;
            sampler2D _VoxelEngineGbufferNormal;
            sampler2D_float _VoxelEngineGbufferDepth;
            float4 _VoxelEngineRtaoSpatialSourceTexelSize;
            float _VoxelEngineRtaoSpatialNormalPower;
            float _VoxelEngineRtaoSpatialDepthScale;
            float _VoxelEngineRtaoSpatialRadius;
            int _VoxelEngineRtaoSpatialSampleCount;

            static const float kGoldenAngle = 2.39996322973;

            bool HasSurface(float2 uv)
            {
                float3 encodedNormal = tex2D(_VoxelEngineGbufferNormal, uv).rgb;
                return dot(encodedNormal, encodedNormal) > 1e-6;
            }

            float3 LoadNormal(float2 uv)
            {
                return VoxelUnpackNormalAndRoughness(tex2D(_VoxelEngineGbufferNormal, uv)).xyz;
            }

            float ComputeSpatialWeight(float radius, float maxRadius)
            {
                return exp2(-2.0 * radius / max(maxRadius, 1e-3));
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float3 centerLight = tex2D(_VoxelEngineRtaoSpatialSource, input.uv).rgb;
                if (!HasSurface(input.uv))
                {
                    return float4(centerLight, 1.0);
                }

                float centerDepth = tex2D(_VoxelEngineGbufferDepth, input.uv).r;
                float3 centerNormal = LoadNormal(input.uv);
                float3 weightedLight = centerLight;
                float weightSum = 1.0;
                int sampleCount = clamp(_VoxelEngineRtaoSpatialSampleCount, 4, 24);
                float maxRadius = max(_VoxelEngineRtaoSpatialRadius, 1.0);

                [unroll]
                for (int sampleIndex = 0; sampleIndex < 24; sampleIndex++)
                {
                    if (sampleIndex >= sampleCount)
                    {
                        break;
                    }

                    float t = ((float)sampleIndex + 0.5) / (float)sampleCount;
                    float radius = sqrt(t) * maxRadius;
                    float angle = (float)sampleIndex * kGoldenAngle;
                    float2 offset = float2(cos(angle), sin(angle)) * radius;
                    float2 uv = input.uv + (offset * _VoxelEngineRtaoSpatialSourceTexelSize.xy);
                    if (!HasSurface(uv))
                    {
                        continue;
                    }

                    float3 sampleLight = tex2D(_VoxelEngineRtaoSpatialSource, uv).rgb;
                    float sampleDepth = tex2D(_VoxelEngineGbufferDepth, uv).r;
                    float3 sampleNormal = LoadNormal(uv);
                    float normalWeight = pow(saturate(dot(centerNormal, sampleNormal)), max(_VoxelEngineRtaoSpatialNormalPower, 1.0));
                    float depthWeight = exp2(-abs(sampleDepth - centerDepth) * max(_VoxelEngineRtaoSpatialDepthScale, 0.0));
                    float weight = ComputeSpatialWeight(radius, maxRadius) * normalWeight * depthWeight;
                    weightedLight += sampleLight * weight;
                    weightSum += weight;
                }

                float3 spatialLight = weightSum > 1e-5 ? weightedLight / weightSum : centerLight;
                return float4(spatialLight, 1.0);
            }
            ENDCG
        }

    }
}
