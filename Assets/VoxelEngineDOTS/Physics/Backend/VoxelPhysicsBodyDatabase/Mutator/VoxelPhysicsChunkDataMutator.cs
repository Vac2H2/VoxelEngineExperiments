using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using VoxelEngineDOTS.Physics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public sealed class VoxelPhysicsChunkDataMutator
    {
        private const int DefaultChunkDataWriteBatchCount = 32;

        private readonly VoxelPhysicsBodyDatabase _bodyDatabase;
        private readonly VoxelPhysicsStructuralChangeTracker _structuralChangeTracker;

        public VoxelPhysicsChunkDataMutator(
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

        public bool TryChunkWrite(
            NativeList<ChunkWriteCommand> commands,
            NativeList<byte> data)
        {
            return TryChunkWrite(
                commands,
                data,
                DefaultChunkDataWriteBatchCount);
        }

        public bool TryChunkWrite(
            NativeList<ChunkWriteCommand> commands,
            NativeList<byte> data,
            int innerloopBatchCount)
        {
            if (!_bodyDatabase.IsCreated ||
                !_structuralChangeTracker.IsCreated ||
                !commands.IsCreated ||
                !data.IsCreated ||
                innerloopBatchCount <= 0)
            {
                return false;
            }

            ChunkDataContainer<byte> chunkDataContainer = _bodyDatabase.ChunkDataContainer;
            int chunkSize = chunkDataContainer.ChunkSize;
            if (!TryGetExpectedDataLength(
                    commands.Length,
                    chunkSize,
                    out int expectedDataLength) ||
                data.Length != expectedDataLength)
            {
                return false;
            }

            if (commands.Length == 0)
            {
                return true;
            }

            if (!TryCreateChunkDataIds(commands, out NativeArray<int> chunkDataIds))
            {
                return false;
            }

            ChunkDataWriteJob job = new ChunkDataWriteJob
            {
                ChunkDataIds = chunkDataIds,
                Data = data.AsArray(),
                ChunkDataWriter = chunkDataContainer.AsWriter(),
                ChunkSize = chunkSize,
            };

            JobHandle writeHandle = job.Schedule(commands.Length, innerloopBatchCount);
            JobHandle disposeHandle = chunkDataIds.Dispose(writeHandle);
            disposeHandle.Complete();

            MarkChunkDataDirty(commands);
            return true;
        }

        public bool TryChunkDataAndBitMask(
            NativeList<ChunkWriteCommand> commands,
            NativeArray<byte> data)
        {
            return TryChunkDataAndBitMask(
                commands,
                data,
                DefaultChunkDataWriteBatchCount);
        }

        public bool TryChunkDataAndBitMask(
            NativeList<ChunkWriteCommand> commands,
            NativeArray<byte> data,
            int innerloopBatchCount)
        {
            if (!_bodyDatabase.IsCreated ||
                !_structuralChangeTracker.IsCreated ||
                !commands.IsCreated ||
                !data.IsCreated ||
                innerloopBatchCount <= 0)
            {
                return false;
            }

            ChunkDataContainer<byte> chunkDataContainer = _bodyDatabase.ChunkDataContainer;
            int chunkSize = chunkDataContainer.ChunkSize;
            if (chunkSize % ChunkDataAndBitMaskJob.RowBitCount != 0)
            {
                return false;
            }

            int bitMaskBytesPerChunk = chunkSize / ChunkDataAndBitMaskJob.RowBitCount;
            if (!TryGetExpectedDataLength(
                    commands.Length,
                    bitMaskBytesPerChunk,
                    out int expectedDataLength) ||
                data.Length != expectedDataLength)
            {
                return false;
            }

            if (commands.Length == 0)
            {
                return true;
            }

            if (!TryCreateChunkDataIds(commands, out NativeArray<int> chunkDataIds))
            {
                return false;
            }

            ChunkDataAndBitMaskJob job = new ChunkDataAndBitMaskJob
            {
                ChunkDataIds = chunkDataIds,
                Data = data,
                ChunkDataWriter = chunkDataContainer.AsWriter(),
                BitMaskBytesPerChunk = bitMaskBytesPerChunk,
            };

            JobHandle writeHandle = job.Schedule(commands.Length, innerloopBatchCount);
            JobHandle disposeHandle = chunkDataIds.Dispose(writeHandle);
            disposeHandle.Complete();

            MarkChunkDataDirty(commands);
            return true;
        }

        private bool TryCreateChunkDataIds(
            NativeList<ChunkWriteCommand> commands,
            out NativeArray<int> chunkDataIds)
        {
            chunkDataIds = new NativeArray<int>(
                commands.Length,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            HashSet<int> uniqueChunkDataIds = new HashSet<int>(commands.Length);
            for (int i = 0; i < commands.Length; i++)
            {
                ChunkWriteCommand command = commands[i];
                if (!_bodyDatabase.TryGetChunkDataId(
                        command.BodyHandle,
                        command.ChunkPosition,
                        out int chunkDataId) ||
                    !uniqueChunkDataIds.Add(chunkDataId))
                {
                    chunkDataIds.Dispose();
                    chunkDataIds = default;
                    return false;
                }

                chunkDataIds[i] = chunkDataId;
            }

            return true;
        }

        private void MarkChunkDataDirty(NativeList<ChunkWriteCommand> commands)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                ChunkWriteCommand command = commands[i];
                _structuralChangeTracker.MarkChunkBoundsDirty(
                    command.BodyHandle,
                    command.ChunkPosition);
            }
        }

        private static bool TryGetExpectedDataLength(
            int commandCount,
            int chunkSize,
            out int expectedDataLength)
        {
            try
            {
                expectedDataLength = checked(commandCount * chunkSize);
                return true;
            }
            catch (OverflowException)
            {
                expectedDataLength = 0;
                return false;
            }
        }
    }
}
