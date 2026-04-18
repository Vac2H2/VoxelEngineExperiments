#ifndef VOXEL_OCCUPANCY_PROCEDURAL_LIGHTING_LOCAL_SHARED_INCLUDED
#define VOXEL_OCCUPANCY_PROCEDURAL_LIGHTING_LOCAL_SHARED_INCLUDED

#ifndef VOXEL_LIGHTING_INCLUDE_HIT_SHADERS
    #define VOXEL_LIGHTING_INCLUDE_HIT_SHADERS 1
#endif

struct LocalLightPayload
{
    float hitT;
    uint hit;
};

bool HasLocalLightHit(LocalLightPayload payload)
{
    return payload.hit != 0u;
}

void InitializeLocalLightPayload(out LocalLightPayload payload)
{
    payload.hitT = 0.0;
    payload.hit = 0u;
}

#if VOXEL_LIGHTING_INCLUDE_HIT_SHADERS
#include "../Shared/VoxelOccupancyProceduralLightingTraversal.hlsl"

void ExecuteProceduralLocalClosestHit(
    inout LocalLightPayload payload,
    AttributeData attributes)
{
    payload.hitT = RayTCurrent();
    payload.hit = 1u;
}
#endif

#endif
