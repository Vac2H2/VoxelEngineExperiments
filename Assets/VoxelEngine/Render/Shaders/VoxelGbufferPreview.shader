Shader "Hidden/VoxelEngine/Rendering/GbufferPreview"
{
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

            sampler2D _VoxelEngineGbufferAlbedo;
            sampler2D _VoxelEngineGbufferNormal;
            sampler2D_float _VoxelEngineGbufferDepth;
            sampler2D_float _VoxelEngineGbufferMotion;
            sampler2D_float _VoxelEngineHitDist;
            sampler2D _VoxelEngineRawLight;
            sampler2D _VoxelEngineDenoisedLight;
            sampler2D _VoxelEngineRawReflectionColor;
            sampler2D _VoxelEngineReflectionColor;
            sampler2D _VoxelEngineSkyTexture;

            float _VoxelEngineGbufferPreviewMode;
            float _VoxelEngineCameraFarClip;
            float _VoxelEngineRtaoHitDistanceMax;
            float4x4 _VoxelEnginePreviewPixelCoordToViewDirWS;
            float4 _VoxelEnginePreviewScreenSize;
            float4 _VoxelEnginePreviewBackgroundColor;
            float3 _VoxelEngineSunDirectionWS;
            float3 _VoxelEngineSunColor;
            float _VoxelEngineVisibleSunEnabled;
            float _VoxelEngineVisibleSunIntensity;
            float _VoxelEngineVisibleSunAngularRadius;
            float _VoxelEngineVisibleSunHaloSize;
            float _VoxelEngineVisibleSunHaloIntensity;
            float _VoxelEngineSkyEnabled;
            float _VoxelEngineSkyExposure;
            float _VoxelEngineSkyRotation;

            static const float kPi = 3.14159265359;
            static const float kTwoPi = 6.28318530718;

            float VisualizeHitDistance(float hitDistance)
            {
                return saturate(hitDistance / max(_VoxelEngineRtaoHitDistanceMax, 1e-6));
            }

            float3 ComputePreviewViewDirectionWS(float2 uv)
            {
                float2 pixel = uv * _VoxelEnginePreviewScreenSize.xy;
                return -normalize(mul(float4(pixel + 0.5, 1.0, 1.0), _VoxelEnginePreviewPixelCoordToViewDirWS).xyz);
            }

            float2 DirectionToEquirectUv(float3 directionWS)
            {
                float3 direction = normalize(directionWS);
                float u = atan2(direction.z, direction.x) / kTwoPi;
                float v = 1.0 - (acos(clamp(direction.y, -1.0, 1.0)) / kPi);
                return float2(frac(u + (_VoxelEngineSkyRotation / kTwoPi) + 0.5), saturate(v));
            }

            float3 SampleSky(float3 directionWS)
            {
                if (_VoxelEngineSkyEnabled < 0.5)
                {
                    return max(_VoxelEnginePreviewBackgroundColor.rgb, 0.0);
                }

                float2 skyUv = DirectionToEquirectUv(directionWS);
                return max(tex2D(_VoxelEngineSkyTexture, skyUv).rgb, 0.0) *
                    max(_VoxelEngineSkyExposure, 0.0);
            }

            float3 ComputeSkyColor(float2 uv)
            {
                float3 viewDirectionWS = ComputePreviewViewDirectionWS(uv);
                float3 skyColor = SampleSky(viewDirectionWS);

                if (_VoxelEngineVisibleSunEnabled < 0.5)
                {
                    return skyColor;
                }

                float sunDirectionLengthSq = dot(_VoxelEngineSunDirectionWS, _VoxelEngineSunDirectionWS);
                if (sunDirectionLengthSq <= 1e-8)
                {
                    return skyColor;
                }

                float3 sunDirectionWS = _VoxelEngineSunDirectionWS * rsqrt(sunDirectionLengthSq);
                float angleToSun = acos(clamp(dot(viewDirectionWS, sunDirectionWS), -1.0, 1.0));
                float angularRadius = max(_VoxelEngineVisibleSunAngularRadius, 1e-4);
                float sunDisk = smoothstep(angularRadius * 1.15, angularRadius * 0.85, angleToSun);
                float halo = exp2(-angleToSun * max(_VoxelEngineVisibleSunHaloSize, 1.0)) *
                    max(_VoxelEngineVisibleSunHaloIntensity, 0.0);
                float3 sunColor = max(_VoxelEngineSunColor, 0.0);
                return skyColor + (sunColor * ((sunDisk * max(_VoxelEngineVisibleSunIntensity, 0.0)) + halo));
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float3 albedo = tex2D(_VoxelEngineGbufferAlbedo, input.uv).rgb;
                float3 encodedNormal = tex2D(_VoxelEngineGbufferNormal, input.uv).rgb;
                bool hasSurface = dot(encodedNormal, encodedNormal) > 1e-6;

                if (_VoxelEngineGbufferPreviewMode < 0.5)
                {
                    return float4(albedo, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 1.5)
                {
                    return float4(tex2D(_VoxelEngineGbufferNormal, input.uv).rgb, 1.0);
                }

                float depth = tex2D(_VoxelEngineGbufferDepth, input.uv).r;
                if (_VoxelEngineGbufferPreviewMode < 2.5 && !hasSurface)
                {
                    return 0.0;
                }

                float preview = saturate(depth / max(_VoxelEngineCameraFarClip, 1e-6));
                if (_VoxelEngineGbufferPreviewMode < 2.5)
                {
                    return float4(preview, preview, preview, 1.0);
                }

                float3 motion = tex2D(_VoxelEngineGbufferMotion, input.uv).xyz;
                if (_VoxelEngineGbufferPreviewMode < 3.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    return float4(
                        saturate((motion.xy * 0.5) + 0.5),
                        saturate(abs(motion.z)),
                        1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 4.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    float hitDistance = tex2D(_VoxelEngineHitDist, input.uv).r;
                    float previewHitDistance = VisualizeHitDistance(hitDistance);
                    return float4(previewHitDistance, previewHitDistance, previewHitDistance, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 5.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    float hitDistance = tex2D(_VoxelEngineHitDist, input.uv).r;
                    float rawAo = VisualizeHitDistance(hitDistance);
                    return float4(rawAo, rawAo, rawAo, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 6.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    float3 rawLight = tex2D(_VoxelEngineRawLight, input.uv).rgb;
                    return float4(rawLight, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 7.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    float3 denoisedLight = tex2D(_VoxelEngineDenoisedLight, input.uv).rgb;
                    return float4(denoisedLight, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 8.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    float3 rawReflectionColor = tex2D(_VoxelEngineRawReflectionColor, input.uv).rgb;
                    return float4(rawReflectionColor, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 9.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    float3 reflectionColor = tex2D(_VoxelEngineReflectionColor, input.uv).rgb;
                    return float4(reflectionColor, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 10.5)
                {
                    if (!hasSurface)
                    {
                        return float4(ComputeSkyColor(input.uv), 1.0);
                    }

                    float3 denoisedLight = max(tex2D(_VoxelEngineDenoisedLight, input.uv).rgb, 0.0);
                    float3 reflectionColor = max(tex2D(_VoxelEngineReflectionColor, input.uv).rgb, 0.0);
                    return float4((albedo * denoisedLight) + reflectionColor, 1.0);
                }

                if (_VoxelEngineGbufferPreviewMode < 11.5)
                {
                    if (!hasSurface)
                    {
                        return 0.0;
                    }

                    float roughness = saturate(tex2D(_VoxelEngineGbufferNormal, input.uv).a);
                    float smoothness = 1.0 - roughness;
                    return float4(smoothness, smoothness, smoothness, 1.0);
                }

                return float4(albedo, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
