Shader "VoxelRT/Rendering/RayTracing/VoxelOccupancyProceduralGbuffer"
{
    SubShader
    {
        Pass
        {
            Name "VoxelOccupancyDXR"
            Tags { "LightMode" = "RayTracing" }

            HLSLPROGRAM
            #pragma only_renderers d3d11
            #pragma multi_compile_local RAY_TRACING_PROCEDURAL_GEOMETRY
            #pragma raytracing surface_shader

            #define RTAS_OCCUPANCY_SEPARATE_TRANSPARENT 1
            #define VOXEL_OCCUPANCY_INCLUDE_HIT_SHADERS 1
            #include "VoxelOccupancyProceduralGbufferShared.hlsl"

            #if RAY_TRACING_PROCEDURAL_GEOMETRY
            [shader("intersection")]
            void IntersectionMain()
            {
                float hitT;
                AttributeData attributes;
                if (TryTraceProceduralIntersection(hitT, attributes))
                {
                    ReportHit(hitT, 0, attributes);
                }
            }
            #endif

            [shader("closesthit")]
            void ClosestHitMain(
                inout RayPayload payload : SV_RayPayload,
                AttributeData attributes : SV_IntersectionAttributes)
            {
                ExecuteProceduralClosestHit(payload, attributes);
            }
            ENDHLSL
        }

        Pass
        {
            Name "VoxelLightingAO"
            Tags { "LightMode" = "RayTracing" }

            HLSLPROGRAM
            #pragma only_renderers d3d11
            #pragma multi_compile_local RAY_TRACING_PROCEDURAL_GEOMETRY
            #pragma raytracing surface_shader

            #define VOXEL_LIGHTING_INCLUDE_HIT_SHADERS 1
            #include "../Lighting/AO/VoxelOccupancyProceduralLightingAOShared.hlsl"

            #if RAY_TRACING_PROCEDURAL_GEOMETRY
            [shader("intersection")]
            void IntersectionMain()
            {
                float hitT;
                AttributeData attributes;
                if (TryTraceProceduralIntersection(hitT, attributes))
                {
                    ReportHit(hitT, 0, attributes);
                }
            }
            #endif

            [shader("closesthit")]
            void ClosestHitMain(
                inout AoPayload payload : SV_RayPayload,
                AttributeData attributes : SV_IntersectionAttributes)
            {
                ExecuteProceduralAoClosestHit(payload, attributes);
            }
            ENDHLSL
        }

        Pass
        {
            Name "VoxelLightingSun"
            Tags { "LightMode" = "RayTracing" }

            HLSLPROGRAM
            #pragma only_renderers d3d11
            #pragma multi_compile_local RAY_TRACING_PROCEDURAL_GEOMETRY
            #pragma raytracing surface_shader

            #define VOXEL_LIGHTING_INCLUDE_HIT_SHADERS 1
            #include "../Lighting/Sun/VoxelOccupancyProceduralLightingSunShared.hlsl"

            #if RAY_TRACING_PROCEDURAL_GEOMETRY
            [shader("intersection")]
            void IntersectionMain()
            {
                float hitT;
                AttributeData attributes;
                if (TryTraceProceduralIntersection(hitT, attributes))
                {
                    ReportHit(hitT, 0, attributes);
                }
            }
            #endif

            [shader("closesthit")]
            void ClosestHitMain(
                inout SunPayload payload : SV_RayPayload,
                AttributeData attributes : SV_IntersectionAttributes)
            {
                ExecuteProceduralSunClosestHit(payload, attributes);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
