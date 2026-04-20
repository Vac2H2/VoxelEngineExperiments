#ifndef VOXEL_ENGINE_PROCEDURAL_SHADER_SHARED_INCLUDED
#define VOXEL_ENGINE_PROCEDURAL_SHADER_SHARED_INCLUDED

#ifndef VOXEL_ENGINE_INCLUDE_HIT_SHADERS
    #define VOXEL_ENGINE_INCLUDE_HIT_SHADERS 0
#endif

static const uint kVoxelChunkDimension = 8u;
static const uint kVoxelChunkVoxelByteCount = 512u;
static const uint kRayPayloadFlagValidHit = 1u << 0;
static const uint kRayPayloadFlagTransparentHit = 1u << 1;
static const uint kRayPayloadFlagKeepScreenDoorHit = 1u << 2;
static const float kProceduralRayTMin = 1e-5;
static const float kHugeDistance = 1e30;

struct RayPayload
{
    float hitT;
    float3 worldNormal;
    uint packedAlbedo;
    uint flags;
};

bool HasHit(RayPayload payload)
{
    return (payload.flags & kRayPayloadFlagValidHit) != 0u;
}

bool IsTransparentHit(RayPayload payload)
{
    return (payload.flags & kRayPayloadFlagTransparentHit) != 0u;
}

bool KeepsScreenDoorHit(RayPayload payload)
{
    return (payload.flags & kRayPayloadFlagKeepScreenDoorHit) != 0u;
}

bool ShouldKeepScreenDoorHitForPixel(uint2 pixel)
{
    return ((pixel.x ^ pixel.y) & 1u) == 0u;
}

bool ShouldKeepScreenDoorHit()
{
    return ShouldKeepScreenDoorHitForPixel(DispatchRaysIndex().xy);
}

#if VOXEL_ENGINE_INCLUDE_HIT_SHADERS
struct VoxelBlasAabbDesc
{
    int chunkIndex;
    int3 localMin;
    int3 localMax;
};

struct VoxelBlasAabb
{
    float3 boundsMin;
    float3 boundsMax;
};

struct AttributeData
{
    uint packedHitData;
};

ByteAddressBuffer _VoxelVolumeBuffer;
StructuredBuffer<VoxelBlasAabbDesc> _VoxelAabbDescBuffer;
StructuredBuffer<VoxelBlasAabb> _VoxelAabbBuffer;
StructuredBuffer<uint> _VoxelPaletteColorBuffer;

int _VoxelChunkCount;
int _VoxelAabbCount;
int _VoxelPaletteColorCount;
float _VoxelOpaqueMaterial;
float _VoxelDebugAabbOverlay;
float _VoxelDebugAabbLineWidth;

struct VoxelHit
{
    float t;
    uint faceId;
    uint localVoxelIndex;
};

struct DdaState
{
    int3 voxel;
    int3 step;
    int lastAxis;
    int lastStepSign;
    float t;
    float tLimit;
    float3 tMax;
    float3 tDelta;
};

uint ReadRawByte(ByteAddressBuffer buffer, uint byteAddress)
{
    uint wordAddress = byteAddress & ~3u;
    uint packedWord = buffer.Load(wordAddress);
    uint byteShift = (byteAddress & 3u) * 8u;
    return (packedWord >> byteShift) & 0xFFu;
}

uint ReadPaletteEntry(uint paletteEntryIndex)
{
    if ((int)paletteEntryIndex < 0 || (int)paletteEntryIndex >= _VoxelPaletteColorCount)
    {
        return 0u;
    }

    return _VoxelPaletteColorBuffer[paletteEntryIndex];
}

float3 DecodeFaceNormal(uint faceId)
{
    if (faceId == 0u) return float3(-1.0, 0.0, 0.0);
    if (faceId == 1u) return float3(1.0, 0.0, 0.0);
    if (faceId == 2u) return float3(0.0, -1.0, 0.0);
    if (faceId == 3u) return float3(0.0, 1.0, 0.0);
    if (faceId == 4u) return float3(0.0, 0.0, -1.0);
    return float3(0.0, 0.0, 1.0);
}

bool RayBox(
    float3 rayOrigin,
    float3 rayDirection,
    float3 boundsMin,
    float3 boundsMax,
    out float tEnter,
    out float tExit,
    out int entryAxis,
    out int entryStepSign)
{
    const float epsilon = 1e-8;
    const float infinity = 3.402823466e+38;

    entryAxis = 0;
    entryStepSign = 1;

    float txMin;
    float txMax;
    if (abs(rayDirection.x) < epsilon)
    {
        if (rayOrigin.x < boundsMin.x || rayOrigin.x > boundsMax.x) { tEnter = 0.0; tExit = -1.0; return false; }
        txMin = -infinity;
        txMax = infinity;
    }
    else
    {
        float tx0 = (boundsMin.x - rayOrigin.x) / rayDirection.x;
        float tx1 = (boundsMax.x - rayOrigin.x) / rayDirection.x;
        txMin = min(tx0, tx1);
        txMax = max(tx0, tx1);
    }

    float tyMin;
    float tyMax;
    if (abs(rayDirection.y) < epsilon)
    {
        if (rayOrigin.y < boundsMin.y || rayOrigin.y > boundsMax.y) { tEnter = 0.0; tExit = -1.0; return false; }
        tyMin = -infinity;
        tyMax = infinity;
    }
    else
    {
        float ty0 = (boundsMin.y - rayOrigin.y) / rayDirection.y;
        float ty1 = (boundsMax.y - rayOrigin.y) / rayDirection.y;
        tyMin = min(ty0, ty1);
        tyMax = max(ty0, ty1);
    }

    float tzMin;
    float tzMax;
    if (abs(rayDirection.z) < epsilon)
    {
        if (rayOrigin.z < boundsMin.z || rayOrigin.z > boundsMax.z) { tEnter = 0.0; tExit = -1.0; return false; }
        tzMin = -infinity;
        tzMax = infinity;
    }
    else
    {
        float tz0 = (boundsMin.z - rayOrigin.z) / rayDirection.z;
        float tz1 = (boundsMax.z - rayOrigin.z) / rayDirection.z;
        tzMin = min(tz0, tz1);
        tzMax = max(tz0, tz1);
    }

    tEnter = max(max(txMin, tyMin), tzMin);
    tExit = min(min(txMax, tyMax), tzMax);
    if (tExit < max(tEnter, 0.0))
    {
        return false;
    }

    entryAxis = 0;
    if (tyMin > txMin) entryAxis = 1;
    if (tzMin > ((entryAxis == 0) ? txMin : tyMin)) entryAxis = 2;

    float direction = entryAxis == 0 ? rayDirection.x : (entryAxis == 1 ? rayDirection.y : rayDirection.z);
    entryStepSign = direction >= 0.0 ? 1 : -1;
    return true;
}

float NextFloatToward(float value, bool towardPositive)
{
    uint bits = asuint(value);
    uint absBits = bits & 0x7fffffffu;

    if (absBits >= 0x7f800000u)
    {
        return value;
    }

    if (absBits == 0u)
    {
        return towardPositive ? asfloat(0x00000001u) : asfloat(0x80000001u);
    }

    if (towardPositive)
    {
        bits = value > 0.0 ? (bits + 1u) : (bits - 1u);
    }
    else
    {
        bits = value > 0.0 ? (bits - 1u) : (bits + 1u);
    }

    return asfloat(bits);
}

float3 BiasEntryPoint(float3 entryPoint, float3 rayDirection)
{
    float3 result = entryPoint;

    if (rayDirection.x > 0.0) result.x = NextFloatToward(result.x, true);
    else if (rayDirection.x < 0.0) result.x = NextFloatToward(result.x, false);

    if (rayDirection.y > 0.0) result.y = NextFloatToward(result.y, true);
    else if (rayDirection.y < 0.0) result.y = NextFloatToward(result.y, false);

    if (rayDirection.z > 0.0) result.z = NextFloatToward(result.z, true);
    else if (rayDirection.z < 0.0) result.z = NextFloatToward(result.z, false);

    return result;
}

bool InitDda(
    float3 rayOriginVS,
    float3 rayDirectionVS,
    int3 volumeSize,
    float tStart,
    float tEnd,
    int entryAxis,
    int entryStepSign,
    out DdaState state)
{
    float t = max(tStart, 0.0);
    if (t >= tEnd)
    {
        return false;
    }

    float3 pointEnter = rayOriginVS + rayDirectionVS * t;
    float3 samplePoint = BiasEntryPoint(pointEnter, rayDirectionVS);
    float3 interiorMax = max((float3)volumeSize - float3(1e-4, 1e-4, 1e-4), 0.0);
    samplePoint = clamp(samplePoint, 0.0, interiorMax);

    state.voxel = (int3)floor(samplePoint);
    state.step = int3(
        rayDirectionVS.x > 0.0 ? 1 : (rayDirectionVS.x < 0.0 ? -1 : 0),
        rayDirectionVS.y > 0.0 ? 1 : (rayDirectionVS.y < 0.0 ? -1 : 0),
        rayDirectionVS.z > 0.0 ? 1 : (rayDirectionVS.z < 0.0 ? -1 : 0));

    state.tDelta = float3(
        state.step.x == 0 ? kHugeDistance : abs(1.0 / rayDirectionVS.x),
        state.step.y == 0 ? kHugeDistance : abs(1.0 / rayDirectionVS.y),
        state.step.z == 0 ? kHugeDistance : abs(1.0 / rayDirectionVS.z));

    float3 nextBoundary = float3(
        state.step.x > 0 ? (state.voxel.x + 1) : state.voxel.x,
        state.step.y > 0 ? (state.voxel.y + 1) : state.voxel.y,
        state.step.z > 0 ? (state.voxel.z + 1) : state.voxel.z);

    state.tMax = float3(
        state.step.x == 0 ? kHugeDistance : (nextBoundary.x - rayOriginVS.x) / rayDirectionVS.x,
        state.step.y == 0 ? kHugeDistance : (nextBoundary.y - rayOriginVS.y) / rayDirectionVS.y,
        state.step.z == 0 ? kHugeDistance : (nextBoundary.z - rayOriginVS.z) / rayDirectionVS.z);
    state.tMax = max(state.tMax, t.xxx);

    state.t = t;
    state.tLimit = tEnd;
    state.lastAxis = entryAxis;
    state.lastStepSign = entryStepSign;
    return true;
}

float3 ComputeAxisMask(float3 sideDistance)
{
    float chooseX = step(sideDistance.x, sideDistance.y) * step(sideDistance.x, sideDistance.z);
    float chooseY = (1.0 - chooseX) * step(sideDistance.y, sideDistance.z);
    float chooseZ = 1.0 - chooseX - chooseY;
    return float3(chooseX, chooseY, chooseZ);
}

void StepDda(inout DdaState state)
{
    float3 axisMask = ComputeAxisMask(state.tMax);
    int3 axisMaskInt = (int3)round(axisMask);

    state.t = dot(axisMask, state.tMax);
    state.voxel += axisMaskInt * state.step;
    state.tMax += axisMask * state.tDelta;
    state.lastAxis = axisMaskInt.y + axisMaskInt.z * 2;
    state.lastStepSign =
        axisMaskInt.x * state.step.x +
        axisMaskInt.y * state.step.y +
        axisMaskInt.z * state.step.z;
}

uint EncodeFaceId(int axis, int stepSign)
{
    return (uint)(axis * 2 + (stepSign < 0 ? 1 : 0));
}

uint ComputeLocalVoxelIndex(int3 voxelCoord)
{
    return
        (uint)voxelCoord.x +
        (kVoxelChunkDimension * ((uint)voxelCoord.y + (kVoxelChunkDimension * (uint)voxelCoord.z)));
}

uint PackHitData(uint faceId, uint localVoxelIndex)
{
    return (localVoxelIndex << 3u) | (faceId & 0x7u);
}

uint DecodeFaceIdFromHitData(uint packedHitData)
{
    return packedHitData & 0x7u;
}

uint DecodeLocalVoxelIndex(uint packedHitData)
{
    return packedHitData >> 3u;
}

AttributeData EncodeAttributes(VoxelHit hit)
{
    AttributeData attributes;
    attributes.packedHitData = PackHitData(hit.faceId, hit.localVoxelIndex);
    return attributes;
}

float3 TransformFaceNormalToWorld(uint faceId)
{
    float3 normalOS = DecodeFaceNormal(faceId);
    float3x3 worldToObject = (float3x3)WorldToObject();
    return normalize(mul(normalOS, worldToObject));
}

bool ResolveAabbDesc(uint primitiveIndex, out VoxelBlasAabbDesc descriptor)
{
    descriptor.chunkIndex = 0;
    descriptor.localMin = 0;
    descriptor.localMax = 0;

    if ((int)primitiveIndex < 0 || (int)primitiveIndex >= _VoxelAabbCount)
    {
        return false;
    }

    descriptor = _VoxelAabbDescBuffer[primitiveIndex];
    int rawChunkIndex = descriptor.chunkIndex;
    if (rawChunkIndex < 0 || rawChunkIndex >= _VoxelChunkCount)
    {
        return false;
    }

    return true;
}

bool IsDebugAabbOverlayMaterial()
{
    return _VoxelDebugAabbOverlay >= 0.5;
}

float ResolveDebugAabbLineWidth()
{
    return max(_VoxelDebugAabbLineWidth, 1e-4);
}

bool IsPointNearDebugAabbEdge(
    float3 pointVS,
    int hitAxis,
    int3 localMin,
    int3 localMax,
    float lineWidth)
{
    float3 minVS = (float3)localMin;
    float3 maxVS = (float3)localMax;
    float2 edgeDistances;

    if (hitAxis == 0)
    {
        edgeDistances = float2(
            min(pointVS.y - minVS.y, maxVS.y - pointVS.y),
            min(pointVS.z - minVS.z, maxVS.z - pointVS.z));
    }
    else if (hitAxis == 1)
    {
        edgeDistances = float2(
            min(pointVS.x - minVS.x, maxVS.x - pointVS.x),
            min(pointVS.z - minVS.z, maxVS.z - pointVS.z));
    }
    else
    {
        edgeDistances = float2(
            min(pointVS.x - minVS.x, maxVS.x - pointVS.x),
            min(pointVS.y - minVS.y, maxVS.y - pointVS.y));
    }

    float nearestEdgeDistance = min(edgeDistances.x, edgeDistances.y);
    return nearestEdgeDistance <= lineWidth;
}

bool TryTraceDebugAabbOverlayIntersection(
    VoxelBlasAabbDesc descriptor,
    VoxelBlasAabb chunkAabb,
    float3 rayOriginOS,
    float3 rayDirectionOS,
    out float hitT,
    out AttributeData attributes)
{
    hitT = 0.0;
    attributes.packedHitData = 0u;

    float tEnter;
    float tExit;
    int entryAxis;
    int entryStepSign;
    if (!RayBox(rayOriginOS, rayDirectionOS, chunkAabb.boundsMin, chunkAabb.boundsMax, tEnter, tExit, entryAxis, entryStepSign))
    {
        return false;
    }

    float traceTMin = max(tEnter, RayTMin());
    if (traceTMin >= tExit)
    {
        return false;
    }

    float3 chunkExtent = max(chunkAabb.boundsMax - chunkAabb.boundsMin, float3(1e-6, 1e-6, 1e-6));
    float3 localMin = (float3)descriptor.localMin;
    float3 localExtent = max((float3)(descriptor.localMax - descriptor.localMin), float3(1e-6, 1e-6, 1e-6));
    float3 voxelScale = localExtent / chunkExtent;
    float3 hitPointOS = rayOriginOS + rayDirectionOS * traceTMin;
    float3 pointVS = ((hitPointOS - chunkAabb.boundsMin) * voxelScale) + localMin;
    pointVS = clamp(pointVS, localMin, (float3)descriptor.localMax);

    if (!IsPointNearDebugAabbEdge(
        pointVS,
        entryAxis,
        descriptor.localMin,
        descriptor.localMax,
        ResolveDebugAabbLineWidth()))
    {
        return false;
    }

    VoxelHit hit;
    hit.t = traceTMin;
    hit.faceId = EncodeFaceId(entryAxis, entryStepSign);
    hit.localVoxelIndex = 0u;
    hitT = hit.t;
    attributes = EncodeAttributes(hit);
    return true;
}

bool IsVoxelOccupied(uint chunkIndex, int3 voxelCoord)
{
    if (any(voxelCoord < 0) || any(voxelCoord >= int3(kVoxelChunkDimension, kVoxelChunkDimension, kVoxelChunkDimension)))
    {
        return false;
    }

    uint byteAddress = (chunkIndex * kVoxelChunkVoxelByteCount) + ComputeLocalVoxelIndex(voxelCoord);
    return ReadRawByte(_VoxelVolumeBuffer, byteAddress) != 0u;
}

uint ReadPaletteEntryIndex(uint chunkIndex, uint localVoxelIndex)
{
    uint byteAddress = (chunkIndex * kVoxelChunkVoxelByteCount) + localVoxelIndex;
    return ReadRawByte(_VoxelVolumeBuffer, byteAddress);
}

float ComputeCurrentCellExitT(DdaState state)
{
    return min(state.tMax.x, min(state.tMax.y, state.tMax.z));
}

bool TraceChunkOccupancy(
    uint chunkIndex,
    float3 rayOriginVS,
    float3 rayDirectionVS,
    float tEnter,
    float tExit,
    int entryAxis,
    int entryStepSign,
    out VoxelHit hit)
{
    hit.t = 0.0;
    hit.faceId = 0u;
    hit.localVoxelIndex = 0u;

    int3 volumeSize = int3(kVoxelChunkDimension, kVoxelChunkDimension, kVoxelChunkDimension);
    DdaState state;
    if (!InitDda(rayOriginVS, rayDirectionVS, volumeSize, tEnter, tExit, entryAxis, entryStepSign, state))
    {
        return false;
    }

    int maxStepCount = max(1, volumeSize.x + volumeSize.y + volumeSize.z + 3);

    [loop]
    for (int stepIndex = 0; stepIndex < maxStepCount; stepIndex++)
    {
        if (state.t >= state.tLimit || any(state.voxel < 0) || any(state.voxel >= volumeSize))
        {
            break;
        }

        float cellExitT = min(state.tLimit, ComputeCurrentCellExitT(state));
        if (cellExitT > state.t && IsVoxelOccupied(chunkIndex, state.voxel))
        {
            float reportT = max(state.t, kProceduralRayTMin);
            if (reportT <= cellExitT)
            {
                hit.t = reportT;
                hit.faceId = EncodeFaceId(state.lastAxis, state.lastStepSign);
                hit.localVoxelIndex = ComputeLocalVoxelIndex(state.voxel);
                return true;
            }
        }

        StepDda(state);
    }

    return false;
}

bool TryTraceProceduralIntersection(out float hitT, out AttributeData attributes)
{
    hitT = 0.0;
    attributes.packedHitData = 0u;

    uint primitiveIndex = PrimitiveIndex();
    VoxelBlasAabbDesc descriptor;
    if (!ResolveAabbDesc(primitiveIndex, descriptor))
    {
        return false;
    }
    uint chunkIndex = (uint)descriptor.chunkIndex;

    VoxelBlasAabb chunkAabb = _VoxelAabbBuffer[primitiveIndex];
    float3 rayOriginOS = ObjectRayOrigin();
    float3 rayDirectionOS = ObjectRayDirection();

    if (IsDebugAabbOverlayMaterial())
    {
        return TryTraceDebugAabbOverlayIntersection(
            descriptor,
            chunkAabb,
            rayOriginOS,
            rayDirectionOS,
            hitT,
            attributes);
    }

    float tEnter;
    float tExit;
    int entryAxis;
    int entryStepSign;
    if (!RayBox(rayOriginOS, rayDirectionOS, chunkAabb.boundsMin, chunkAabb.boundsMax, tEnter, tExit, entryAxis, entryStepSign))
    {
        return false;
    }

    float traceTMin = max(tEnter, RayTMin());
    if (traceTMin >= tExit)
    {
        return false;
    }

    float3 chunkExtent = max(chunkAabb.boundsMax - chunkAabb.boundsMin, float3(1e-6, 1e-6, 1e-6));
    float3 localMin = (float3)descriptor.localMin;
    float3 localExtent = max((float3)(descriptor.localMax - descriptor.localMin), float3(1e-6, 1e-6, 1e-6));
    float3 voxelScale = localExtent / chunkExtent;
    float3 rayOriginVS = ((rayOriginOS - chunkAabb.boundsMin) * voxelScale) + localMin;
    float3 rayDirectionVS = rayDirectionOS * voxelScale;

    VoxelHit hit;
    if (!TraceChunkOccupancy(chunkIndex, rayOriginVS, rayDirectionVS, traceTMin, tExit, entryAxis, entryStepSign, hit))
    {
        return false;
    }

    hitT = hit.t;
    attributes = EncodeAttributes(hit);
    return true;
}

void ExecuteProceduralClosestHit(
    inout RayPayload payload,
    AttributeData attributes)
{
    uint primitiveIndex = PrimitiveIndex();
    VoxelBlasAabbDesc descriptor;
    bool isValidChunk = ResolveAabbDesc(primitiveIndex, descriptor);
    uint chunkIndex = isValidChunk ? (uint)descriptor.chunkIndex : 0u;
    uint faceId = DecodeFaceIdFromHitData(attributes.packedHitData);
    uint localVoxelIndex = DecodeLocalVoxelIndex(attributes.packedHitData);
    uint paletteEntryIndex = isValidChunk ? ReadPaletteEntryIndex(chunkIndex, localVoxelIndex) : 0u;
    bool isOpaqueMaterial = _VoxelOpaqueMaterial >= 0.5;
    bool isDebugAabbOverlay = IsDebugAabbOverlayMaterial();
    bool keepScreenDoorHit = isOpaqueMaterial || ShouldKeepScreenDoorHit();

    payload.hitT = RayTCurrent();
    payload.worldNormal = TransformFaceNormalToWorld(faceId);
    payload.packedAlbedo = isDebugAabbOverlay ? ReadPaletteEntry(1u) : ReadPaletteEntry(paletteEntryIndex);
    payload.flags = kRayPayloadFlagValidHit;
    if (!isOpaqueMaterial && !isDebugAabbOverlay)
    {
        payload.flags |= kRayPayloadFlagTransparentHit;
    }

    if (keepScreenDoorHit)
    {
        payload.flags |= kRayPayloadFlagKeepScreenDoorHit;
    }
}
#endif

#endif
