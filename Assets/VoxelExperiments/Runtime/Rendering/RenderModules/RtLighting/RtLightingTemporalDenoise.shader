Shader "Hidden/VoxelExperiments/Rendering/RtLightingTemporalDenoise"
{
    Properties
    {
        _MainTex ("Lighting Raw", 2D) = "black" {}
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
            sampler2D_float _VoxelExperimentsDepth;
            sampler2D_float _VoxelExperimentsNormal;
            sampler2D_float _VoxelExperimentsVelocity;
            sampler2D_float _HistoryLightingTex;
            sampler2D_float _HistoryDepthTex;
            sampler2D _HistoryNormalTex;

            float4x4 _PixelCoordToViewDirWS;
            float3 _CameraPositionWS;
            float3 _CameraForwardWS;
            float3 _PrevCameraPositionWS;
            float3 _PrevCameraForwardWS;
            float _HasLightingHistory;
            float _HistoryWeight;
            float _DepthRejectRelativeThreshold;
            float _NormalRejectCosThreshold;

            float3 DecodeLightingNormal(float3 encodedNormal)
            {
                return normalize((encodedNormal * 2.0) - 1.0);
            }

            float3 ComputeLightingPrimaryViewDirectionWS(float2 uv)
            {
                float2 pixel = (uv * _ScreenParams.xy) - 0.5;
                return -normalize(mul(float4(pixel + 0.5, 1.0, 1.0), _PixelCoordToViewDirWS).xyz);
            }

            float ReconstructPrimaryRayHitT(float linearViewDepth, float3 viewDirectionWS)
            {
                return linearViewDepth / max(dot(viewDirectionWS, _CameraForwardWS), 1e-6);
            }

            float3 ClampHistoryToNeighborhood(float2 uv, float3 historyLighting)
            {
                float2 texel = _MainTex_TexelSize.xy;
                float3 center = tex2D(_MainTex, uv).rgb;
                float3 minLighting = center;
                float3 maxLighting = center;

                float3 leftLighting = tex2D(_MainTex, uv + float2(-texel.x, 0.0)).rgb;
                float3 rightLighting = tex2D(_MainTex, uv + float2(texel.x, 0.0)).rgb;
                float3 upLighting = tex2D(_MainTex, uv + float2(0.0, texel.y)).rgb;
                float3 downLighting = tex2D(_MainTex, uv + float2(0.0, -texel.y)).rgb;

                minLighting = min(minLighting, leftLighting);
                minLighting = min(minLighting, rightLighting);
                minLighting = min(minLighting, upLighting);
                minLighting = min(minLighting, downLighting);

                maxLighting = max(maxLighting, leftLighting);
                maxLighting = max(maxLighting, rightLighting);
                maxLighting = max(maxLighting, upLighting);
                maxLighting = max(maxLighting, downLighting);

                return clamp(historyLighting, minLighting, maxLighting);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float3 currentLighting = tex2D(_MainTex, uv).rgb;
                if (_HasLightingHistory < 0.5)
                {
                    return float4(currentLighting, 1.0);
                }

                float currentDepth = tex2D(_VoxelExperimentsDepth, uv).r;
                if (currentDepth <= 0.0)
                {
                    return float4(currentLighting, 1.0);
                }

                float2 velocity = tex2D(_VoxelExperimentsVelocity, uv).rg;
                float2 previousUv = uv - velocity;
                if (any(previousUv < 0.0) || any(previousUv > 1.0))
                {
                    return float4(currentLighting, 1.0);
                }

                float3 currentNormal = DecodeLightingNormal(tex2D(_VoxelExperimentsNormal, uv).xyz);
                float3 viewDirectionWS = ComputeLightingPrimaryViewDirectionWS(uv);
                float hitT = ReconstructPrimaryRayHitT(currentDepth, viewDirectionWS);
                float3 positionWS = _CameraPositionWS + (viewDirectionWS * hitT);

                float expectedPreviousDepth = max(dot(positionWS - _PrevCameraPositionWS, _PrevCameraForwardWS), 0.0);
                float historyDepth = tex2D(_HistoryDepthTex, previousUv).r;
                if (historyDepth <= 0.0)
                {
                    return float4(currentLighting, 1.0);
                }

                float3 historyNormal = DecodeLightingNormal(tex2D(_HistoryNormalTex, previousUv).xyz);
                float depthRelativeError = abs(historyDepth - expectedPreviousDepth) / max(expectedPreviousDepth, 1e-3);
                float normalSimilarity = dot(currentNormal, historyNormal);
                if (depthRelativeError > _DepthRejectRelativeThreshold || normalSimilarity < _NormalRejectCosThreshold)
                {
                    return float4(currentLighting, 1.0);
                }

                float3 historyLighting = tex2D(_HistoryLightingTex, previousUv).rgb;
                historyLighting = ClampHistoryToNeighborhood(uv, historyLighting);

                float blendWeight = saturate(_HistoryWeight);
                float3 denoisedLighting = lerp(currentLighting, historyLighting, blendWeight);
                return float4(denoisedLighting, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
