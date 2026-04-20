Shader "VoxelEngine/Rendering/RayTracing/VoxelProceduralShader"
{
    Properties
    {
        [HideInInspector] _VoxelChunkCount("Voxel Chunk Count", Int) = 0
        [HideInInspector] _VoxelAabbCount("Voxel AABB Count", Int) = 0
        [HideInInspector] _VoxelPaletteColorCount("Voxel Palette Color Count", Int) = 0
        [HideInInspector] _VoxelOpaqueMaterial("Voxel Opaque Material", Float) = 1
        [HideInInspector] _VoxelDebugAabbOverlay("Voxel Debug AABB Overlay", Float) = 0
        [HideInInspector] _VoxelDebugAabbLineWidth("Voxel Debug AABB Line Width", Float) = 0.08
    }

    SubShader
    {
        Pass
        {
            Name "VoxelProceduralDXR"
            Tags { "LightMode" = "RayTracing" }

            HLSLPROGRAM
            #pragma only_renderers d3d11
            #pragma multi_compile_local RAY_TRACING_PROCEDURAL_GEOMETRY
            #pragma raytracing surface_shader

            #define VOXEL_ENGINE_INCLUDE_HIT_SHADERS 1
            #include "VoxelProceduralShaderShared.hlsl"

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
    }

    Fallback Off
}
