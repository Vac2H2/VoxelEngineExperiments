using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelRT.Runtime.Data;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    internal readonly struct MeshVoxelizationSettings
    {
        public MeshVoxelizationSettings(float voxelSize, byte solidVoxelValue)
        {
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize), "Voxel size must be greater than zero.");
            }

            if (solidVoxelValue == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(solidVoxelValue), "Solid voxel value must be non-zero.");
            }

            VoxelSize = voxelSize;
            SolidVoxelValue = solidVoxelValue;
        }

        public float VoxelSize { get; }

        public byte SolidVoxelValue { get; }
    }

    internal sealed class MeshVoxelizationResult
    {
        public MeshVoxelizationResult(
            byte[] occupancyBytes,
            byte[] voxelBytes,
            ModelChunkAabb[] chunkAabbs)
        {
            OccupancyBytes = occupancyBytes ?? throw new ArgumentNullException(nameof(occupancyBytes));
            VoxelBytes = voxelBytes ?? throw new ArgumentNullException(nameof(voxelBytes));
            ChunkAabbs = chunkAabbs ?? throw new ArgumentNullException(nameof(chunkAabbs));
        }

        public int ChunkCount => ChunkAabbs.Length;

        public byte[] OccupancyBytes { get; }

        public byte[] VoxelBytes { get; }

        public ModelChunkAabb[] ChunkAabbs { get; }
    }

    internal static class MeshVoxelizer
    {
        private const float ProbeInflationFactor = 1.001f;
        private const int ProgressSampleInterval = 4096;
        private const int ChunkPackingProgressInterval = 64;

        public static Vector3Int CalculateGridDimensions(Bounds bounds, float voxelSize)
        {
            if (voxelSize <= 0f)
            {
                return Vector3Int.zero;
            }

            Vector3 size = bounds.size;
            return new Vector3Int(
                Math.Max(1, Mathf.CeilToInt(size.x / voxelSize)),
                Math.Max(1, Mathf.CeilToInt(size.y / voxelSize)),
                Math.Max(1, Mathf.CeilToInt(size.z / voxelSize)));
        }

        public static MeshVoxelizationResult Voxelize(Mesh sourceMesh, MeshVoxelizationSettings settings)
        {
            if (sourceMesh == null)
            {
                throw new ArgumentNullException(nameof(sourceMesh));
            }

            Bounds bounds = sourceMesh.bounds;
            Vector3Int gridDimensions = CalculateGridDimensions(bounds, settings.VoxelSize);
            if (gridDimensions.x <= 0 || gridDimensions.y <= 0 || gridDimensions.z <= 0)
            {
                throw new InvalidOperationException("Source mesh bounds produced an invalid voxel grid.");
            }

            GameObject meshProbeObject = null;
            GameObject voxelProbeObject = null;
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Preparing voxelization probes",
                    0f);

                meshProbeObject = new GameObject("MeshVoxelizerMeshProbe")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                MeshCollider meshCollider = meshProbeObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = sourceMesh;
                meshCollider.convex = false;

                voxelProbeObject = new GameObject("MeshVoxelizerVoxelProbe")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                BoxCollider voxelProbe = voxelProbeObject.AddComponent<BoxCollider>();
                voxelProbe.size = Vector3.one * (settings.VoxelSize * ProbeInflationFactor);

                List<ChunkRecord> chunks = Rasterize(meshCollider, voxelProbe, bounds, gridDimensions, settings);
                if (chunks.Count == 0)
                {
                    throw new InvalidOperationException("Voxelization produced no occupied chunks. The mesh may be empty or the voxel size may be too small or too large for the current shape.");
                }

                return BuildResult(chunks);
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (voxelProbeObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(voxelProbeObject);
                }

                if (meshProbeObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(meshProbeObject);
                }
            }
        }

        private static List<ChunkRecord> Rasterize(
            MeshCollider meshCollider,
            BoxCollider voxelProbe,
            Bounds bounds,
            Vector3Int gridDimensions,
            MeshVoxelizationSettings settings)
        {
            Dictionary<Vector3Int, int> chunkIndices = new Dictionary<Vector3Int, int>();
            List<ChunkRecord> chunks = new List<ChunkRecord>();
            Vector3 gridOrigin = bounds.min;
            long totalSampleCount = (long)gridDimensions.x * gridDimensions.y * gridDimensions.z;
            long processedSampleCount = 0;

            for (int z = 0; z < gridDimensions.z; z++)
            {
                float sampleZ = gridOrigin.z + ((z + 0.5f) * settings.VoxelSize);
                for (int y = 0; y < gridDimensions.y; y++)
                {
                    float sampleY = gridOrigin.y + ((y + 0.5f) * settings.VoxelSize);
                    for (int x = 0; x < gridDimensions.x; x++)
                    {
                        processedSampleCount++;
                        if (ShouldUpdateRasterProgress(processedSampleCount, totalSampleCount))
                        {
                            if (EditorUtility.DisplayCancelableProgressBar(
                                    "Mesh To Voxel Model",
                                    $"Voxelizing {processedSampleCount:N0} / {totalSampleCount:N0} voxels\nOccupied chunks: {chunks.Count:N0}",
                                    Mathf.Clamp01((float)processedSampleCount / totalSampleCount)))
                            {
                                throw new OperationCanceledException("Mesh voxelization was canceled.");
                            }
                        }

                        Vector3 sampleCenter = new Vector3(
                            gridOrigin.x + ((x + 0.5f) * settings.VoxelSize),
                            sampleY,
                            sampleZ);

                        if (!Physics.ComputePenetration(
                                voxelProbe,
                                sampleCenter,
                                Quaternion.identity,
                                meshCollider,
                                Vector3.zero,
                                Quaternion.identity,
                                out _,
                                out _))
                        {
                            continue;
                        }

                        Vector3Int chunkCoord = new Vector3Int(
                            x / VoxelChunkLayout.Dimension,
                            y / VoxelChunkLayout.Dimension,
                            z / VoxelChunkLayout.Dimension);
                        if (!chunkIndices.TryGetValue(chunkCoord, out int chunkIndex))
                        {
                            chunkIndex = chunks.Count;
                            chunkIndices.Add(chunkCoord, chunkIndex);
                            chunks.Add(new ChunkRecord(
                                chunkCoord,
                                new ChunkBuilder(
                                    gridOrigin + Vector3.Scale((Vector3)chunkCoord, Vector3.one * (VoxelChunkLayout.Dimension * settings.VoxelSize)),
                                    settings.VoxelSize)));
                        }

                        chunks[chunkIndex].Builder.SetVoxel(
                            x % VoxelChunkLayout.Dimension,
                            y % VoxelChunkLayout.Dimension,
                            z % VoxelChunkLayout.Dimension,
                            settings.SolidVoxelValue);
                    }
                }
            }

            return chunks;
        }

        private static MeshVoxelizationResult BuildResult(List<ChunkRecord> chunks)
        {
            byte[] occupancyBytes = new byte[chunks.Count * VoxelChunkLayout.OccupancyByteCount];
            byte[] voxelBytes = new byte[chunks.Count * VoxelChunkLayout.VoxelDataByteCount];
            ModelChunkAabb[] chunkAabbs = new ModelChunkAabb[chunks.Count];

            for (int i = 0; i < chunks.Count; i++)
            {
                if (i == 0 || i == chunks.Count - 1 || ((i + 1) % ChunkPackingProgressInterval) == 0)
                {
                    EditorUtility.DisplayProgressBar(
                        "Mesh To Voxel Model",
                        $"Packing {i + 1:N0} / {chunks.Count:N0} chunks",
                        0.9f + (0.1f * ((float)(i + 1) / chunks.Count)));
                }

                chunks[i].Builder.CopyTo(
                    occupancyBytes,
                    i * VoxelChunkLayout.OccupancyByteCount,
                    voxelBytes,
                    i * VoxelChunkLayout.VoxelDataByteCount);
                chunkAabbs[i] = chunks[i].Builder.BuildAabb();
            }

            return new MeshVoxelizationResult(occupancyBytes, voxelBytes, chunkAabbs);
        }

        private static bool ShouldUpdateRasterProgress(long processedSampleCount, long totalSampleCount)
        {
            if (processedSampleCount <= 1 || processedSampleCount >= totalSampleCount)
            {
                return true;
            }

            return (processedSampleCount % ProgressSampleInterval) == 0;
        }

        private sealed class ChunkRecord
        {
            public ChunkRecord(Vector3Int chunkCoord, ChunkBuilder builder)
            {
                ChunkCoord = chunkCoord;
                Builder = builder ?? throw new ArgumentNullException(nameof(builder));
            }

            public Vector3Int ChunkCoord { get; }

            public ChunkBuilder Builder { get; }
        }

        private sealed class ChunkBuilder
        {
            private readonly byte[] _occupancyBytes = new byte[VoxelChunkLayout.OccupancyByteCount];
            private readonly byte[] _voxelBytes = new byte[VoxelChunkLayout.VoxelDataByteCount];
            private readonly Vector3 _chunkOrigin;
            private readonly float _voxelSize;
            private bool _hasOccupancy;
            private Vector3Int _minOccupiedVoxel;
            private Vector3Int _maxOccupiedVoxel;

            public ChunkBuilder(Vector3 chunkOrigin, float voxelSize)
            {
                _chunkOrigin = chunkOrigin;
                _voxelSize = voxelSize;
            }

            public void SetVoxel(int x, int y, int z, byte value)
            {
                int voxelIndex = VoxelChunkLayout.FlattenLocalIndex(x, y, z);
                int occupancyByteIndex = voxelIndex >> 3;
                byte occupancyMask = checked((byte)(1 << (voxelIndex & 7)));

                _occupancyBytes[occupancyByteIndex] |= occupancyMask;
                _voxelBytes[voxelIndex] = value;

                Vector3Int localCoordinate = new Vector3Int(x, y, z);
                if (!_hasOccupancy)
                {
                    _minOccupiedVoxel = localCoordinate;
                    _maxOccupiedVoxel = localCoordinate;
                    _hasOccupancy = true;
                    return;
                }

                _minOccupiedVoxel = Vector3Int.Min(_minOccupiedVoxel, localCoordinate);
                _maxOccupiedVoxel = Vector3Int.Max(_maxOccupiedVoxel, localCoordinate);
            }

            public void CopyTo(
                byte[] occupancyDestination,
                int occupancyOffset,
                byte[] voxelDestination,
                int voxelOffset)
            {
                Buffer.BlockCopy(_occupancyBytes, 0, occupancyDestination, occupancyOffset, _occupancyBytes.Length);
                Buffer.BlockCopy(_voxelBytes, 0, voxelDestination, voxelOffset, _voxelBytes.Length);
            }

            public ModelChunkAabb BuildAabb()
            {
                if (!_hasOccupancy)
                {
                    throw new InvalidOperationException("Cannot build an AABB for an empty chunk.");
                }

                Vector3 min = _chunkOrigin + Vector3.Scale((Vector3)_minOccupiedVoxel, Vector3.one * _voxelSize);
                Vector3 max = _chunkOrigin + Vector3.Scale((Vector3)(_maxOccupiedVoxel + Vector3Int.one), Vector3.one * _voxelSize);
                return new ModelChunkAabb(min, max);
            }
        }
    }
}
