Shader "Hidden/VoxelExperiments/Rendering/RtLightingCompose"
{
    Properties
    {
        _MainTex ("Lighting", 2D) = "black" {}
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
            sampler2D _VoxelExperimentsAlbedo;
            sampler2D_float _VoxelExperimentsDepth;

            float4 frag(v2f_img input) : SV_Target
            {
                float3 albedo = tex2D(_VoxelExperimentsAlbedo, input.uv).rgb;
                float depth = tex2D(_VoxelExperimentsDepth, input.uv).r;
                if (depth <= 0.0)
                {
                    return float4(albedo, 1.0);
                }

                float3 lighting = tex2D(_MainTex, input.uv).rgb;
                return float4(albedo * lighting, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
