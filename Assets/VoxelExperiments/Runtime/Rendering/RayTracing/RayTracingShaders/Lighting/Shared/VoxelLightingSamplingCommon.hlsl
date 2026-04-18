#ifndef VOXEL_LIGHTING_SAMPLING_COMMON_INCLUDED
#define VOXEL_LIGHTING_SAMPLING_COMMON_INCLUDED

static const float kLightingPi = 3.14159265359;
static const float kLightingTau = 6.28318530718;

#ifndef VOXEL_LIGHTING_USE_BLUE_NOISE
    #define VOXEL_LIGHTING_USE_BLUE_NOISE 0
#endif

#if VOXEL_LIGHTING_USE_BLUE_NOISE
Texture2D<float4> _LightingBlueNoiseTex;
#endif

uint HashLightingUint(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    value ^= value >> 16;
    return value;
}

uint MakeLightingSeed(uint2 pixel, uint sampleIndex, uint frameIndex)
{
    uint seed = HashLightingUint(pixel.x);
    seed = HashLightingUint(seed ^ (pixel.y * 0x9e3779b9u));
    seed = HashLightingUint(seed ^ (sampleIndex * 0x85ebca6bu));
    seed = HashLightingUint(seed ^ (frameIndex * 0xc2b2ae35u));
    return seed;
}

float NextLightingRandom01(inout uint state)
{
    state = HashLightingUint(state + 0x9e3779b9u);
    return (state & 0x00ffffffu) / 16777216.0;
}

float2 NextLightingRandom02(inout uint state)
{
    return float2(
        NextLightingRandom01(state),
        NextLightingRandom01(state));
}

void BuildLightingOrthonormalBasis(
    float3 normalWS,
    out float3 tangentWS,
    out float3 bitangentWS)
{
    float3 up = abs(normalWS.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    tangentWS = normalize(cross(up, normalWS));
    bitangentWS = cross(normalWS, tangentWS);
}

float3 TransformLightingDirectionToWorld(float3 localDirection, float3 normalWS)
{
    float3 tangentWS;
    float3 bitangentWS;
    BuildLightingOrthonormalBasis(normalWS, tangentWS, bitangentWS);
    return
        tangentWS * localDirection.x +
        bitangentWS * localDirection.y +
        normalWS * localDirection.z;
}

float3 SampleLightingCosineHemisphere(float2 sampleUv)
{
    float r = sqrt(saturate(sampleUv.x));
    float phi = kLightingTau * sampleUv.y;
    float x = r * cos(phi);
    float y = r * sin(phi);
    float z = sqrt(saturate(1.0 - sampleUv.x));
    return float3(x, y, z);
}

float3 SampleLightingUnitSphere(float2 sampleUv)
{
    float z = 1.0 - (2.0 * sampleUv.x);
    float r = sqrt(saturate(1.0 - (z * z)));
    float phi = kLightingTau * sampleUv.y;
    return float3(r * cos(phi), r * sin(phi), z);
}

float3 SampleLightingUnitSphere(inout uint state)
{
    float2 sampleUv = NextLightingRandom02(state);
    return SampleLightingUnitSphere(sampleUv);
}

#if VOXEL_LIGHTING_USE_BLUE_NOISE
float4 SampleLightingBlueNoise4(uint2 pixel, uint sampleIndex)
{
    uint2 wrapped = (pixel + uint2(sampleIndex * 29u, sampleIndex * 47u)) & 127u;
    return saturate(_LightingBlueNoiseTex.Load(int3((int2)wrapped, 0)));
}

uint MakeLightingSeedFromBlueNoise(float4 blueNoiseSample, uint sampleIndex)
{
    uint4 packed = (uint4)round(blueNoiseSample * 255.0);
    uint seed = packed.x;
    seed |= packed.y << 8u;
    seed |= packed.z << 16u;
    seed |= packed.w << 24u;
    seed = HashLightingUint(seed ^ (sampleIndex * 0x85ebca6bu));
    return seed;
}
#endif

float3 SampleLightingJitterWS(float3 normalWS, inout uint state, float radius)
{
    if (radius <= 0.0)
    {
        return 0.0;
    }

    float3 localDirection = SampleLightingUnitSphere(state);
    float scale = radius * pow(max(NextLightingRandom01(state), 1e-6), 1.0 / 3.0);
    return TransformLightingDirectionToWorld(localDirection, normalWS) * scale;
}

#endif
