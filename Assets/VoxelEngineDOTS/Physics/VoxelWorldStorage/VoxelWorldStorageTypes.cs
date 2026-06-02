using System;
using Unity.Mathematics;

namespace VoxelEngineDOTS.Physics
{
    public readonly struct VoxelWorldBodyHandle :
        IEquatable<VoxelWorldBodyHandle>
    {
        public static readonly VoxelWorldBodyHandle Invalid = new VoxelWorldBodyHandle(-1, 0);

        public VoxelWorldBodyHandle(int slot, int version)
        {
            Slot = slot;
            Version = version;
        }

        public int Slot { get; }

        public int Version { get; }

        public bool IsValid => Slot >= 0 && Version > 0;

        public bool Equals(VoxelWorldBodyHandle other)
        {
            return Slot == other.Slot && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelWorldBodyHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)math.hash(new int2(Slot, Version));
        }

        public static bool operator ==(VoxelWorldBodyHandle left, VoxelWorldBodyHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelWorldBodyHandle left, VoxelWorldBodyHandle right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct VoxelWorldChunkHandle :
        IEquatable<VoxelWorldChunkHandle>
    {
        public static readonly VoxelWorldChunkHandle Invalid = new VoxelWorldChunkHandle(-1, 0);

        public VoxelWorldChunkHandle(int slot, int version)
        {
            Slot = slot;
            Version = version;
        }

        public int Slot { get; }

        public int Version { get; }

        public bool IsValid => Slot >= 0 && Version > 0;

        public bool Equals(VoxelWorldChunkHandle other)
        {
            return Slot == other.Slot && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelWorldChunkHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)math.hash(new int2(Slot, Version));
        }

        public static bool operator ==(VoxelWorldChunkHandle left, VoxelWorldChunkHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelWorldChunkHandle left, VoxelWorldChunkHandle right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct VoxelWorldChunkKey :
        IEquatable<VoxelWorldChunkKey>
    {
        public VoxelWorldChunkKey(VoxelWorldBodyHandle bodyHandle, int3 chunkPosition)
            : this(bodyHandle.Slot, bodyHandle.Version, chunkPosition)
        {
        }

        public VoxelWorldChunkKey(int bodySlot, int bodyVersion, int3 chunkPosition)
        {
            BodySlot = bodySlot;
            BodyVersion = bodyVersion;
            ChunkPosition = chunkPosition;
        }

        public int BodySlot { get; }

        public int BodyVersion { get; }

        public int3 ChunkPosition { get; }

        public VoxelWorldBodyHandle BodyHandle =>
            new VoxelWorldBodyHandle(BodySlot, BodyVersion);

        public bool Equals(VoxelWorldChunkKey other)
        {
            return BodySlot == other.BodySlot &&
                   BodyVersion == other.BodyVersion &&
                   math.all(ChunkPosition == other.ChunkPosition);
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelWorldChunkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            uint bodyHash = math.hash(new int2(BodySlot, BodyVersion));
            uint chunkHash = math.hash(new int3(
                ChunkPosition.x,
                ChunkPosition.y,
                ChunkPosition.z));

            return (int)math.hash(new uint2(bodyHash, chunkHash));
        }
    }

    [Flags]
    public enum VoxelWorldChunkDirtyFlags : byte
    {
        None = 0,
        Solid = 1 << 0,
        Face = 1 << 1,
        Edge = 1 << 2,
        Corner = 1 << 3,
        Metadata = 1 << 4,
    }

    public readonly struct VoxelWorldBodyConfig
    {
        public VoxelWorldBodyConfig(float4x4 localToWorld, int metadata = 0)
        {
            LocalToWorld = localToWorld;
            Metadata = metadata;
        }

        public float4x4 LocalToWorld { get; }

        public int Metadata { get; }
    }

    public readonly struct VoxelWorldChunkConfig
    {
        public VoxelWorldChunkConfig(int3 chunkPosition, int metadata = 0)
        {
            ChunkPosition = chunkPosition;
            Metadata = metadata;
        }

        public int3 ChunkPosition { get; }

        public int Metadata { get; }
    }

    public struct VoxelWorldBodyRecord
    {
        public int Version;
        public byte Occupied;
        public float4x4 LocalToWorld;
        public int ChunkCount;
        public int Metadata;

        public readonly bool IsOccupied => Occupied != 0;
    }

    public struct VoxelWorldChunkRecord
    {
        public int Version;
        public byte Occupied;
        public int BodySlot;
        public int BodyVersion;
        public int3 ChunkPosition;
        public VoxelWorldChunkDirtyFlags DirtyFlags;
        public int SolidCount;
        public int Metadata;

        public readonly bool IsOccupied => Occupied != 0;

        public readonly VoxelWorldBodyHandle BodyHandle =>
            new VoxelWorldBodyHandle(BodySlot, BodyVersion);
    }
}
