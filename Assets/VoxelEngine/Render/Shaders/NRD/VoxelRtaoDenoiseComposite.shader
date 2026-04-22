Shader "Hidden/VoxelEngine/Rendering/RtaoDenoiseComposite"
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

            sampler2D _MainTex;

            float4 frag(v2f_img input) : SV_Target
            {
                float aoValue = tex2D(_MainTex, input.uv).r;
                return float4(aoValue, aoValue, aoValue, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
