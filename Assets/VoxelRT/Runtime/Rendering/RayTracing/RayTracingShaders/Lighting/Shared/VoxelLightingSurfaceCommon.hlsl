#ifndef VOXEL_LIGHTING_SURFACE_COMMON_INCLUDED
#define VOXEL_LIGHTING_SURFACE_COMMON_INCLUDED

Texture2D<float4> _VoxelRtNormal;
Texture2D<float> _VoxelRtDepth;

float4x4 _PixelCoordToViewDirWS;
float3 _CameraPositionWS;
float3 _CameraForwardWS;

float3 DecodeLightingNormal(float3 encodedNormal)
{
    return normalize((encodedNormal * 2.0) - 1.0);
}

float3 ComputeLightingPrimaryViewDirectionWS(uint2 pixel)
{
    return -normalize(mul(float4((float2)pixel + 0.5, 1.0, 1.0), _PixelCoordToViewDirWS).xyz);
}

float ReconstructPrimaryRayHitT(float linearViewDepth, float3 viewDirectionWS)
{
    return linearViewDepth / max(dot(viewDirectionWS, _CameraForwardWS), 1e-6);
}

bool TryLoadLightingSurface(
    uint2 pixel,
    out float3 positionWS,
    out float3 normalWS,
    out float3 viewDirectionWS)
{
    float linearViewDepth = _VoxelRtDepth.Load(int3(pixel, 0));
    if (linearViewDepth <= 0.0)
    {
        positionWS = 0.0;
        normalWS = 0.0;
        viewDirectionWS = 0.0;
        return false;
    }

    viewDirectionWS = ComputeLightingPrimaryViewDirectionWS(pixel);
    normalWS = DecodeLightingNormal(_VoxelRtNormal.Load(int3(pixel, 0)).xyz);
    positionWS = _CameraPositionWS + (viewDirectionWS * ReconstructPrimaryRayHitT(linearViewDepth, viewDirectionWS));
    return true;
}

#endif
