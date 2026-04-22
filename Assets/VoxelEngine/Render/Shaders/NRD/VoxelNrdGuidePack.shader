Shader "Hidden/VoxelEngine/Rendering/NrdGuidePack"
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
            #include "Vendor/NRD.hlsli"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _VoxelEngineNrdSecondarySource;
            float4 _VoxelEngineNrdSecondarySourceTexelSize;
            float _VoxelEngineNrdPixelStep;
            float _VoxelEngineNrdSecondaryPixelStep;
            float _VoxelEngineNrdGuideMode;
            float4 _VoxelEngineNrdHitDistanceParameters;

            float2 ComputeSourceUv(float2 outputUv, float4 texelSize, float pixelStep)
            {
                float2 sourceSize = max(texelSize.zw, 1.0);
                pixelStep = max(pixelStep, 1.0);
                float2 outputPixel = floor((outputUv * sourceSize) / pixelStep);
                float2 sourcePixel = (outputPixel * pixelStep) + (pixelStep * 0.5);
                return saturate(sourcePixel * texelSize.xy);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                if (_VoxelEngineNrdGuideMode > 0.5)
                {
                    float2 hitDistUv = ComputeSourceUv(input.uv, _MainTex_TexelSize, _VoxelEngineNrdPixelStep);
                    float2 viewZUv = ComputeSourceUv(input.uv, _VoxelEngineNrdSecondarySourceTexelSize, _VoxelEngineNrdSecondaryPixelStep);
                    float hitDistance = tex2D(_MainTex, hitDistUv).r;
                    float viewZ = tex2D(_VoxelEngineNrdSecondarySource, viewZUv).r;
                    float normalizedHitDistance = REBLUR_FrontEnd_GetNormHitDist(
                        hitDistance,
                        max(viewZ, 1e-4),
                        _VoxelEngineNrdHitDistanceParameters.xyz,
                        1.0);
                    return normalizedHitDistance.xxxx;
                }

                return tex2D(_MainTex, ComputeSourceUv(input.uv, _MainTex_TexelSize, _VoxelEngineNrdPixelStep));
            }
            ENDCG
        }
    }

    Fallback Off
}
