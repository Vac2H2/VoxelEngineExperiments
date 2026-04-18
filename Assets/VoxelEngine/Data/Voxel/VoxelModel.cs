using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngine.Data.Voxel
{
    // VoxelModel packages one voxel object into the render-preferred
    // opaque/transparent split while keeping the object logical as a whole.
    public sealed class VoxelModel : IDisposable
    {
        public VoxelModel(VoxelVolume opaqueVolume, VoxelVolume transparentVolume)
        {
            OpaqueVolume = ValidateVolume(nameof(opaqueVolume), opaqueVolume);
            TransparentVolume = ValidateVolume(nameof(transparentVolume), transparentVolume);

            if (ReferenceEquals(OpaqueVolume, TransparentVolume))
            {
                throw new ArgumentException("Opaque and transparent volumes must be different VoxelVolume instances.");
            }
        }

        public static VoxelModel Create(int initialOpaqueChunkCapacity, int initialTransparentChunkCapacity, Allocator allocator)
        {
            return new VoxelModel(
                new VoxelVolume(initialOpaqueChunkCapacity, allocator),
                new VoxelVolume(initialTransparentChunkCapacity, allocator));
        }

        public VoxelVolume OpaqueVolume { get; }

        public VoxelVolume TransparentVolume { get; }

        public int ChunkCount => OpaqueVolume.ChunkCount + TransparentVolume.ChunkCount;

        public bool IsEmpty => ChunkCount == 0;

        public bool HasOpaqueChunks => OpaqueVolume.ChunkCount > 0;

        public bool HasTransparentChunks => TransparentVolume.ChunkCount > 0;

        public bool ContainsChunk(int3 chunkCoordinate)
        {
            return OpaqueVolume.ContainsChunk(chunkCoordinate) || TransparentVolume.ContainsChunk(chunkCoordinate);
        }

        public void Dispose()
        {
            OpaqueVolume.Dispose();
            TransparentVolume.Dispose();
        }

        private static VoxelVolume ValidateVolume(string paramName, VoxelVolume volume)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (!volume.IsCreated)
            {
                throw new ArgumentException("VoxelModel requires a created VoxelVolume.", paramName);
            }

            return volume;
        }
    }
}
