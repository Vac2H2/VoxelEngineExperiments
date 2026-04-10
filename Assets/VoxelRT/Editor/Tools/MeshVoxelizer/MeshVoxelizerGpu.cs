using System;
using UnityEditor;
using UnityEngine;
using VoxelRT.Runtime.Data;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    internal static class MeshVoxelizerGpu
    {
        private const string ComputeShaderAssetPath = "Assets/VoxelRT/Editor/Tools/MeshVoxelizer/MeshVoxelizer.compute";
        private const string KernelName = "CSMain";
        private const int OccupancyWordCountPerChunk = VoxelChunkLayout.OccupancyByteCount / sizeof(uint);

        public static MeshVoxelizationResult Voxelize(Mesh sourceMesh, MeshVoxelizationSettings settings)
        {
            if (sourceMesh == null)
            {
                throw new ArgumentNullException(nameof(sourceMesh));
            }

            if (!SystemInfo.supportsComputeShaders)
            {
                throw new InvalidOperationException("This editor runtime does not support compute shaders.");
            }

            ComputeShader computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderAssetPath);
            if (computeShader == null)
            {
                throw new InvalidOperationException($"Missing compute shader asset at '{ComputeShaderAssetPath}'.");
            }

            Bounds bounds = sourceMesh.bounds;
            Vector3Int gridDimensions = MeshVoxelizer.CalculateGridDimensions(bounds, settings.VoxelSize);
            Vector3Int chunkDimensions = new Vector3Int(
                DivideRoundUp(gridDimensions.x, VoxelChunkLayout.Dimension),
                DivideRoundUp(gridDimensions.y, VoxelChunkLayout.Dimension),
                DivideRoundUp(gridDimensions.z, VoxelChunkLayout.Dimension));
            int totalChunkCount = checked(chunkDimensions.x * chunkDimensions.y * chunkDimensions.z);
            if (totalChunkCount <= 0)
            {
                throw new InvalidOperationException("Source mesh produced an invalid chunk grid.");
            }

            Vector3[] vertices = sourceMesh.vertices;
            int[] indices = sourceMesh.triangles;
            uint[] packedIndices = new uint[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                packedIndices[i] = checked((uint)indices[i]);
            }

            ComputeBuffer vertexBuffer = null;
            ComputeBuffer indexBuffer = null;
            ComputeBuffer chunkOccupancyBuffer = null;
            ComputeBuffer chunkFlagsBuffer = null;

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Uploading mesh data to GPU",
                    0.05f);

                vertexBuffer = new ComputeBuffer(vertices.Length, sizeof(float) * 3);
                indexBuffer = new ComputeBuffer(packedIndices.Length, sizeof(uint));
                chunkOccupancyBuffer = new ComputeBuffer(totalChunkCount * OccupancyWordCountPerChunk, sizeof(uint));
                chunkFlagsBuffer = new ComputeBuffer(totalChunkCount, sizeof(uint));

                vertexBuffer.SetData(vertices);
                indexBuffer.SetData(packedIndices);
                chunkOccupancyBuffer.SetData(new uint[totalChunkCount * OccupancyWordCountPerChunk]);
                chunkFlagsBuffer.SetData(new uint[totalChunkCount]);

                int kernel = computeShader.FindKernel(KernelName);
                computeShader.SetInts("_GridDimensions", gridDimensions.x, gridDimensions.y, gridDimensions.z);
                computeShader.SetInts("_ChunkDimensions", chunkDimensions.x, chunkDimensions.y, chunkDimensions.z);
                computeShader.SetVector("_BoundsMin", bounds.min);
                computeShader.SetFloat("_VoxelSize", settings.VoxelSize);
                computeShader.SetInt("_TriangleCount", packedIndices.Length / 3);
                computeShader.SetBuffer(kernel, "_Vertices", vertexBuffer);
                computeShader.SetBuffer(kernel, "_Indices", indexBuffer);
                computeShader.SetBuffer(kernel, "_ChunkOccupancyWords", chunkOccupancyBuffer);
                computeShader.SetBuffer(kernel, "_ChunkFlags", chunkFlagsBuffer);

                uint threadGroupSizeX;
                uint threadGroupSizeY;
                uint threadGroupSizeZ;
                computeShader.GetKernelThreadGroupSizes(kernel, out threadGroupSizeX, out threadGroupSizeY, out threadGroupSizeZ);

                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Dispatching GPU voxelization",
                    0.25f);

                computeShader.Dispatch(
                    kernel,
                    DivideRoundUp(gridDimensions.x, checked((int)threadGroupSizeX)),
                    DivideRoundUp(gridDimensions.y, checked((int)threadGroupSizeY)),
                    DivideRoundUp(gridDimensions.z, checked((int)threadGroupSizeZ)));

                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Reading GPU occupancy data",
                    0.55f);

                uint[] occupancyWords = new uint[totalChunkCount * OccupancyWordCountPerChunk];
                uint[] chunkFlags = new uint[totalChunkCount];
                chunkOccupancyBuffer.GetData(occupancyWords);

                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Reading GPU chunk flags",
                    0.7f);

                chunkFlagsBuffer.GetData(chunkFlags);

                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Compacting non-empty chunks",
                    0.82f);

                return CompactToResult(
                    occupancyWords,
                    chunkFlags,
                    chunkDimensions,
                    bounds.min,
                    settings.VoxelSize,
                    settings.SolidVoxelValue);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                chunkFlagsBuffer?.Dispose();
                chunkOccupancyBuffer?.Dispose();
                indexBuffer?.Dispose();
                vertexBuffer?.Dispose();
            }
        }

        private static MeshVoxelizationResult CompactToResult(
            uint[] occupancyWords,
            uint[] chunkFlags,
            Vector3Int chunkDimensions,
            Vector3 boundsMin,
            float voxelSize,
            byte solidVoxelValue)
        {
            int occupiedChunkCount = 0;
            for (int i = 0; i < chunkFlags.Length; i++)
            {
                if (chunkFlags[i] != 0u)
                {
                    occupiedChunkCount++;
                }
            }

            if (occupiedChunkCount == 0)
            {
                throw new InvalidOperationException("Voxelization produced no occupied chunks. The mesh may be empty or the voxel size may be too small or too large for the current shape.");
            }

            byte[] occupancyBytes = new byte[occupiedChunkCount * VoxelChunkLayout.OccupancyByteCount];
            byte[] voxelBytes = new byte[occupiedChunkCount * VoxelChunkLayout.VoxelDataByteCount];
            ModelChunkAabb[] chunkAabbs = new ModelChunkAabb[occupiedChunkCount];

            int outputChunkIndex = 0;
            for (int chunkLinearIndex = 0; chunkLinearIndex < chunkFlags.Length; chunkLinearIndex++)
            {
                if (chunkFlags[chunkLinearIndex] == 0u)
                {
                    continue;
                }

                int sourceWordOffset = chunkLinearIndex * OccupancyWordCountPerChunk;
                int occupancyByteOffset = outputChunkIndex * VoxelChunkLayout.OccupancyByteCount;
                int voxelByteOffset = outputChunkIndex * VoxelChunkLayout.VoxelDataByteCount;

                for (int wordIndex = 0; wordIndex < OccupancyWordCountPerChunk; wordIndex++)
                {
                    uint word = occupancyWords[sourceWordOffset + wordIndex];
                    int baseByteOffset = occupancyByteOffset + (wordIndex * sizeof(uint));
                    occupancyBytes[baseByteOffset] = (byte)(word & 0xFFu);
                    occupancyBytes[baseByteOffset + 1] = (byte)((word >> 8) & 0xFFu);
                    occupancyBytes[baseByteOffset + 2] = (byte)((word >> 16) & 0xFFu);
                    occupancyBytes[baseByteOffset + 3] = (byte)((word >> 24) & 0xFFu);
                }

                BuildVoxelBytesAndAabb(
                    occupancyWords,
                    sourceWordOffset,
                    voxelBytes,
                    voxelByteOffset,
                    boundsMin,
                    voxelSize,
                    solidVoxelValue,
                    LinearToChunkCoord(chunkLinearIndex, chunkDimensions),
                    out ModelChunkAabb chunkAabb);

                chunkAabbs[outputChunkIndex] = chunkAabb;
                outputChunkIndex++;
            }

            return new MeshVoxelizationResult(occupancyBytes, voxelBytes, chunkAabbs);
        }

        private static void BuildVoxelBytesAndAabb(
            uint[] occupancyWords,
            int sourceWordOffset,
            byte[] voxelBytes,
            int voxelByteOffset,
            Vector3 boundsMin,
            float voxelSize,
            byte solidVoxelValue,
            Vector3Int chunkCoord,
            out ModelChunkAabb chunkAabb)
        {
            bool hasOccupancy = false;
            Vector3Int minLocal = default;
            Vector3Int maxLocal = default;

            for (int localIndex = 0; localIndex < VoxelChunkLayout.VoxelCount; localIndex++)
            {
                uint word = occupancyWords[sourceWordOffset + (localIndex >> 5)];
                bool occupied = (word & (1u << (localIndex & 31))) != 0u;
                voxelBytes[voxelByteOffset + localIndex] = occupied ? solidVoxelValue : (byte)0;

                if (!occupied)
                {
                    continue;
                }

                Vector3Int localCoordinate = new Vector3Int(
                    localIndex % VoxelChunkLayout.Dimension,
                    (localIndex / VoxelChunkLayout.Dimension) % VoxelChunkLayout.Dimension,
                    localIndex / (VoxelChunkLayout.Dimension * VoxelChunkLayout.Dimension));

                if (!hasOccupancy)
                {
                    minLocal = localCoordinate;
                    maxLocal = localCoordinate;
                    hasOccupancy = true;
                }
                else
                {
                    minLocal = Vector3Int.Min(minLocal, localCoordinate);
                    maxLocal = Vector3Int.Max(maxLocal, localCoordinate);
                }
            }

            if (!hasOccupancy)
            {
                throw new InvalidOperationException("GPU chunk compaction encountered a chunk flagged as occupied without any occupancy bits.");
            }

            Vector3 chunkOrigin = boundsMin + Vector3.Scale((Vector3)chunkCoord, Vector3.one * (VoxelChunkLayout.Dimension * voxelSize));
            Vector3 min = chunkOrigin + Vector3.Scale((Vector3)minLocal, Vector3.one * voxelSize);
            Vector3 max = chunkOrigin + Vector3.Scale((Vector3)(maxLocal + Vector3Int.one), Vector3.one * voxelSize);
            chunkAabb = new ModelChunkAabb(min, max);
        }

        private static Vector3Int LinearToChunkCoord(int chunkLinearIndex, Vector3Int chunkDimensions)
        {
            int chunkX = chunkLinearIndex % chunkDimensions.x;
            int yz = chunkLinearIndex / chunkDimensions.x;
            int chunkY = yz % chunkDimensions.y;
            int chunkZ = yz / chunkDimensions.y;
            return new Vector3Int(chunkX, chunkY, chunkZ);
        }

        private static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
