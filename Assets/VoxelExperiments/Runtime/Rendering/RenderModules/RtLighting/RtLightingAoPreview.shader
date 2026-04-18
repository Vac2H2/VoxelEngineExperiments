Shader "VoxelExperiments/Rendering/Preview/RtLightingAoPreview"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
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

            float4 frag(v2f_img input) : SV_Target
            {
                float visibility = saturate(tex2D(_MainTex, input.uv).r);
                return float4(visibility, visibility, visibility, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
