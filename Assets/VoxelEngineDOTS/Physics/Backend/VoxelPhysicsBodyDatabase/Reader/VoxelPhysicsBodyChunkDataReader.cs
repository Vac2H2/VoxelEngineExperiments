using System;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngineDOTS.Physics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public readonly struct VoxelPhysicsBodyChunkKey :
        IEquatable<VoxelPhysicsBodyChunkKey>
    {
        public VoxelPhysicsBodyChunkKey(int bodyId, int3 chunkPosition)
        {
            BodyId = bodyId;
            ChunkPosition = chunkPosition;
        }

        public int BodyId { get; }
        public int3 ChunkPosition { get; }

        public bool Equals(VoxelPhysicsBodyChunkKey other)
        {
            return BodyId == other.BodyId &&
                   math.all(ChunkPosition == other.ChunkPosition);
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelPhysicsBodyChunkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)math.hash(new int4(
                BodyId,
                ChunkPosition.x,
                ChunkPosition.y,
                ChunkPosition.z));
        }
    }

    public readonly struct VoxelPhysicsBodyChunkDataReader
    {
        private readonly NativeHashMap<VoxelPhysicsBodyChunkKey, int>.ReadOnly _chunkDataIdsByKey;
        private readonly ChunkDataContainer<byte>.Reader _chunkDataReader;

        internal VoxelPhysicsBodyChunkDataReader(
            NativeHashMap<VoxelPhysicsBodyChunkKey, int>.ReadOnly chunkDataIdsByKey,
            ChunkDataContainer<byte>.Reader chunkDataReader)
        {
            _chunkDataIdsByKey = chunkDataIdsByKey;
            _chunkDataReader = chunkDataReader;
        }

        public bool IsCreated =>
            _chunkDataIdsByKey.IsCreated &&
            _chunkDataReader.IsCreated;

        public int ChunkSize => _chunkDataReader.ChunkSize;

        public bool ContainsChunk(int bodyId, int3 chunkPosition)
        {
            return IsCreated &&
                   _chunkDataIdsByKey.ContainsKey(
                       new VoxelPhysicsBodyChunkKey(bodyId, chunkPosition));
        }

        public bool TryGetChunkDataId(
            int bodyId,
            int3 chunkPosition,
            out int chunkDataId)
        {
            if (!IsCreated)
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            return _chunkDataIdsByKey.TryGetValue(
                new VoxelPhysicsBodyChunkKey(bodyId, chunkPosition),
                out chunkDataId);
        }

        public bool TryGetChunkSlice(
            int bodyId,
            int3 chunkPosition,
            out NativeSlice<byte> chunkData)
        {
            if (!TryGetChunkDataId(bodyId, chunkPosition, out int chunkDataId))
            {
                chunkData = default;
                return false;
            }

            return _chunkDataReader.TryGetChunkSlice(chunkDataId, out chunkData);
        }
    }
}
