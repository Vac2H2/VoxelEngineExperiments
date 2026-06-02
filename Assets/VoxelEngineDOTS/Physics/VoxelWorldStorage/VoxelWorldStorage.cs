using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineDOTS.Physics
{
    public sealed class VoxelWorldStorage : IDisposable
    {
        public const int ChunkSize = VoxelEngineConstants.ChunkSize;
        public const int VoxelsPerChunk = VoxelEngineConstants.VoxelsPerChunk;
        public const int BitPlaneBytesPerChunk = 64;
        public const int InvalidSlot = -1;

        private const string ContainerName = "VoxelWorldStorage";

        private NativeList<VoxelWorldBodyRecord> _bodies;
        private NativeList<VoxelWorldChunkRecord> _chunks;
        private NativeParallelHashMap<VoxelWorldChunkKey, int> _chunkLookup;
        private NativeList<int> _freeBodySlots;
        private NativeList<int> _freeChunkSlots;
        private NativeList<byte> _isSolid;
        private NativeList<byte> _isFace;
        private NativeList<byte> _isEdge;
        private NativeList<byte> _isCorner;
        private int _bodyCount;
        private int _chunkCount;
        private bool _isDisposed;

        public VoxelWorldStorage(
            int initialBodyCapacity,
            int initialChunkCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateInitialCapacity(initialBodyCapacity, nameof(initialBodyCapacity));
            ValidateInitialCapacity(initialChunkCapacity, nameof(initialChunkCapacity));

            _bodies = new NativeList<VoxelWorldBodyRecord>(initialBodyCapacity, allocator);
            _chunks = new NativeList<VoxelWorldChunkRecord>(initialChunkCapacity, allocator);
            _chunkLookup = new NativeParallelHashMap<VoxelWorldChunkKey, int>(
                initialChunkCapacity,
                allocator);
            _freeBodySlots = new NativeList<int>(initialBodyCapacity, allocator);
            _freeChunkSlots = new NativeList<int>(initialChunkCapacity, allocator);
            _isSolid = new NativeList<byte>(
                checked(initialChunkCapacity * BitPlaneBytesPerChunk),
                allocator);
            _isFace = new NativeList<byte>(
                checked(initialChunkCapacity * BitPlaneBytesPerChunk),
                allocator);
            _isEdge = new NativeList<byte>(
                checked(initialChunkCapacity * BitPlaneBytesPerChunk),
                allocator);
            _isCorner = new NativeList<byte>(
                checked(initialChunkCapacity * BitPlaneBytesPerChunk),
                allocator);
        }

        public bool IsCreated =>
            !_isDisposed &&
            _bodies.IsCreated &&
            _chunks.IsCreated &&
            _chunkLookup.IsCreated &&
            _freeBodySlots.IsCreated &&
            _freeChunkSlots.IsCreated &&
            _isSolid.IsCreated &&
            _isFace.IsCreated &&
            _isEdge.IsCreated &&
            _isCorner.IsCreated;

        public int BodyCount
        {
            get
            {
                ThrowIfDisposed();
                return _bodyCount;
            }
        }

        public int BodySlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _bodies.Length;
            }
        }

        public int ChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return _chunkCount;
            }
        }

        public int ChunkSlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _chunks.Length;
            }
        }

        public int FreeBodySlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _freeBodySlots.Length;
            }
        }

        public int FreeChunkSlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _freeChunkSlots.Length;
            }
        }

        public int BitPlaneLength
        {
            get
            {
                ThrowIfDisposed();
                return _isSolid.Length;
            }
        }

        public bool TryAddBody(
            in VoxelWorldBodyConfig config,
            out VoxelWorldBodyHandle bodyHandle)
        {
            ThrowIfDisposed();

            int bodySlot = AllocateBodySlot();
            VoxelWorldBodyRecord previousRecord = _bodies[bodySlot];
            int version = previousRecord.Version > 0 ? previousRecord.Version : 1;

            _bodies[bodySlot] = new VoxelWorldBodyRecord
            {
                Version = version,
                Occupied = 1,
                LocalToWorld = config.LocalToWorld,
                ChunkCount = 0,
                Metadata = config.Metadata,
            };

            _bodyCount++;
            bodyHandle = new VoxelWorldBodyHandle(bodySlot, version);
            return true;
        }

        public bool TryRemoveBody(VoxelWorldBodyHandle bodyHandle)
        {
            ThrowIfDisposed();

            if (!TryGetOccupiedBodySlot(bodyHandle, out int bodySlot))
            {
                return false;
            }

            for (int chunkSlot = 0; chunkSlot < _chunks.Length; chunkSlot++)
            {
                VoxelWorldChunkRecord chunk = _chunks[chunkSlot];
                if (!chunk.IsOccupied ||
                    chunk.BodySlot != bodyHandle.Slot ||
                    chunk.BodyVersion != bodyHandle.Version)
                {
                    continue;
                }

                ReleaseChunkSlot(chunkSlot, removeLookup: true, decrementBodyChunkCount: false);
            }

            VoxelWorldBodyRecord body = _bodies[bodySlot];
            body.Version = GetNextVersion(body.Version);
            body.Occupied = 0;
            body.ChunkCount = 0;
            body.Metadata = 0;
            _bodies[bodySlot] = body;

            _freeBodySlots.Add(bodySlot);
            _bodyCount--;
            return true;
        }

        public bool ContainsBody(VoxelWorldBodyHandle bodyHandle)
        {
            ThrowIfDisposed();
            return TryGetOccupiedBodySlot(bodyHandle, out _);
        }

        public bool TryGetBodyRecord(
            VoxelWorldBodyHandle bodyHandle,
            out VoxelWorldBodyRecord body)
        {
            ThrowIfDisposed();

            if (!TryGetOccupiedBodySlot(bodyHandle, out int bodySlot))
            {
                body = default;
                return false;
            }

            body = _bodies[bodySlot];
            return true;
        }

        public bool TrySetBodyTransform(
            VoxelWorldBodyHandle bodyHandle,
            float4x4 localToWorld)
        {
            ThrowIfDisposed();

            if (!TryGetOccupiedBodySlot(bodyHandle, out int bodySlot))
            {
                return false;
            }

            VoxelWorldBodyRecord body = _bodies[bodySlot];
            body.LocalToWorld = localToWorld;
            _bodies[bodySlot] = body;
            return true;
        }

        public bool TryAddChunk(
            VoxelWorldBodyHandle bodyHandle,
            int3 chunkPosition,
            out VoxelWorldChunkHandle chunkHandle)
        {
            return TryAddChunk(
                bodyHandle,
                new VoxelWorldChunkConfig(chunkPosition),
                out chunkHandle);
        }

        public bool TryAddChunk(
            VoxelWorldBodyHandle bodyHandle,
            in VoxelWorldChunkConfig config,
            out VoxelWorldChunkHandle chunkHandle)
        {
            ThrowIfDisposed();

            if (!TryGetOccupiedBodySlot(bodyHandle, out int bodySlot))
            {
                chunkHandle = VoxelWorldChunkHandle.Invalid;
                return false;
            }

            VoxelWorldChunkKey key = new VoxelWorldChunkKey(bodyHandle, config.ChunkPosition);
            if (_chunkLookup.ContainsKey(key))
            {
                chunkHandle = VoxelWorldChunkHandle.Invalid;
                return false;
            }

            int chunkSlot = AllocateChunkSlot();
            VoxelWorldChunkRecord previousRecord = _chunks[chunkSlot];
            int version = previousRecord.Version > 0 ? previousRecord.Version : 1;

            EnsureChunkLookupCapacity(_chunkLookup.Count() + 1);
            if (!_chunkLookup.TryAdd(key, chunkSlot))
            {
                _freeChunkSlots.Add(chunkSlot);
                chunkHandle = VoxelWorldChunkHandle.Invalid;
                return false;
            }

            _chunks[chunkSlot] = new VoxelWorldChunkRecord
            {
                Version = version,
                Occupied = 1,
                BodySlot = bodyHandle.Slot,
                BodyVersion = bodyHandle.Version,
                ChunkPosition = config.ChunkPosition,
                DirtyFlags = VoxelWorldChunkDirtyFlags.None,
                SolidCount = 0,
                Metadata = config.Metadata,
            };

            ClearChunkBitPlanes(chunkSlot);

            VoxelWorldBodyRecord body = _bodies[bodySlot];
            body.ChunkCount++;
            _bodies[bodySlot] = body;

            _chunkCount++;
            chunkHandle = new VoxelWorldChunkHandle(chunkSlot, version);
            return true;
        }

        public bool TryRemoveChunk(
            VoxelWorldBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            return TryRemoveChunk(bodyHandle, chunkPosition, out _);
        }

        public bool TryRemoveChunk(
            VoxelWorldBodyHandle bodyHandle,
            int3 chunkPosition,
            out VoxelWorldChunkHandle chunkHandle)
        {
            ThrowIfDisposed();

            if (!TryGetOccupiedBodySlot(bodyHandle, out _))
            {
                chunkHandle = VoxelWorldChunkHandle.Invalid;
                return false;
            }

            if (!TryGetChunkSlot(bodyHandle, chunkPosition, out int chunkSlot))
            {
                chunkHandle = VoxelWorldChunkHandle.Invalid;
                return false;
            }

            VoxelWorldChunkRecord chunk = _chunks[chunkSlot];
            chunkHandle = new VoxelWorldChunkHandle(chunkSlot, chunk.Version);
            if (ReleaseChunkSlot(
                chunkSlot,
                removeLookup: true,
                decrementBodyChunkCount: true))
            {
                return true;
            }

            chunkHandle = VoxelWorldChunkHandle.Invalid;
            return false;
        }

        public bool ContainsChunk(
            VoxelWorldBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            ThrowIfDisposed();

            return TryGetChunkSlot(bodyHandle, chunkPosition, out _);
        }

        public bool TryGetChunkHandle(
            VoxelWorldBodyHandle bodyHandle,
            int3 chunkPosition,
            out VoxelWorldChunkHandle chunkHandle)
        {
            ThrowIfDisposed();

            if (!TryGetChunkSlot(bodyHandle, chunkPosition, out int chunkSlot))
            {
                chunkHandle = VoxelWorldChunkHandle.Invalid;
                return false;
            }

            chunkHandle = new VoxelWorldChunkHandle(chunkSlot, _chunks[chunkSlot].Version);
            return true;
        }

        public bool TryGetChunkRecord(
            VoxelWorldChunkHandle chunkHandle,
            out VoxelWorldChunkRecord chunk)
        {
            ThrowIfDisposed();

            if (!TryGetOccupiedChunkSlot(chunkHandle, out int chunkSlot))
            {
                chunk = default;
                return false;
            }

            chunk = _chunks[chunkSlot];
            return true;
        }

        public bool TryGetChunkRecord(
            VoxelWorldBodyHandle bodyHandle,
            int3 chunkPosition,
            out VoxelWorldChunkRecord chunk)
        {
            ThrowIfDisposed();

            if (!TryGetChunkSlot(bodyHandle, chunkPosition, out int chunkSlot))
            {
                chunk = default;
                return false;
            }

            chunk = _chunks[chunkSlot];
            return true;
        }

        public bool TrySetChunkDirtyFlags(
            VoxelWorldChunkHandle chunkHandle,
            VoxelWorldChunkDirtyFlags dirtyFlags)
        {
            ThrowIfDisposed();

            if (!TryGetOccupiedChunkSlot(chunkHandle, out int chunkSlot))
            {
                return false;
            }

            VoxelWorldChunkRecord chunk = _chunks[chunkSlot];
            chunk.DirtyFlags = dirtyFlags;
            _chunks[chunkSlot] = chunk;
            return true;
        }

        public bool TrySetChunkSolidCount(
            VoxelWorldChunkHandle chunkHandle,
            int solidCount)
        {
            ThrowIfDisposed();
            ValidateSolidCount(solidCount);

            if (!TryGetOccupiedChunkSlot(chunkHandle, out int chunkSlot))
            {
                return false;
            }

            VoxelWorldChunkRecord chunk = _chunks[chunkSlot];
            chunk.SolidCount = solidCount;
            _chunks[chunkSlot] = chunk;
            return true;
        }

        public VoxelWorldParallelView GetParallelView()
        {
            ThrowIfDisposed();

            return new VoxelWorldParallelView
            {
                Bodies = _bodies.AsArray(),
                Chunks = _chunks.AsArray(),
                ChunkLookup = _chunkLookup.AsReadOnly(),
                IsSolid = _isSolid.AsArray(),
                IsFace = _isFace.AsArray(),
                IsEdge = _isEdge.AsArray(),
                IsCorner = _isCorner.AsArray(),
            };
        }

        public void Clear()
        {
            ThrowIfDisposed();

            _chunkLookup.Clear();
            _freeBodySlots.Clear();
            _freeChunkSlots.Clear();

            for (int bodySlot = 0; bodySlot < _bodies.Length; bodySlot++)
            {
                VoxelWorldBodyRecord body = _bodies[bodySlot];
                if (body.IsOccupied)
                {
                    body.Version = GetNextVersion(body.Version);
                }

                body.Occupied = 0;
                body.ChunkCount = 0;
                body.Metadata = 0;
                _bodies[bodySlot] = body;
                _freeBodySlots.Add(bodySlot);
            }

            for (int chunkSlot = 0; chunkSlot < _chunks.Length; chunkSlot++)
            {
                VoxelWorldChunkRecord chunk = _chunks[chunkSlot];
                if (chunk.IsOccupied)
                {
                    chunk.Version = GetNextVersion(chunk.Version);
                }

                chunk.Occupied = 0;
                chunk.BodySlot = InvalidSlot;
                chunk.BodyVersion = 0;
                chunk.DirtyFlags = VoxelWorldChunkDirtyFlags.None;
                chunk.SolidCount = 0;
                chunk.Metadata = 0;
                _chunks[chunkSlot] = chunk;
                _freeChunkSlots.Add(chunkSlot);
            }

            ClearBitPlane(_isSolid);
            ClearBitPlane(_isFace);
            ClearBitPlane(_isEdge);
            ClearBitPlane(_isCorner);

            _bodyCount = 0;
            _chunkCount = 0;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_bodies.IsCreated)
            {
                _bodies.Dispose();
            }

            if (_chunks.IsCreated)
            {
                _chunks.Dispose();
            }

            if (_chunkLookup.IsCreated)
            {
                _chunkLookup.Dispose();
            }

            if (_freeBodySlots.IsCreated)
            {
                _freeBodySlots.Dispose();
            }

            if (_freeChunkSlots.IsCreated)
            {
                _freeChunkSlots.Dispose();
            }

            if (_isSolid.IsCreated)
            {
                _isSolid.Dispose();
            }

            if (_isFace.IsCreated)
            {
                _isFace.Dispose();
            }

            if (_isEdge.IsCreated)
            {
                _isEdge.Dispose();
            }

            if (_isCorner.IsCreated)
            {
                _isCorner.Dispose();
            }

            _bodyCount = 0;
            _chunkCount = 0;
            _isDisposed = true;
        }

        public static int GetBitPlaneByteIndex(int chunkSlot, int3 localVoxelPosition)
        {
            ValidateChunkSlot(chunkSlot);
            ValidateLocalVoxelPosition(localVoxelPosition);

            int lineIndex = localVoxelPosition.y + localVoxelPosition.z * ChunkSize;
            return checked(chunkSlot * BitPlaneBytesPerChunk + lineIndex);
        }

        public static byte GetBitMask(int localX)
        {
            if ((uint)localX >= (uint)ChunkSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localX),
                    "Local voxel x coordinate must be in [0, 7].");
            }

            return (byte)(1 << localX);
        }

        private int AllocateBodySlot()
        {
            if (_freeBodySlots.Length > 0)
            {
                int freeListIndex = _freeBodySlots.Length - 1;
                int bodySlot = _freeBodySlots[freeListIndex];
                _freeBodySlots.RemoveAt(freeListIndex);
                return bodySlot;
            }

            int newSlot = _bodies.Length;
            _bodies.Add(new VoxelWorldBodyRecord
            {
                Version = 1,
            });
            return newSlot;
        }

        private int AllocateChunkSlot()
        {
            if (_freeChunkSlots.Length > 0)
            {
                int freeListIndex = _freeChunkSlots.Length - 1;
                int chunkSlot = _freeChunkSlots[freeListIndex];
                _freeChunkSlots.RemoveAt(freeListIndex);
                return chunkSlot;
            }

            int newSlot = _chunks.Length;
            _chunks.Add(new VoxelWorldChunkRecord
            {
                Version = 1,
                BodySlot = InvalidSlot,
            });
            AppendBitPlaneSlot(_isSolid);
            AppendBitPlaneSlot(_isFace);
            AppendBitPlaneSlot(_isEdge);
            AppendBitPlaneSlot(_isCorner);
            return newSlot;
        }

        private bool ReleaseChunkSlot(
            int chunkSlot,
            bool removeLookup,
            bool decrementBodyChunkCount)
        {
            if ((uint)chunkSlot >= (uint)_chunks.Length)
            {
                return false;
            }

            VoxelWorldChunkRecord chunk = _chunks[chunkSlot];
            if (!chunk.IsOccupied)
            {
                return false;
            }

            if (removeLookup)
            {
                _chunkLookup.Remove(new VoxelWorldChunkKey(
                    chunk.BodySlot,
                    chunk.BodyVersion,
                    chunk.ChunkPosition));
            }

            if (decrementBodyChunkCount &&
                (uint)chunk.BodySlot < (uint)_bodies.Length)
            {
                VoxelWorldBodyRecord body = _bodies[chunk.BodySlot];
                if (body.IsOccupied && body.Version == chunk.BodyVersion)
                {
                    body.ChunkCount = math.max(0, body.ChunkCount - 1);
                    _bodies[chunk.BodySlot] = body;
                }
            }

            chunk.Version = GetNextVersion(chunk.Version);
            chunk.Occupied = 0;
            chunk.BodySlot = InvalidSlot;
            chunk.BodyVersion = 0;
            chunk.ChunkPosition = default;
            chunk.DirtyFlags = VoxelWorldChunkDirtyFlags.None;
            chunk.SolidCount = 0;
            chunk.Metadata = 0;
            _chunks[chunkSlot] = chunk;

            _freeChunkSlots.Add(chunkSlot);
            _chunkCount--;
            return true;
        }

        private bool TryGetOccupiedBodySlot(
            VoxelWorldBodyHandle bodyHandle,
            out int bodySlot)
        {
            if (!bodyHandle.IsValid || (uint)bodyHandle.Slot >= (uint)_bodies.Length)
            {
                bodySlot = InvalidSlot;
                return false;
            }

            VoxelWorldBodyRecord body = _bodies[bodyHandle.Slot];
            if (!body.IsOccupied || body.Version != bodyHandle.Version)
            {
                bodySlot = InvalidSlot;
                return false;
            }

            bodySlot = bodyHandle.Slot;
            return true;
        }

        private bool TryGetOccupiedChunkSlot(
            VoxelWorldChunkHandle chunkHandle,
            out int chunkSlot)
        {
            if (!chunkHandle.IsValid || (uint)chunkHandle.Slot >= (uint)_chunks.Length)
            {
                chunkSlot = InvalidSlot;
                return false;
            }

            VoxelWorldChunkRecord chunk = _chunks[chunkHandle.Slot];
            if (!chunk.IsOccupied || chunk.Version != chunkHandle.Version)
            {
                chunkSlot = InvalidSlot;
                return false;
            }

            chunkSlot = chunkHandle.Slot;
            return true;
        }

        private bool TryGetChunkSlot(
            VoxelWorldBodyHandle bodyHandle,
            int3 chunkPosition,
            out int chunkSlot)
        {
            if (!TryGetOccupiedBodySlot(bodyHandle, out _))
            {
                chunkSlot = InvalidSlot;
                return false;
            }

            if (!_chunkLookup.TryGetValue(
                    new VoxelWorldChunkKey(bodyHandle, chunkPosition),
                    out chunkSlot))
            {
                chunkSlot = InvalidSlot;
                return false;
            }

            VoxelWorldChunkRecord chunk = _chunks[chunkSlot];
            if (!chunk.IsOccupied ||
                chunk.BodySlot != bodyHandle.Slot ||
                chunk.BodyVersion != bodyHandle.Version ||
                !math.all(chunk.ChunkPosition == chunkPosition))
            {
                chunkSlot = InvalidSlot;
                return false;
            }

            return true;
        }

        private void EnsureChunkLookupCapacity(int requiredCapacity)
        {
            if (_chunkLookup.Capacity >= requiredCapacity)
            {
                return;
            }

            int nextCapacity = math.max(requiredCapacity, math.max(1, _chunkLookup.Capacity * 2));
            _chunkLookup.Capacity = nextCapacity;
        }

        private void ClearChunkBitPlanes(int chunkSlot)
        {
            int start = checked(chunkSlot * BitPlaneBytesPerChunk);
            int end = start + BitPlaneBytesPerChunk;

            for (int i = start; i < end; i++)
            {
                _isSolid[i] = 0;
                _isFace[i] = 0;
                _isEdge[i] = 0;
                _isCorner[i] = 0;
            }
        }

        private static void AppendBitPlaneSlot(NativeList<byte> bitPlane)
        {
            int oldLength = bitPlane.Length;
            bitPlane.Resize(
                oldLength + BitPlaneBytesPerChunk,
                NativeArrayOptions.ClearMemory);
        }

        private static void ClearBitPlane(NativeList<byte> bitPlane)
        {
            for (int i = 0; i < bitPlane.Length; i++)
            {
                bitPlane[i] = 0;
            }
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        private static int GetNextVersion(int version)
        {
            return version == int.MaxValue ? 1 : version + 1;
        }

        private static void ValidateInitialCapacity(int initialCapacity, string parameterName)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Initial capacity must be greater than zero.");
            }
        }

        private static void ValidateChunkSlot(int chunkSlot)
        {
            if (chunkSlot < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSlot),
                    "Chunk slot must be non-negative.");
            }
        }

        private static void ValidateLocalVoxelPosition(int3 localVoxelPosition)
        {
            if ((uint)localVoxelPosition.x >= (uint)ChunkSize ||
                (uint)localVoxelPosition.y >= (uint)ChunkSize ||
                (uint)localVoxelPosition.z >= (uint)ChunkSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localVoxelPosition),
                    "Local voxel coordinates must be in [0, 7].");
            }
        }

        private static void ValidateSolidCount(int solidCount)
        {
            if ((uint)solidCount > (uint)VoxelsPerChunk)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(solidCount),
                    "Solid count must be in [0, 512].");
            }
        }
    }
}
