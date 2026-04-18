Shader "Hidden/VoxelExperiments/Rendering/RtLightingAdditive"
{
    Properties
    {
        _MainTex ("AO Source", 2D) = "white" {}
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
            sampler2D_float _VoxelExperimentsSunLight;
            sampler2D_float _VoxelExperimentsLocalLight;
            float4 _AmbientColor;

            float4 frag(v2f_img input) : SV_Target
            {
                float ao = saturate(tex2D(_MainTex, input.uv).r);
                float3 lighting = (_AmbientColor.rgb * ao) +
                                  tex2D(_VoxelExperimentsSunLight, input.uv).rgb +
                                  tex2D(_VoxelExperimentsLocalLight, input.uv).rgb;
                return float4(lighting, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
