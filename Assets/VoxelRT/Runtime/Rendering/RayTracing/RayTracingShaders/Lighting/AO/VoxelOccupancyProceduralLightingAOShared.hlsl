#ifndef VOXEL_OCCUPANCY_PROCEDURAL_LIGHTING_AO_SHARED_INCLUDED
#define VOXEL_OCCUPANCY_PROCEDURAL_LIGHTING_AO_SHARED_INCLUDED

#ifndef VOXEL_LIGHTING_INCLUDE_HIT_SHADERS
    #define VOXEL_LIGHTING_INCLUDE_HIT_SHADERS 1
#endif

struct AoPayload
{
    float hitT;
    uint hit;
};

bool HasAoHit(AoPayload payload)
{
    return payload.hit != 0u;
}

void InitializeAoPayload(out AoPayload payload)
{
    payload.hitT = 0.0;
    payload.hit = 0u;
}

#if VOXEL_LIGHTING_INCLUDE_HIT_SHADERS
#include "../Shared/VoxelOccupancyProceduralLightingTraversal.hlsl"

void ExecuteProceduralAoClosestHit(
    inout AoPayload payload,
    AttributeData attributes)
{
    payload.hitT = RayTCurrent();
    payload.hit = 1u;
}
#endif

#endif
