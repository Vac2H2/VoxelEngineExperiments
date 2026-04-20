using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelExperiments.Runtime.Data;
using VoxelExperiments.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelExperiments.Editor.Tools.MeshVoxelizer
{
    public sealed class SphereToVoxelModelWindow : EditorWindow
    {
        private const int VoxelRasterProgressInterval = 4096;
        private const int ChunkPackingProgressInterval = 32;

        [SerializeField] private VoxelModel _targetModel;
        [SerializeField] private float _radius = 4.0f;
        [SerializeField] private float _voxelSize = 0.25f;
        [SerializeField] private int _solidVoxelValue = 1;

        [MenuItem("VoxelExperiments/Tools/Sphere To Voxel Model")]
        public static void Open()
        {
            SphereToVoxelModelWindow window = GetWindow<SphereToVoxelModelWindow>();
            window.titleContent = new GUIContent("Sphere To Voxel");
            window.minSize = new Vector2(420.0f, 280.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Sphere", EditorStyles.boldLabel);
            _radius = EditorGUILayout.FloatField("Radius", _radius);
            _voxelSize = EditorGUILayout.FloatField("Voxel Size", _voxelSize);
            _solidVoxelValue = EditorGUILayout.IntSlider("Solid Voxel Value", _solidVoxelValue, 1, 255);
            _targetModel = (VoxelModel)EditorGUILayout.ObjectField("Target Model", _targetModel, typeof(VoxelModel), false);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Memory Layout", ResolveRequestedMemoryLayout());
            }

            EditorGUILayout.Space();

            if (_radius > 0.0f && _voxelSize > 0.0f)
            {
                DrawSphereSummary(_radius, _voxelSize);
            }
            else
            {
                EditorGUILayout.HelpBox("Enter a positive radius and voxel size.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The generated sphere uses the model minimum corner as the local origin. Chunk (0,0,0) starts at (0,0,0), and the sphere center is placed at (radius, radius, radius).",
                MessageType.None);

            using (new EditorGUI.DisabledScope(_radius <= 0.0f || _voxelSize <= 0.0f))
            {
                if (GUILayout.Button(_targetModel == null ? "Create VoxelModel" : "Overwrite Target Model", GUILayout.Height(32.0f)))
                {
                    Bake();
                }
            }
        }

        private static void DrawSphereSummary(float radius, float voxelSize)
        {
            float diameter = radius * 2.0f;
            float chunkSize = VoxelChunkLayout.Dimension * voxelSize;
            int diameterVoxelCount = Mathf.Max(1, Mathf.CeilToInt(diameter / voxelSize));
            int diameterChunkCount = Mathf.Max(1, Mathf.CeilToInt(diameter / chunkSize));
            float approximateVoxelCount = (4.0f / 3.0f) * Mathf.PI * Mathf.Pow(radius / voxelSize, 3.0f);

            EditorGUILayout.LabelField("Diameter", diameter.ToString("F4"));
            EditorGUILayout.LabelField("Approx. Voxels Across", diameterVoxelCount.ToString());
            EditorGUILayout.LabelField("Approx. Chunks Across", diameterChunkCount.ToString());
            EditorGUILayout.LabelField("Approx. Filled Voxels", approximateVoxelCount.ToString("N0"));

            if (approximateVoxelCount > 2_000_000.0f)
            {
                EditorGUILayout.HelpBox("The estimated filled voxel count is large. Expect a slower bake and a larger asset.", MessageType.Warning);
            }
        }

        private void Bake()
        {
            string assetPath = ResolveTargetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            try
            {
                MeshVoxelizationResult result = GenerateSphereResult(
                    _radius,
                    _voxelSize,
                    checked((byte)_solidVoxelValue),
                    ResolveRequestedMemoryLayout());
                VoxelModel asset = VoxelModelAssetWriter.WriteAsset(_targetModel, assetPath, result);
                _targetModel = asset;
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Sphere To Voxel Model", exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static MeshVoxelizationResult GenerateSphereResult(
            float radius,
            float voxelSize,
            byte solidVoxelValue,
            VoxelMemoryLayout memoryLayout)
        {
            if (radius <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be greater than zero.");
            }

            if (voxelSize <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize), "Voxel size must be greater than zero.");
            }

            if (solidVoxelValue == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(solidVoxelValue), "Solid voxel value must be non-zero.");
            }

            if (!Enum.IsDefined(typeof(VoxelMemoryLayout), memoryLayout))
            {
                throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }

            float radiusSquared = radius * radius;
            Vector3 voxelExtent = Vector3.one * voxelSize;
            Vector3Int gridDimensions = CalculateGridDimensions(radius, voxelSize);
            Vector3 gridOrigin = CalculateGridOrigin(gridDimensions, voxelSize);
            Vector3 sphereCenter = new Vector3(radius, radius, radius);

            Dictionary<Vector3Int, int> chunkIndices = new Dictionary<Vector3Int, int>();
            List<ChunkBuilder> occupiedChunks = new List<ChunkBuilder>();
            long totalVoxelCount = (long)gridDimensions.x * gridDimensions.y * gridDimensions.z;
            long processedVoxelCount = 0L;

            for (int z = 0; z < gridDimensions.z; z++)
            {
                float voxelMinZ = gridOrigin.z + (z * voxelSize);
                for (int y = 0; y < gridDimensions.y; y++)
                {
                    float voxelMinY = gridOrigin.y + (y * voxelSize);
                    for (int x = 0; x < gridDimensions.x; x++)
                    {
                        processedVoxelCount++;
                        if (ShouldUpdateVoxelProgress(processedVoxelCount, totalVoxelCount))
                        {
                            if (EditorUtility.DisplayCancelableProgressBar(
                                    "Sphere To Voxel Model",
                                    $"Rasterizing {processedVoxelCount:N0} / {totalVoxelCount:N0} voxels\nOccupied chunks: {occupiedChunks.Count:N0}",
                                    Mathf.Clamp01((float)processedVoxelCount / totalVoxelCount)))
                            {
                                throw new OperationCanceledException("Sphere voxelization was canceled.");
                            }
                        }

                        Vector3 voxelMin = new Vector3(
                            gridOrigin.x + (x * voxelSize),
                            voxelMinY,
                            voxelMinZ);
                        Vector3 voxelMax = voxelMin + voxelExtent;
                        if (!IntersectsSphere(voxelMin - sphereCenter, voxelMax - sphereCenter, radiusSquared))
                        {
                            continue;
                        }

                        Vector3Int chunkCoord = new Vector3Int(
                            x / VoxelChunkLayout.Dimension,
                            y / VoxelChunkLayout.Dimension,
                            z / VoxelChunkLayout.Dimension);
                        if (!chunkIndices.TryGetValue(chunkCoord, out int chunkIndex))
                        {
                            chunkIndex = occupiedChunks.Count;
                            chunkIndices.Add(chunkCoord, chunkIndex);
                            Vector3 chunkOrigin = gridOrigin + Vector3.Scale(
                                (Vector3)chunkCoord,
                                Vector3.one * (VoxelChunkLayout.Dimension * voxelSize));
                            occupiedChunks.Add(new ChunkBuilder(chunkCoord, chunkOrigin, voxelSize, memoryLayout));
                        }

                        occupiedChunks[chunkIndex].SetVoxel(
                            x % VoxelChunkLayout.Dimension,
                            y % VoxelChunkLayout.Dimension,
                            z % VoxelChunkLayout.Dimension,
                            solidVoxelValue);
                    }
                }
            }

            if (occupiedChunks.Count == 0)
            {
                throw new InvalidOperationException("Sphere voxelization produced no occupied chunks.");
            }

            return BuildResult(memoryLayout, occupiedChunks);
        }

        private static MeshVoxelizationResult BuildResult(VoxelMemoryLayout memoryLayout, List<ChunkBuilder> occupiedChunks)
        {
            byte[] occupancyBytes = new byte[occupiedChunks.Count * VoxelChunkLayout.OccupancyByteCount];
            byte[] voxelBytes = new byte[occupiedChunks.Count * VoxelChunkLayout.VoxelDataByteCount];
            ModelChunkAabb[] chunkAabbs = new ModelChunkAabb[occupiedChunks.Count];
            Vector3Int[] chunkCoordinates = new Vector3Int[occupiedChunks.Count];

            for (int i = 0; i < occupiedChunks.Count; i++)
            {
                if (i == 0 || i == occupiedChunks.Count - 1 || ((i + 1) % ChunkPackingProgressInterval) == 0)
                {
                    EditorUtility.DisplayProgressBar(
                        "Sphere To Voxel Model",
                        $"Packing {i + 1:N0} / {occupiedChunks.Count:N0} chunks",
                        0.9f + (0.1f * ((float)(i + 1) / occupiedChunks.Count)));
                }

                occupiedChunks[i].CopyTo(
                    occupancyBytes,
                    i * VoxelChunkLayout.OccupancyByteCount,
                    voxelBytes,
                    i * VoxelChunkLayout.VoxelDataByteCount);
                chunkAabbs[i] = occupiedChunks[i].BuildAabb();
                chunkCoordinates[i] = occupiedChunks[i].ChunkCoordinate;
            }

            return new MeshVoxelizationResult(memoryLayout, occupancyBytes, voxelBytes, chunkAabbs, chunkCoordinates);
        }

        private string ResolveTargetPath()
        {
            if (_targetModel != null)
            {
                return AssetDatabase.GetAssetPath(_targetModel);
            }

            return EditorUtility.SaveFilePanelInProject(
                "Create Sphere VoxelModel",
                "SphereVoxelModel.asset",
                "asset",
                "Choose where to save the generated sphere VoxelModel.",
                "Assets");
        }

        private static Vector3Int CalculateGridDimensions(float radius, float voxelSize)
        {
            int diameterVoxelCount = Mathf.Max(1, Mathf.CeilToInt((radius * 2.0f) / voxelSize));
            return new Vector3Int(diameterVoxelCount, diameterVoxelCount, diameterVoxelCount);
        }

        private static Vector3 CalculateGridOrigin(Vector3Int gridDimensions, float voxelSize)
        {
            return Vector3.zero;
        }

        private VoxelMemoryLayout ResolveRequestedMemoryLayout()
        {
            return _targetModel != null
                ? _targetModel.MemoryLayout
                : VoxelMemoryLayout.Linear;
        }

        private static bool ShouldUpdateVoxelProgress(long processedVoxelCount, long totalVoxelCount)
        {
            if (processedVoxelCount <= 1L || processedVoxelCount >= totalVoxelCount)
            {
                return true;
            }

            return (processedVoxelCount % VoxelRasterProgressInterval) == 0L;
        }

        private static bool IntersectsSphere(Vector3 boundsMin, Vector3 boundsMax, float radiusSquared)
        {
            float squaredDistance = 0.0f;

            squaredDistance += ComputeAxisDistanceSquared(boundsMin.x, boundsMax.x);
            squaredDistance += ComputeAxisDistanceSquared(boundsMin.y, boundsMax.y);
            squaredDistance += ComputeAxisDistanceSquared(boundsMin.z, boundsMax.z);

            return squaredDistance <= radiusSquared;
        }

        private static float ComputeAxisDistanceSquared(float min, float max)
        {
            if (0.0f < min)
            {
                return min * min;
            }

            if (0.0f > max)
            {
                return max * max;
            }

            return 0.0f;
        }

        private sealed class ChunkBuilder
        {
            private readonly byte[] _occupancyBytes = new byte[VoxelChunkLayout.OccupancyByteCount];
            private readonly byte[] _voxelBytes = new byte[VoxelChunkLayout.VoxelDataByteCount];
            private readonly Vector3Int _chunkCoordinate;
            private readonly Vector3 _chunkOrigin;
            private readonly float _voxelSize;
            private readonly VoxelMemoryLayout _memoryLayout;

            public ChunkBuilder(Vector3Int chunkCoordinate, Vector3 chunkOrigin, float voxelSize, VoxelMemoryLayout memoryLayout)
            {
                _chunkCoordinate = chunkCoordinate;
                _chunkOrigin = chunkOrigin;
                _voxelSize = voxelSize;
                _memoryLayout = memoryLayout;
            }

            public bool HasOccupancy { get; private set; }

            public Vector3Int ChunkCoordinate => _chunkCoordinate;

            public void SetVoxel(int x, int y, int z, byte value)
            {
                int voxelIndex = VoxelChunkLayout.FlattenVoxelDataIndex(_memoryLayout, x, y, z);
                int occupancyByteIndex = VoxelChunkLayout.ComputeOccupancyByteIndex(_memoryLayout, x, y, z);
                byte occupancyMask = VoxelChunkLayout.ComputeOccupancyBitMask(_memoryLayout, x, y, z);

                _occupancyBytes[occupancyByteIndex] |= occupancyMask;
                _voxelBytes[voxelIndex] = value;
                HasOccupancy = true;
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
                if (!HasOccupancy)
                {
                    throw new InvalidOperationException("Cannot build an AABB for an empty chunk.");
                }

                Vector3 max = _chunkOrigin + (Vector3.one * (VoxelChunkLayout.Dimension * _voxelSize));
                return new ModelChunkAabb(_chunkOrigin, max);
            }
        }
    }
}
