using Unity.Mathematics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public readonly struct VoxelPhysicsBodyHandle
    {
        public VoxelPhysicsBodyHandle(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public int Index { get; }

        public int Version { get; }

        public bool IsValid => Index >= 0 && Version > 0;
    }

    public readonly struct VoxelPhysicsBodyConfig
    {
        public VoxelPhysicsBodyConfig(float4x4 localToWorld)
        {
            LocalToWorld = localToWorld;
        }

        public float4x4 LocalToWorld { get; }
    }

    public interface IVoxelPhysicsBackendRegistration
    {
        bool TryCreateBody(
            in VoxelPhysicsBodyConfig config,
            out VoxelPhysicsBodyHandle bodyHandle);

        bool TryDestroyBody(VoxelPhysicsBodyHandle bodyHandle);

        bool TryAddChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition);

        bool TryRemoveChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition);

        bool ContainsBody(VoxelPhysicsBodyHandle bodyHandle);

        bool ContainsChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition);
    }
}
