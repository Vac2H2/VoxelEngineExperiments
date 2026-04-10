using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using VoxelRT.Runtime.Data;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    internal static class MeshVoxelizerGpu
    {
        private static readonly string TraceLogPath = Path.Combine(Environment.CurrentDirectory, "Temp", "MeshVoxelizerTrace.log");
        private static readonly int[] TrailingZeroCountTable =
        {
            0, 1, 28, 2, 29, 14, 24, 3,
            30, 22, 20, 15, 25, 17, 4, 8,
            31, 27, 13, 23, 21, 19, 16, 7,
            26, 12, 18, 6, 11, 5, 10, 9,
        };

        private const string ComputeShaderAssetPath = "Assets/VoxelRT/Editor/Tools/MeshVoxelizer/MeshVoxelizer.compute";
        private const string ClearKernelName = "ClearWordsMain";
        private const string SurfaceKernelName = "ConservativeSurfaceMain";
        private const int WordBitCount = sizeof(uint) * 8;
        private const int TriangleThreadGroupSize = 64;
        private const int WordThreadGroupSize = 64;
        private const int FloodPadding = 2;
        private const int TrianglePackingLogInterval = 262144;
        private const int FloodFillProgressInterval = 131072;
        private const int CompactionProgressInterval = 128;

        [StructLayout(LayoutKind.Sequential)]
        private struct TriangleData
        {
            public Vector4 V0;
            public Vector4 V1;
            public Vector4 V2;
        }

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
            Vector3Int outputGridDimensions = MeshVoxelizer.CalculateGridDimensions(bounds, settings.VoxelSize);
            if (outputGridDimensions.x <= 0 || outputGridDimensions.y <= 0 || outputGridDimensions.z <= 0)
            {
                throw new InvalidOperationException("Source mesh bounds produced an invalid voxel grid.");
            }

            Vector3Int surfaceGridDimensions = outputGridDimensions + (Vector3Int.one * (FloodPadding * 2));
            Vector3 surfaceGridOffset = Vector3.one * (FloodPadding * settings.VoxelSize);

            Stopwatch totalStopwatch = Stopwatch.StartNew();
            Stopwatch stageStopwatch = Stopwatch.StartNew();
            long outputVoxelCount = (long)outputGridDimensions.x * outputGridDimensions.y * outputGridDimensions.z;
            long surfaceVoxelCount = (long)surfaceGridDimensions.x * surfaceGridDimensions.y * surfaceGridDimensions.z;
            LogStage(
                "Start",
                totalStopwatch,
                $"mesh={sourceMesh.name}, voxelSize={settings.VoxelSize}, outputGrid={FormatVector(outputGridDimensions)}, outputVoxels={outputVoxelCount:N0}, surfaceGrid={FormatVector(surfaceGridDimensions)}, surfaceVoxels={surfaceVoxelCount:N0}");
            LogStage("BuildTriangleData begin", totalStopwatch);
            TriangleData[] triangleData = BuildTriangleData(sourceMesh, bounds.min, surfaceGridOffset);
            long buildTrianglesMilliseconds = stageStopwatch.ElapsedMilliseconds;
            if (triangleData.Length == 0)
            {
                throw new InvalidOperationException("Source mesh has no triangles.");
            }
            LogStage(
                "BuildTriangleData end",
                totalStopwatch,
                $"triangles={triangleData.Length:N0}, triangleBytes={ComputeTriangleBytes(triangleData.Length):N0}");

            int wordsPerRow = MeshVoxelizer.DivideRoundUp(surfaceGridDimensions.x, WordBitCount);
            int totalWordCount = checked(wordsPerRow * surfaceGridDimensions.y * surfaceGridDimensions.z);

            ComputeBuffer triangleBuffer = null;
            ComputeBuffer surfaceBuffer = null;
            long uploadMilliseconds = 0L;
            long clearDispatchMilliseconds = 0L;
            long surfaceDispatchMilliseconds = 0L;
            long readbackMilliseconds = 0L;
            long floodFillMilliseconds = 0L;
            long compactionMilliseconds = 0L;

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Uploading mesh triangles to GPU",
                    0.05f);

                LogStage(
                    "Create buffers begin",
                    totalStopwatch,
                    $"wordsPerRow={wordsPerRow:N0}, totalWordCount={totalWordCount:N0}, surfaceBufferBytes={ComputeWordBytes(totalWordCount):N0}");
                triangleBuffer = new ComputeBuffer(triangleData.Length, Marshal.SizeOf<TriangleData>());
                surfaceBuffer = new ComputeBuffer(totalWordCount, sizeof(uint));
                LogStage("Create buffers end", totalStopwatch);

                LogStage("Upload triangle buffer begin", totalStopwatch);
                triangleBuffer.SetData(triangleData);
                LogStage("Upload triangle buffer end", totalStopwatch);

                LogStage("Find kernels begin", totalStopwatch);
                int clearKernel = computeShader.FindKernel(ClearKernelName);
                int surfaceKernel = computeShader.FindKernel(SurfaceKernelName);
                LogStage("Find kernels end", totalStopwatch);

                LogStage("Bind parameters begin", totalStopwatch);
                SetCommonParameters(
                    computeShader,
                    triangleBuffer,
                    surfaceGridDimensions,
                    wordsPerRow,
                    settings.VoxelSize,
                    triangleData.Length);
                LogStage("Bind parameters end", totalStopwatch);
                uploadMilliseconds = stageStopwatch.ElapsedMilliseconds - buildTrianglesMilliseconds;

                computeShader.SetInt("_ClearWordCount", totalWordCount);
                computeShader.SetBuffer(clearKernel, "_ClearTarget", surfaceBuffer);
                computeShader.SetBuffer(clearKernel, "_SurfaceWords", surfaceBuffer);

                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Clearing dense surface buffer on GPU",
                    0.14f);

                LogStage("Dispatch clear begin", totalStopwatch);
                computeShader.Dispatch(
                    clearKernel,
                    MeshVoxelizer.DivideRoundUp(totalWordCount, WordThreadGroupSize),
                    1,
                    1);
                LogStage("Dispatch clear end", totalStopwatch);
                clearDispatchMilliseconds = stageStopwatch.ElapsedMilliseconds - buildTrianglesMilliseconds - uploadMilliseconds;

                computeShader.SetBuffer(surfaceKernel, "_SurfaceWords", surfaceBuffer);

                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Conservative surface voxelization",
                    0.24f);

                LogStage(
                    "Dispatch surface begin",
                    totalStopwatch,
                    $"threadGroups={MeshVoxelizer.DivideRoundUp(triangleData.Length, TriangleThreadGroupSize):N0}");
                computeShader.Dispatch(
                    surfaceKernel,
                    MeshVoxelizer.DivideRoundUp(triangleData.Length, TriangleThreadGroupSize),
                    1,
                    1);
                LogStage("Dispatch surface end", totalStopwatch);
                surfaceDispatchMilliseconds = stageStopwatch.ElapsedMilliseconds - buildTrianglesMilliseconds - uploadMilliseconds - clearDispatchMilliseconds;

                EditorUtility.DisplayProgressBar(
                    "Mesh To Voxel Model",
                    "Downloading dense surface volume",
                    0.56f);

                LogStage("Readback begin", totalStopwatch, "GetData will block until queued GPU work completes");
                uint[] surfaceWords = new uint[totalWordCount];
                surfaceBuffer.GetData(surfaceWords);
                LogStage("Readback end", totalStopwatch);
                readbackMilliseconds = stageStopwatch.ElapsedMilliseconds - buildTrianglesMilliseconds - uploadMilliseconds - clearDispatchMilliseconds - surfaceDispatchMilliseconds;

                LogStage("Flood fill begin", totalStopwatch);
                uint[] exteriorWords = FloodFillExteriorVolume(surfaceWords, surfaceGridDimensions);
                LogStage("Flood fill end", totalStopwatch);
                floodFillMilliseconds = stageStopwatch.ElapsedMilliseconds - buildTrianglesMilliseconds - uploadMilliseconds - clearDispatchMilliseconds - surfaceDispatchMilliseconds - readbackMilliseconds;

                LogStage("Compaction begin", totalStopwatch);
                MeshVoxelizationResult result = CompactExteriorVolume(
                    exteriorWords,
                    surfaceGridDimensions,
                    outputGridDimensions,
                    new Vector3Int(FloodPadding, FloodPadding, FloodPadding),
                    settings.VoxelSize,
                    settings.SolidVoxelValue);
                LogStage("Compaction end", totalStopwatch, $"chunks={result.ChunkCount:N0}");
                compactionMilliseconds = stageStopwatch.ElapsedMilliseconds - buildTrianglesMilliseconds - uploadMilliseconds - clearDispatchMilliseconds - surfaceDispatchMilliseconds - readbackMilliseconds - floodFillMilliseconds;

            EmitDebugLog(
                    $"MeshVoxelizer timings | triangles pack {buildTrianglesMilliseconds} ms | upload {uploadMilliseconds} ms | clear {clearDispatchMilliseconds} ms | surface dispatch {surfaceDispatchMilliseconds} ms | readback {readbackMilliseconds} ms | flood fill {floodFillMilliseconds} ms | compact {compactionMilliseconds} ms | total {totalStopwatch.ElapsedMilliseconds} ms");
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                surfaceBuffer?.Dispose();
                triangleBuffer?.Dispose();
            }
        }

        private static void SetCommonParameters(
            ComputeShader computeShader,
            ComputeBuffer triangleBuffer,
            Vector3Int gridDimensions,
            int wordsPerRow,
            float voxelSize,
            int triangleCount)
        {
            LogStage("SetCommonParameters _GridDimensions begin", null);
            computeShader.SetInts("_GridDimensions", gridDimensions.x, gridDimensions.y, gridDimensions.z);
            LogStage("SetCommonParameters _GridDimensions end", null);
            LogStage("SetCommonParameters _WordsPerRow begin", null, $"value={wordsPerRow}");
            computeShader.SetInt("_WordsPerRow", wordsPerRow);
            LogStage("SetCommonParameters _WordsPerRow end", null);
            LogStage("SetCommonParameters _VoxelSize begin", null, $"value={voxelSize}");
            computeShader.SetFloat("_VoxelSize", voxelSize);
            LogStage("SetCommonParameters _VoxelSize end", null);
            LogStage("SetCommonParameters _TriCount begin", null, $"value={triangleCount}");
            computeShader.SetInt("_TriCount", triangleCount);
            LogStage("SetCommonParameters _TriCount end", null);
            LogStage("SetCommonParameters bind clear _TriRecords begin", null);
            computeShader.SetBuffer(computeShader.FindKernel(ClearKernelName), "_TriRecords", triangleBuffer);
            LogStage("SetCommonParameters bind clear _TriRecords end", null);
            LogStage("SetCommonParameters bind surface _TriRecords begin", null);
            computeShader.SetBuffer(computeShader.FindKernel(SurfaceKernelName), "_TriRecords", triangleBuffer);
            LogStage("SetCommonParameters bind surface _TriRecords end", null);
        }

        private static TriangleData[] BuildTriangleData(Mesh sourceMesh, Vector3 boundsMin, Vector3 gridOffset)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            LogStage("BuildTriangleData vertices fetch begin", null);
            Vector3[] vertices = sourceMesh.vertices;
            LogStage("BuildTriangleData vertices fetch end", null, $"count={vertices.Length:N0}, t={stopwatch.ElapsedMilliseconds} ms");
            LogStage("BuildTriangleData indices fetch begin", null);
            int[] indices = sourceMesh.triangles;
            LogStage("BuildTriangleData indices fetch end", null, $"count={indices.Length:N0}, t={stopwatch.ElapsedMilliseconds} ms");
            LogStage("BuildTriangleData allocate triangle array begin", null, $"triangleCount={indices.Length / 3:N0}");
            TriangleData[] triangleData = new TriangleData[indices.Length / 3];
            LogStage("BuildTriangleData allocate triangle array end", null, $"t={stopwatch.ElapsedMilliseconds} ms");
            LogStage("BuildTriangleData pack loop begin", null);

            for (int triangleIndex = 0; triangleIndex < triangleData.Length; triangleIndex++)
            {
                int baseIndex = triangleIndex * 3;
                Vector3 v0 = (vertices[indices[baseIndex]] - boundsMin) + gridOffset;
                Vector3 v1 = (vertices[indices[baseIndex + 1]] - boundsMin) + gridOffset;
                Vector3 v2 = (vertices[indices[baseIndex + 2]] - boundsMin) + gridOffset;

                triangleData[triangleIndex] = new TriangleData
                {
                    V0 = new Vector4(v0.x, v0.y, v0.z, 0f),
                    V1 = new Vector4(v1.x, v1.y, v1.z, 0f),
                    V2 = new Vector4(v2.x, v2.y, v2.z, 0f),
                };

                if (triangleIndex == 0
                    || triangleIndex == triangleData.Length - 1
                    || ((triangleIndex + 1) % TrianglePackingLogInterval) == 0)
                {
                    LogStage(
                        "BuildTriangleData pack loop progress",
                        null,
                        $"packed={triangleIndex + 1:N0}/{triangleData.Length:N0}, t={stopwatch.ElapsedMilliseconds} ms");
                }
            }

            LogStage("BuildTriangleData pack loop end", null, $"t={stopwatch.ElapsedMilliseconds} ms");
            return triangleData;
        }

        private static uint[] FloodFillExteriorVolume(uint[] surfaceWords, Vector3Int gridDimensions)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            long totalVoxelCount = (long)gridDimensions.x * gridDimensions.y * gridDimensions.z;
            if (totalVoxelCount <= 0L || totalVoxelCount > int.MaxValue)
            {
                throw new InvalidOperationException("Dense voxel volume is too large for flood fill.");
            }

            LogStage("FloodFillExteriorVolume setup begin", null, $"grid={FormatVector(gridDimensions)}, voxels={totalVoxelCount:N0}");
            int wordsPerRow = MeshVoxelizer.DivideRoundUp(gridDimensions.x, WordBitCount);
            uint[] exteriorWords = new uint[surfaceWords.Length];
            Queue<int> queue = new Queue<int>((int)Math.Min(totalVoxelCount, 65536L));
            LogStage("FloodFillExteriorVolume setup end", null, $"wordsPerRow={wordsPerRow:N0}, exteriorWordCount={exteriorWords.Length:N0}, t={stopwatch.ElapsedMilliseconds} ms");

            EditorUtility.DisplayProgressBar(
                "Mesh To Voxel Model",
                "Seeding padded exterior boundary",
                0.64f);

            LogStage("FloodFillExteriorVolume seed boundary begin", null);
            SeedBoundaryExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue);
            LogStage("FloodFillExteriorVolume seed boundary end", null, $"queue={queue.Count:N0}, t={stopwatch.ElapsedMilliseconds} ms");

            int processedExteriorCount = 0;
            while (queue.Count > 0)
            {
                int encodedVoxel = queue.Dequeue();
                processedExteriorCount++;

                if (processedExteriorCount == 1
                    || queue.Count == 0
                    || (processedExteriorCount % FloodFillProgressInterval) == 0)
                {
                    float progress = 0.66f + (0.18f * Mathf.Clamp01(processedExteriorCount / (float)totalVoxelCount));
                    EditorUtility.DisplayProgressBar(
                        "Mesh To Voxel Model",
                        $"Flood filling exterior {processedExteriorCount:N0} / {totalVoxelCount:N0} voxels",
                        progress);
                    LogStage(
                        "FloodFillExteriorVolume progress",
                        null,
                        $"processed={processedExteriorCount:N0}/{totalVoxelCount:N0}, queue={queue.Count:N0}, t={stopwatch.ElapsedMilliseconds} ms");
                }

                DecodeVoxelIndex(encodedVoxel, gridDimensions, out int x, out int y, out int z);

                TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x - 1, y, z);
                TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x + 1, y, z);
                TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, y - 1, z);
                TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, y + 1, z);
                TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, y, z - 1);
                TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, y, z + 1);
            }

            LogStage("FloodFillExteriorVolume end", null, $"processed={processedExteriorCount:N0}, t={stopwatch.ElapsedMilliseconds} ms");
            return exteriorWords;
        }

        private static void SeedBoundaryExterior(
            uint[] surfaceWords,
            uint[] exteriorWords,
            Vector3Int gridDimensions,
            int wordsPerRow,
            Queue<int> queue)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int maxX = gridDimensions.x - 1;
            int maxY = gridDimensions.y - 1;
            int maxZ = gridDimensions.z - 1;

            LogStage("SeedBoundaryExterior x-faces begin", null);
            for (int z = 0; z < gridDimensions.z; z++)
            {
                for (int y = 0; y < gridDimensions.y; y++)
                {
                    TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, 0, y, z);
                    TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, maxX, y, z);
                }
            }
            LogStage("SeedBoundaryExterior x-faces end", null, $"queue={queue.Count:N0}, t={stopwatch.ElapsedMilliseconds} ms");

            LogStage("SeedBoundaryExterior y-faces begin", null);
            for (int z = 0; z < gridDimensions.z; z++)
            {
                for (int x = 0; x < gridDimensions.x; x++)
                {
                    TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, 0, z);
                    TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, maxY, z);
                }
            }
            LogStage("SeedBoundaryExterior y-faces end", null, $"queue={queue.Count:N0}, t={stopwatch.ElapsedMilliseconds} ms");

            LogStage("SeedBoundaryExterior z-faces begin", null);
            for (int y = 0; y < gridDimensions.y; y++)
            {
                for (int x = 0; x < gridDimensions.x; x++)
                {
                    TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, y, 0);
                    TryEnqueueExterior(surfaceWords, exteriorWords, gridDimensions, wordsPerRow, queue, x, y, maxZ);
                }
            }
            LogStage("SeedBoundaryExterior z-faces end", null, $"queue={queue.Count:N0}, t={stopwatch.ElapsedMilliseconds} ms");
        }

        private static bool TryEnqueueExterior(
            uint[] surfaceWords,
            uint[] exteriorWords,
            Vector3Int gridDimensions,
            int wordsPerRow,
            Queue<int> queue,
            int x,
            int y,
            int z)
        {
            if ((uint)x >= (uint)gridDimensions.x
                || (uint)y >= (uint)gridDimensions.y
                || (uint)z >= (uint)gridDimensions.z)
            {
                return false;
            }

            if (IsVoxelSet(surfaceWords, wordsPerRow, gridDimensions.y, x, y, z)
                || IsVoxelSet(exteriorWords, wordsPerRow, gridDimensions.y, x, y, z))
            {
                return false;
            }

            SetVoxelBit(exteriorWords, wordsPerRow, gridDimensions.y, x, y, z);
            queue.Enqueue(EncodeVoxelIndex(x, y, z, gridDimensions));
            return true;
        }

        private static MeshVoxelizationResult CompactExteriorVolume(
            uint[] exteriorWords,
            Vector3Int sourceGridDimensions,
            Vector3Int outputGridDimensions,
            Vector3Int sourceGridOffset,
            float voxelSize,
            byte solidVoxelValue)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            LogStage(
                "CompactExteriorVolume setup begin",
                null,
                $"sourceGrid={FormatVector(sourceGridDimensions)}, outputGrid={FormatVector(outputGridDimensions)}, sourceOffset={FormatVector(sourceGridOffset)}");
            int wordsPerRow = MeshVoxelizer.DivideRoundUp(sourceGridDimensions.x, WordBitCount);
            Vector3Int chunkDimensions = MeshVoxelizer.CalculateChunkDimensions(outputGridDimensions);
            int totalRowCount = checked(outputGridDimensions.y * outputGridDimensions.z);

            Dictionary<int, CompactChunkBuilder> chunkBuilders = new Dictionary<int, CompactChunkBuilder>();
            List<int> chunkKeys = new List<int>();
            LogStage(
                "CompactExteriorVolume setup end",
                null,
                $"wordsPerRow={wordsPerRow:N0}, chunkGrid={FormatVector(chunkDimensions)}, totalRows={totalRowCount:N0}, t={stopwatch.ElapsedMilliseconds} ms");

            int processedRowCount = 0;
            for (int sourceZ = sourceGridOffset.z; sourceZ < sourceGridOffset.z + outputGridDimensions.z; sourceZ++)
            {
                int outputZ = sourceZ - sourceGridOffset.z;
                for (int sourceY = sourceGridOffset.y; sourceY < sourceGridOffset.y + outputGridDimensions.y; sourceY++)
                {
                    processedRowCount++;
                    if (processedRowCount == 1
                        || processedRowCount == totalRowCount
                        || (processedRowCount % CompactionProgressInterval) == 0)
                    {
                        float progress = 0.86f + (0.14f * ((float)processedRowCount / totalRowCount));
                        EditorUtility.DisplayProgressBar(
                            "Mesh To Voxel Model",
                            $"Compacting dense volume rows {processedRowCount:N0} / {totalRowCount:N0}",
                            progress);
                        LogStage(
                            "CompactExteriorVolume progress",
                            null,
                            $"rows={processedRowCount:N0}/{totalRowCount:N0}, chunks={chunkBuilders.Count:N0}, t={stopwatch.ElapsedMilliseconds} ms");
                    }

                    int outputY = sourceY - sourceGridOffset.y;
                    int rowBase = ((sourceZ * sourceGridDimensions.y) + sourceY) * wordsPerRow;
                    for (int wordIndex = 0; wordIndex < wordsPerRow; wordIndex++)
                    {
                        uint solidWord = CreateValidMask(wordIndex, sourceGridDimensions.x) & ~exteriorWords[rowBase + wordIndex];
                        while (solidWord != 0u)
                        {
                            int bitIndex = CountTrailingZeroBits(solidWord);
                            int sourceX = (wordIndex * WordBitCount) + bitIndex;
                            int outputX = sourceX - sourceGridOffset.x;

                            if ((uint)outputX < (uint)outputGridDimensions.x)
                            {
                                Vector3Int chunkCoord = new Vector3Int(
                                    outputX / VoxelChunkLayout.Dimension,
                                    outputY / VoxelChunkLayout.Dimension,
                                    outputZ / VoxelChunkLayout.Dimension);
                                int chunkKey = FlattenChunkIndex(chunkCoord, chunkDimensions);
                                if (!chunkBuilders.TryGetValue(chunkKey, out CompactChunkBuilder chunkBuilder))
                                {
                                    chunkBuilder = new CompactChunkBuilder(chunkCoord);
                                    chunkBuilders.Add(chunkKey, chunkBuilder);
                                    chunkKeys.Add(chunkKey);
                                }

                                int localX = outputX - (chunkCoord.x * VoxelChunkLayout.Dimension);
                                int localY = outputY - (chunkCoord.y * VoxelChunkLayout.Dimension);
                                int localZ = outputZ - (chunkCoord.z * VoxelChunkLayout.Dimension);
                                int voxelIndex = VoxelChunkLayout.FlattenVoxelDataIndex(localX, localY, localZ);
                                int occupancyByteIndex = VoxelChunkLayout.ComputeOccupancyByteIndex(localX, localY, localZ);
                                byte occupancyMask = VoxelChunkLayout.ComputeOccupancyBitMask(localX);

                                chunkBuilder.OccupancyBytes[occupancyByteIndex] |= occupancyMask;
                                chunkBuilder.VoxelBytes[voxelIndex] = solidVoxelValue;
                            }

                            solidWord &= solidWord - 1u;
                        }
                    }
                }
            }

            if (chunkBuilders.Count == 0)
            {
                throw new InvalidOperationException("Voxelization produced no occupied chunks.");
            }

            LogStage("CompactExteriorVolume sort chunk keys begin", null, $"chunkCount={chunkBuilders.Count:N0}");
            chunkKeys.Sort();
            LogStage("CompactExteriorVolume sort chunk keys end", null, $"t={stopwatch.ElapsedMilliseconds} ms");

            byte[] occupancyBytes = new byte[chunkKeys.Count * VoxelChunkLayout.OccupancyByteCount];
            byte[] voxelBytes = new byte[chunkKeys.Count * VoxelChunkLayout.VoxelDataByteCount];
            ModelChunkAabb[] chunkAabbs = new ModelChunkAabb[chunkKeys.Count];
            LogStage("CompactExteriorVolume allocate output arrays end", null, $"t={stopwatch.ElapsedMilliseconds} ms");

            LogStage("CompactExteriorVolume finalize chunks begin", null);
            for (int i = 0; i < chunkKeys.Count; i++)
            {
                CompactChunkBuilder chunkBuilder = chunkBuilders[chunkKeys[i]];
                Buffer.BlockCopy(
                    chunkBuilder.OccupancyBytes,
                    0,
                    occupancyBytes,
                    i * VoxelChunkLayout.OccupancyByteCount,
                    VoxelChunkLayout.OccupancyByteCount);
                Buffer.BlockCopy(
                    chunkBuilder.VoxelBytes,
                    0,
                    voxelBytes,
                    i * VoxelChunkLayout.VoxelDataByteCount,
                    VoxelChunkLayout.VoxelDataByteCount);

                Vector3 chunkOrigin = Vector3.Scale(
                    (Vector3)chunkBuilder.ChunkCoord,
                    Vector3.one * (VoxelChunkLayout.Dimension * voxelSize));
                chunkAabbs[i] = new ModelChunkAabb(
                    chunkOrigin,
                    chunkOrigin + (Vector3.one * (VoxelChunkLayout.Dimension * voxelSize)));
            }
            LogStage("CompactExteriorVolume finalize chunks end", null, $"t={stopwatch.ElapsedMilliseconds} ms");

            return new MeshVoxelizationResult(occupancyBytes, voxelBytes, chunkAabbs);
        }

        private static int EncodeVoxelIndex(int x, int y, int z, Vector3Int gridDimensions)
        {
            return x + (gridDimensions.x * (y + (gridDimensions.y * z)));
        }

        private static void DecodeVoxelIndex(int index, Vector3Int gridDimensions, out int x, out int y, out int z)
        {
            int yz = index / gridDimensions.x;
            x = index - (yz * gridDimensions.x);
            z = yz / gridDimensions.y;
            y = yz - (z * gridDimensions.y);
        }

        private static int FlattenChunkIndex(Vector3Int chunkCoord, Vector3Int chunkDimensions)
        {
            return chunkCoord.x + (chunkDimensions.x * (chunkCoord.y + (chunkDimensions.y * chunkCoord.z)));
        }

        private static int ComputeWordIndex(int wordsPerRow, int gridHeight, int x, int y, int z)
        {
            return ((z * gridHeight) + y) * wordsPerRow + (x >> 5);
        }

        private static bool IsVoxelSet(uint[] words, int wordsPerRow, int gridHeight, int x, int y, int z)
        {
            int wordIndex = ComputeWordIndex(wordsPerRow, gridHeight, x, y, z);
            uint bitMask = 1u << (x & (WordBitCount - 1));
            return (words[wordIndex] & bitMask) != 0u;
        }

        private static void SetVoxelBit(uint[] words, int wordsPerRow, int gridHeight, int x, int y, int z)
        {
            int wordIndex = ComputeWordIndex(wordsPerRow, gridHeight, x, y, z);
            uint bitMask = 1u << (x & (WordBitCount - 1));
            words[wordIndex] |= bitMask;
        }

        private static uint CreateValidMask(int wordIndex, int gridWidth)
        {
            int remainingBitCount = gridWidth - (wordIndex * WordBitCount);
            if (remainingBitCount <= 0)
            {
                return 0u;
            }

            if (remainingBitCount >= WordBitCount)
            {
                return 0xffffffffu;
            }

            return (1u << remainingBitCount) - 1u;
        }

        private static int CountTrailingZeroBits(uint value)
        {
            uint isolatedBit = value & unchecked((uint)-(int)value);
            return TrailingZeroCountTable[(isolatedBit * 0x077cb531u) >> 27];
        }

        [System.Diagnostics.Conditional("MESH_VOXELIZER_TRACE")]
        private static void LogStage(string stageName, Stopwatch stopwatch, string details = null)
        {
            string prefix = stopwatch == null
                ? "MeshVoxelizer step"
                : $"MeshVoxelizer stage | t={stopwatch.ElapsedMilliseconds} ms";
            string message = string.IsNullOrEmpty(details)
                ? $"{prefix} | {stageName}"
                : $"{prefix} | {stageName} | {details}";

            EmitDebugLog(message);
            AppendTraceLine(message);
        }

        private static string FormatVector(Vector3Int value)
        {
            return $"{value.x}x{value.y}x{value.z}";
        }

        private static long ComputeTriangleBytes(int triangleCount)
        {
            return (long)triangleCount * Marshal.SizeOf<TriangleData>();
        }

        private static long ComputeWordBytes(int wordCount)
        {
            return (long)wordCount * sizeof(uint);
        }

        [System.Diagnostics.Conditional("MESH_VOXELIZER_TRACE")]
        internal static void AppendTraceLine(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TraceLogPath));
                File.AppendAllText(
                    TraceLogPath,
                    $"{DateTime.UtcNow:O} | {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        [System.Diagnostics.Conditional("MESH_VOXELIZER_TRACE")]
        internal static void EmitDebugLog(string message)
        {
            UnityEngine.Debug.LogFormat(UnityEngine.LogType.Log, UnityEngine.LogOption.NoStacktrace, null, "{0}", message);
        }

        private sealed class CompactChunkBuilder
        {
            public CompactChunkBuilder(Vector3Int chunkCoord)
            {
                ChunkCoord = chunkCoord;
                OccupancyBytes = new byte[VoxelChunkLayout.OccupancyByteCount];
                VoxelBytes = new byte[VoxelChunkLayout.VoxelDataByteCount];
            }

            public Vector3Int ChunkCoord { get; }

            public byte[] OccupancyBytes { get; }

            public byte[] VoxelBytes { get; }
        }
    }
}
