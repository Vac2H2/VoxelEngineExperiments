#ifndef VOXEL_ENGINE_GBUFFER_ENCODING_INCLUDED
#define VOXEL_ENGINE_GBUFFER_ENCODING_INCLUDED

float3 VoxelSafeNormalize(float3 value)
{
    float lengthSq = dot(value, value);
    return lengthSq > 1e-8 ? value * rsqrt(lengthSq) : float3(0.0, 1.0, 0.0);
}

float4 VoxelPackNormalAndRoughness(float3 normalWS, float linearRoughness)
{
    float3 normal = VoxelSafeNormalize(normalWS);
    float maxAbsComponent = max(max(abs(normal.x), abs(normal.y)), abs(normal.z));
    normal /= max(maxAbsComponent, 1e-6);
    return float4((normal * 0.5) + 0.5, saturate(linearRoughness));
}

float4 VoxelUnpackNormalAndRoughness(float4 packedNormalRoughness)
{
    float3 normal = (packedNormalRoughness.xyz * 2.0) - 1.0;
    return float4(VoxelSafeNormalize(normal), saturate(packedNormalRoughness.w));
}

#endif
