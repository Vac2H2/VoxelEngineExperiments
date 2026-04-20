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
            sampler2D_float _VoxelEngineRtao;

            float _VoxelEngineGbufferPreviewMode;
            float _VoxelEngineRtaoMaxDistance;

            float4 frag(v2f_img input) : SV_Target
            {
                float3 encodedNormal = tex2D(_VoxelEngineGbufferNormal, input.uv).rgb;
                bool hasSurface = dot(encodedNormal, encodedNormal) > 1e-6;

                if (_VoxelEngineGbufferPreviewMode < 0.5)
                {
                    return float4(tex2D(_VoxelEngineGbufferAlbedo, input.uv).rgb, 1.0);
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

                float preview = saturate(depth);
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

                float aoDistance = tex2D(_VoxelEngineRtao, input.uv).r;
                if (!hasSurface)
                {
                    return 0.0;
                }

                float aoPreview = saturate(aoDistance / max(_VoxelEngineRtaoMaxDistance, 1e-6));
                return float4(aoPreview, aoPreview, aoPreview, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
