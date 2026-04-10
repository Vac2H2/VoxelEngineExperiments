#ifndef VOXEL_OCCUPANCY_PROCEDURAL_SHARED_INCLUDED
#define VOXEL_OCCUPANCY_PROCEDURAL_SHARED_INCLUDED

#ifndef RTAS_OCCUPANCY_SEPARATE_TRANSPARENT
    #define RTAS_OCCUPANCY_SEPARATE_TRANSPARENT 0
#endif

#ifndef VOXEL_OCCUPANCY_INCLUDE_HIT_SHADERS
    #define VOXEL_OCCUPANCY_INCLUDE_HIT_SHADERS 1
#endif

static const uint kVoxelChunkDimension = 8u;
static const uint kVoxelChunkOccupancyByteCount = 64u;
static const float kProceduralRayTMin = 1e-5;
static const float kHugeDistance = 1e30;

#if VOXEL_OCCUPANCY_INCLUDE_HIT_SHADERS
struct VoxelChunkAabbData
{
    float3 boundsMin;
    float3 boundsMax;
};

ByteAddressBuffer _VoxelOccupancyChunkBuffer;
StructuredBuffer<uint> _VoxelModelChunkStartBuffer;
StructuredBuffer<VoxelChunkAabbData> _VoxelChunkAabbBuffer;

int _VoxelModelResidencyId;
int _VoxelPaletteResidencyId;
float _VoxelOpaqueMaterial;

struct AttributeData
{
    uint packedNormal;
};
#endif

struct RayPayload
{
    uint hit;
    float hitT;
    uint packedNormalAndPaletteId;
};

#if VOXEL_OCCUPANCY_INCLUDE_HIT_SHADERS
struct VoxelHit
{
    float t;
    uint faceId;
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

bool RayBox(
    float3 ro,
    float3 rd,
    float3 bmin,
    float3 bmax,
    out float tEnter,
    out float tExit,
    out int entryAxis,
    out int entryStepSign)
{
    const float eps = 1e-8;
    const float inf = 3.402823466e+38;

    entryAxis = 0;
    entryStepSign = 1;

    float txMin;
    float txMax;
    if (abs(rd.x) < eps)
    {
        if (ro.x < bmin.x || ro.x > bmax.x) { tEnter = 0.0; tExit = -1.0; return false; }
        txMin = -inf;
        txMax = inf;
    }
    else
    {
        float tx0 = (bmin.x - ro.x) / rd.x;
        float tx1 = (bmax.x - ro.x) / rd.x;
        txMin = min(tx0, tx1);
        txMax = max(tx0, tx1);
    }

    float tyMin;
    float tyMax;
    if (abs(rd.y) < eps)
    {
        if (ro.y < bmin.y || ro.y > bmax.y) { tEnter = 0.0; tExit = -1.0; return false; }
        tyMin = -inf;
        tyMax = inf;
    }
    else
    {
        float ty0 = (bmin.y - ro.y) / rd.y;
        float ty1 = (bmax.y - ro.y) / rd.y;
        tyMin = min(ty0, ty1);
        tyMax = max(ty0, ty1);
    }

    float tzMin;
    float tzMax;
    if (abs(rd.z) < eps)
    {
        if (ro.z < bmin.z || ro.z > bmax.z) { tEnter = 0.0; tExit = -1.0; return false; }
        tzMin = -inf;
        tzMax = inf;
    }
    else
    {
        float tz0 = (bmin.z - ro.z) / rd.z;
        float tz1 = (bmax.z - ro.z) / rd.z;
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

    float dir = (entryAxis == 0) ? rd.x : (entryAxis == 1 ? rd.y : rd.z);
    entryStepSign = dir >= 0.0 ? 1 : -1;
    return true;
}

float NextFloatToward(float x, bool towardPositive)
{
    uint bits = asuint(x);
    uint absBits = bits & 0x7fffffffu;

    if (absBits >= 0x7f800000u)
        return x;

    if (absBits == 0u)
        return towardPositive ? asfloat(0x00000001u) : asfloat(0x80000001u);

    if (towardPositive)
        bits = (x > 0.0) ? (bits + 1u) : (bits - 1u);
    else
        bits = (x > 0.0) ? (bits - 1u) : (bits + 1u);

    return asfloat(bits);
}

float3 BiasEntryPoint(float3 pEnter, float3 rd)
{
    float3 p = pEnter;

    if (rd.x > 0.0) p.x = NextFloatToward(p.x, true);
    else if (rd.x < 0.0) p.x = NextFloatToward(p.x, false);

    if (rd.y > 0.0) p.y = NextFloatToward(p.y, true);
    else if (rd.y < 0.0) p.y = NextFloatToward(p.y, false);

    if (rd.z > 0.0) p.z = NextFloatToward(p.z, true);
    else if (rd.z < 0.0) p.z = NextFloatToward(p.z, false);

    return p;
}

bool InitDda(
    float3 roVS,
    float3 rdVS,
    int3 volumeSize,
    float tStart,
    float tEnd,
    int entryAxis,
    int entryStepSign,
    out DdaState state)
{
    float t = max(tStart, 0.0);
    if (t >= tEnd)
        return false;

    float3 pEnter = roVS + rdVS * t;
    float3 p = BiasEntryPoint(pEnter, rdVS);
    float3 interiorMax = max((float3)volumeSize - float3(1e-4, 1e-4, 1e-4), 0.0);
    p = clamp(p, 0.0, interiorMax);

    state.voxel = (int3)floor(p);
    state.step = int3(
        rdVS.x > 0.0 ? 1 : (rdVS.x < 0.0 ? -1 : 0),
        rdVS.y > 0.0 ? 1 : (rdVS.y < 0.0 ? -1 : 0),
        rdVS.z > 0.0 ? 1 : (rdVS.z < 0.0 ? -1 : 0));

    state.tDelta = float3(
        state.step.x == 0 ? kHugeDistance : abs(1.0 / rdVS.x),
        state.step.y == 0 ? kHugeDistance : abs(1.0 / rdVS.y),
        state.step.z == 0 ? kHugeDistance : abs(1.0 / rdVS.z));

    float3 nextBoundary = float3(
        state.step.x > 0 ? (state.voxel.x + 1) : state.voxel.x,
        state.step.y > 0 ? (state.voxel.y + 1) : state.voxel.y,
        state.step.z > 0 ? (state.voxel.z + 1) : state.voxel.z);

    state.tMax = float3(
        state.step.x == 0 ? kHugeDistance : (nextBoundary.x - roVS.x) / rdVS.x,
        state.step.y == 0 ? kHugeDistance : (nextBoundary.y - roVS.y) / rdVS.y,
        state.step.z == 0 ? kHugeDistance : (nextBoundary.z - roVS.z) / rdVS.z);
    state.tMax = max(state.tMax, t.xxx);

    state.t = t;
    state.tLimit = tEnd;
    state.lastAxis = entryAxis;
    state.lastStepSign = entryStepSign;

    return true;
}

float3 ComputeBranchlessAxisMask(float3 sideDistance)
{
    float chooseX = step(sideDistance.x, sideDistance.y) * step(sideDistance.x, sideDistance.z);
    float chooseY = (1.0 - chooseX) * step(sideDistance.y, sideDistance.z);
    float chooseZ = 1.0 - chooseX - chooseY;
    return float3(chooseX, chooseY, chooseZ);
}

void StepDda(inout DdaState state)
{
    float3 axisMask = ComputeBranchlessAxisMask(state.tMax);
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

uint ReadOccupancyByte(uint byteAddress)
{
    uint wordAddress = byteAddress & ~3u;
    uint packedWord = _VoxelOccupancyChunkBuffer.Load(wordAddress);
    uint byteShift = (byteAddress & 3u) * 8u;
    return (packedWord >> byteShift) & 0xFFu;
}

uint EncodeFaceId(int axis, int stepSign)
{
    return (uint)(axis * 2 + (stepSign < 0 ? 1 : 0));
}

AttributeData EncodeAttributes(VoxelHit hit)
{
    AttributeData attributes;
    attributes.packedNormal = hit.faceId;
    return attributes;
}
#endif

uint PackNormalAndPaletteId(uint faceId, uint paletteId)
{
    return (paletteId << 3u) | (faceId & 0x7u);
}

uint DecodeFaceId(uint packedNormalAndPaletteId)
{
    return packedNormalAndPaletteId & 0x7u;
}

uint DecodePaletteId(uint packedNormalAndPaletteId)
{
    return packedNormalAndPaletteId >> 3u;
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

#if VOXEL_OCCUPANCY_INCLUDE_HIT_SHADERS
bool IsOpaqueMaterial()
{
    return _VoxelOpaqueMaterial >= 0.5;
}

bool ShouldKeepScreenDoorHit()
{
    uint2 pixel = DispatchRaysIndex().xy;
    return ((pixel.x ^ pixel.y) & 1u) == 0u;
}

bool ShouldKeepFirstVoxelHit()
{
#if RTAS_OCCUPANCY_SEPARATE_TRANSPARENT
    return IsOpaqueMaterial() || ShouldKeepScreenDoorHit();
#else
    return true;
#endif
}

bool ShouldTerminateAfterFirstNonEmptyVoxel()
{
#if RTAS_OCCUPANCY_SEPARATE_TRANSPARENT
    return true;
#else
    return false;
#endif
}

uint ResolveChunkGlobalIndex(uint primitiveIndex, out bool isValid)
{
    isValid = false;

    if (_VoxelModelResidencyId < 0)
    {
        return 0u;
    }

    uint modelResidencyId = (uint)_VoxelModelResidencyId;
    uint startSlot = _VoxelModelChunkStartBuffer[modelResidencyId];
    if (startSlot == 0xffffffffu)
    {
        return 0u;
    }

    isValid = true;
    return startSlot + primitiveIndex;
}

bool IsVoxelOccupied(uint chunkGlobalIndex, int3 voxelCoord)
{
    if (any(voxelCoord < 0) || any(voxelCoord >= int3(kVoxelChunkDimension, kVoxelChunkDimension, kVoxelChunkDimension)))
    {
        return false;
    }

    uint byteAddress =
        (chunkGlobalIndex * kVoxelChunkOccupancyByteCount) +
        (uint)voxelCoord.y +
        (kVoxelChunkDimension * (uint)voxelCoord.z);
    uint occupancyByte = ReadOccupancyByte(byteAddress);
    return (occupancyByte & (1u << (uint)voxelCoord.x)) != 0u;
}

float ComputeCurrentCellExitT(DdaState state)
{
    return min(state.tMax.x, min(state.tMax.y, state.tMax.z));
}

bool TraceChunkOccupancy(
    uint chunkGlobalIndex,
    float3 rayOriginVS,
    float3 rayDirectionVS,
    float tEnter,
    float tExit,
    int entryAxis,
    int entryStepSign,
    out VoxelHit hit,
    out bool traversalTerminated)
{
    hit.t = 0.0;
    hit.faceId = 0u;
    traversalTerminated = false;

    int3 volumeSize = int3(kVoxelChunkDimension, kVoxelChunkDimension, kVoxelChunkDimension);
    DdaState state;
    if (!InitDda(
        rayOriginVS,
        rayDirectionVS,
        volumeSize,
        tEnter,
        tExit,
        entryAxis,
        entryStepSign,
        state))
    {
        return false;
    }

    int rootStepCount = max(1, volumeSize.x + volumeSize.y + volumeSize.z + 3);

    [loop]
    for (int stepIndex = 0; stepIndex < rootStepCount; stepIndex++)
    {
        if (state.t >= state.tLimit || any(state.voxel < 0) || any(state.voxel >= volumeSize))
        {
            break;
        }

        float cellExitT = min(state.tLimit, ComputeCurrentCellExitT(state));
        if (cellExitT > state.t && IsVoxelOccupied(chunkGlobalIndex, state.voxel))
        {
            if (ShouldKeepFirstVoxelHit())
            {
                float reportT = max(state.t, kProceduralRayTMin);
                if (reportT <= cellExitT)
                {
                    hit.t = reportT;
                    hit.faceId = EncodeFaceId(state.lastAxis, state.lastStepSign);
                    traversalTerminated = true;
                    return true;
                }
            }

            if (ShouldTerminateAfterFirstNonEmptyVoxel())
            {
                traversalTerminated = true;
                return false;
            }
        }

        StepDda(state);
    }

    return false;
}

bool TryTraceProceduralIntersection(
    out float hitT,
    out AttributeData attributes)
{
    hitT = 0.0;
    attributes.packedNormal = 0u;

    uint primitiveIndex = PrimitiveIndex();
    bool isValidChunk;
    uint chunkGlobalIndex = ResolveChunkGlobalIndex(primitiveIndex, isValidChunk);
    if (!isValidChunk)
    {
        return false;
    }

    VoxelChunkAabbData chunkAabb = _VoxelChunkAabbBuffer[primitiveIndex];
    float3 rayOriginOS = ObjectRayOrigin();
    float3 rayDirectionOS = ObjectRayDirection();

    float tEnter;
    float tExit;
    int entryAxis;
    int entryStepSign;
    if (!RayBox(rayOriginOS, rayDirectionOS, chunkAabb.boundsMin, chunkAabb.boundsMax, tEnter, tExit, entryAxis, entryStepSign))
    {
        return false;
    }

    float3 chunkExtent = max(chunkAabb.boundsMax - chunkAabb.boundsMin, float3(1e-6, 1e-6, 1e-6));
    float3 voxelScale = float3(kVoxelChunkDimension, kVoxelChunkDimension, kVoxelChunkDimension) / chunkExtent;
    float3 rayOriginVS = (rayOriginOS - chunkAabb.boundsMin) * voxelScale;
    float3 rayDirectionVS = rayDirectionOS * voxelScale;

    VoxelHit hit;
    bool traversalTerminated;
    if (!TraceChunkOccupancy(
        chunkGlobalIndex,
        rayOriginVS,
        rayDirectionVS,
        tEnter,
        tExit,
        entryAxis,
        entryStepSign,
        hit,
        traversalTerminated))
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
    uint paletteId = _VoxelPaletteResidencyId < 0 ? 0u : (uint)_VoxelPaletteResidencyId;
    payload.hit = 1u;
    payload.hitT = RayTCurrent();
    payload.packedNormalAndPaletteId = PackNormalAndPaletteId(attributes.packedNormal, paletteId);
}
#endif

#endif
