using Unity.Mathematics;

namespace VoxelEngineDOTS.BVH
{
    public static class Morton3D
    {
        public static uint Encode(float3 p, float3 worldMin, float3 worldMax, int bitsPerAxis)
        {
            int bits = math.clamp(bitsPerAxis, 1, 10);
            float3 extent = math.max(worldMax - worldMin, new float3(0.0001f));
            float3 normalized = math.saturate((p - worldMin) / extent);
            uint maxCoord = (uint)((1 << bits) - 1);

            uint x = (uint)math.min((int)math.floor(normalized.x * maxCoord), (int)maxCoord);
            uint y = (uint)math.min((int)math.floor(normalized.y * maxCoord), (int)maxCoord);
            uint z = (uint)math.min((int)math.floor(normalized.z * maxCoord), (int)maxCoord);

            return ExpandBits10(x) | (ExpandBits10(y) << 1) | (ExpandBits10(z) << 2);
        }

        private static uint ExpandBits10(uint value)
        {
            value &= 0x000003ffu;
            value = (value | (value << 16)) & 0x030000ffu;
            value = (value | (value << 8)) & 0x0300f00fu;
            value = (value | (value << 4)) & 0x030c30c3u;
            value = (value | (value << 2)) & 0x09249249u;
            return value;
        }
    }
}
