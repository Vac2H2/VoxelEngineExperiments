using System;
using Unity.Mathematics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public sealed class VoxelPhysicsStructuralMutator
    {
        private readonly VoxelPhysicsBodyDatabase _bodyDatabase;
        private readonly VoxelPhysicsStructuralChangeTracker _structuralChangeTracker;

        public VoxelPhysicsStructuralMutator(
            VoxelPhysicsBodyDatabase bodyDatabase,
            VoxelPhysicsStructuralChangeTracker structuralChangeTracker)
        {
            _bodyDatabase = bodyDatabase ?? throw new ArgumentNullException(nameof(bodyDatabase));
            _structuralChangeTracker = structuralChangeTracker ??
                throw new ArgumentNullException(nameof(structuralChangeTracker));
        }

        public VoxelPhysicsBodyDatabase BodyDatabase => _bodyDatabase;

        public VoxelPhysicsStructuralChangeTracker StructuralChangeTracker =>
            _structuralChangeTracker;

        public VoxelPhysicsBody RequestBody()
        {
            VoxelPhysicsBody body = _bodyDatabase.RequestBody();
            _structuralChangeTracker.MarkBodyCreated(body.Handle);
            return body;
        }

        public bool TryDestroyBody(VoxelPhysicsBodyHandle bodyHandle)
        {
            if (!_bodyDatabase.TryDestroyBody(bodyHandle))
            {
                return false;
            }

            _structuralChangeTracker.MarkBodyDestroyed(bodyHandle);
            return true;
        }

        public bool TryAddChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            return TryAddChunk(bodyHandle, chunkPosition, out _);
        }

        public bool TryAddChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            out int chunkDataId)
        {
            if (!_bodyDatabase.TryAddChunk(bodyHandle, chunkPosition, out chunkDataId))
            {
                return false;
            }

            _structuralChangeTracker.MarkChunkAdded(bodyHandle, chunkPosition);
            return true;
        }

        public bool TryRemoveChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            return TryRemoveChunk(bodyHandle, chunkPosition, out _);
        }

        public bool TryRemoveChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            out int chunkDataId)
        {
            if (!_bodyDatabase.TryRemoveChunk(bodyHandle, chunkPosition, out chunkDataId))
            {
                return false;
            }

            _structuralChangeTracker.MarkChunkRemoved(bodyHandle, chunkPosition);
            return true;
        }

        public bool TryMarkChunkBoundsDirty(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            if (!_bodyDatabase.ContainsChunk(bodyHandle, chunkPosition))
            {
                return false;
            }

            _structuralChangeTracker.MarkChunkBoundsDirty(bodyHandle, chunkPosition);
            return true;
        }
    }
}
