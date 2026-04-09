using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.SurfaceTypeTableService
{
    public sealed class SurfaceTypeTableService : ISurfaceTypeTableService
    {
        public const uint FixedEntryCount = 256;
        public const uint FixedEntryStrideBytes = sizeof(uint);

        private GraphicsBuffer _surfaceTypeTableBuffer;

        public SurfaceTypeTableService()
        {
            _surfaceTypeTableBuffer = CreateBuffer();
        }

        public GraphicsBuffer SurfaceTypeTableBuffer => _surfaceTypeTableBuffer;

        public uint SurfaceTypeTableStrideBytes => FixedEntryStrideBytes;

        public uint SurfaceTypeEntryCount => FixedEntryCount;

        public void Update(NativeArray<uint> packedEntries)
        {
            if (!_surfaceTypeTableBuffer.IsValid())
            {
                throw new ObjectDisposedException(nameof(SurfaceTypeTableService));
            }

            if (!packedEntries.IsCreated)
            {
                throw new ArgumentException("Surface type entries must be created.", nameof(packedEntries));
            }

            if ((uint)packedEntries.Length != FixedEntryCount)
            {
                throw new ArgumentException(
                    $"Expected {FixedEntryCount} packed surface type entries, got {packedEntries.Length}.",
                    nameof(packedEntries));
            }

            _surfaceTypeTableBuffer.SetData(packedEntries);
        }

        public void Dispose()
        {
            _surfaceTypeTableBuffer?.Dispose();
            _surfaceTypeTableBuffer = null;
        }

        private static GraphicsBuffer CreateBuffer()
        {
            return new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                checked((int)FixedEntryCount),
                checked((int)FixedEntryStrideBytes));
        }
    }
}
