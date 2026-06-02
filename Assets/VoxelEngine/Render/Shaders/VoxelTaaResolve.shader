Shader "Hidden/VoxelEngine/Rendering/TaaResolve"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "TaaResolve"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "VoxelGbufferEncoding.hlsl"

            sampler2D _VoxelEngineTaaCurrentColor;
            sampler2D _VoxelEngineTaaHistoryColor;
            sampler2D_float _VoxelEngineTaaHistoryViewZ;
            sampler2D _VoxelEngineTaaHistoryNormal;
            sampler2D_float _VoxelEngineGbufferViewZ;
            sampler2D _VoxelEngineGbufferNormal;
            sampler2D _VoxelEngineGbufferMotion;

            float _VoxelEngineTaaHasHistory;
            float _VoxelEngineTaaHistoryWeight;
            float _VoxelEngineTaaDepthRelativeThreshold;
            float _VoxelEngineTaaNormalThreshold;
            float4 _VoxelEngineTaaCurrentTexelSize;

            bool HasSurface(float2 uv)
            {
                float3 encodedNormal = tex2D(_VoxelEngineGbufferNormal, uv).rgb;
                return dot(encodedNormal, encodedNormal) > 1e-6;
            }

            float3 LoadCurrentNormal(float2 uv)
            {
                return VoxelUnpackNormalAndRoughness(tex2D(_VoxelEngineGbufferNormal, uv)).xyz;
            }

            float3 LoadHistoryNormal(float2 uv)
            {
                return VoxelUnpackNormalAndRoughness(tex2D(_VoxelEngineTaaHistoryNormal, uv)).xyz;
            }

            float3 ClampHistoryToCurrentNeighborhood(float2 uv, float3 historyColor, float clampExpansion)
            {
                float3 minColor = tex2D(_VoxelEngineTaaCurrentColor, uv).rgb;
                float3 maxColor = minColor;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 sampleUv = uv + (float2(x, y) * _VoxelEngineTaaCurrentTexelSize.xy);
                        float3 sampleColor = tex2D(_VoxelEngineTaaCurrentColor, sampleUv).rgb;
                        minColor = min(minColor, sampleColor);
                        maxColor = max(maxColor, sampleColor);
                    }
                }

                float3 neighborhoodRange = max(maxColor - minColor, 1e-4);
                return clamp(
                    historyColor,
                    minColor - (neighborhoodRange * clampExpansion),
                    maxColor + (neighborhoodRange * clampExpansion));
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float3 currentColor = max(tex2D(_VoxelEngineTaaCurrentColor, input.uv).rgb, 0.0);
                if (_VoxelEngineTaaHasHistory < 0.5 || !HasSurface(input.uv))
                {
                    return float4(currentColor, 1.0);
                }

                float3 motion = tex2D(_VoxelEngineGbufferMotion, input.uv).xyz;
                float2 previousUv = input.uv + motion.xy;
                if (any(previousUv < 0.0) || any(previousUv > 1.0))
                {
                    return float4(currentColor, 1.0);
                }

                float currentViewZ = tex2D(_VoxelEngineGbufferViewZ, input.uv).r;
                float expectedPreviousViewZ = max(currentViewZ + motion.z, 0.0);
                float historyViewZ = tex2D(_VoxelEngineTaaHistoryViewZ, previousUv).r;
                if (historyViewZ <= 0.0 || expectedPreviousViewZ <= 0.0)
                {
                    return float4(currentColor, 1.0);
                }

                float depthRelativeError =
                    abs(historyViewZ - expectedPreviousViewZ) / max(expectedPreviousViewZ, 1e-3);
                float farHistoryFactor = saturate((currentViewZ - 48.0) / 192.0);
                float depthThreshold = lerp(
                    max(_VoxelEngineTaaDepthRelativeThreshold, 0.0),
                    max(_VoxelEngineTaaDepthRelativeThreshold, 0.22),
                    farHistoryFactor);
                if (depthRelativeError > depthThreshold)
                {
                    return float4(currentColor, 1.0);
                }

                float3 currentNormal = LoadCurrentNormal(input.uv);
                float3 historyEncodedNormal = tex2D(_VoxelEngineTaaHistoryNormal, previousUv).rgb;
                if (dot(historyEncodedNormal, historyEncodedNormal) <= 1e-6)
                {
                    return float4(currentColor, 1.0);
                }

                float3 historyNormal = LoadHistoryNormal(previousUv);
                float normalSimilarity = dot(currentNormal, historyNormal);
                float normalThreshold = lerp(
                    saturate(_VoxelEngineTaaNormalThreshold),
                    min(saturate(_VoxelEngineTaaNormalThreshold), 0.35),
                    farHistoryFactor);
                if (normalSimilarity < normalThreshold)
                {
                    return float4(currentColor, 1.0);
                }

                float3 historyColor = max(tex2D(_VoxelEngineTaaHistoryColor, previousUv).rgb, 0.0);
                historyColor = ClampHistoryToCurrentNeighborhood(input.uv, historyColor, lerp(0.05, 0.20, farHistoryFactor));

                float historyWeight = saturate(_VoxelEngineTaaHistoryWeight);
                return float4(lerp(currentColor, historyColor, historyWeight), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
