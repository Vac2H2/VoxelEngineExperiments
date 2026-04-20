using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Tools
{
    public sealed class SphereVoxelModelAssetWindow : EditorWindow
    {
        private const int VoxelRasterProgressInterval = 4096;

        [SerializeField] private VoxelModelAsset _targetAsset;
        [SerializeField] private int _diameterInVoxels = 16;
        [SerializeField] private int _solidVoxelValue = 1;

        [MenuItem("VoxelEngine/Tools/Create Sphere VoxelModel Asset")]
        public static void Open()
        {
            SphereVoxelModelAssetWindow window = GetWindow<SphereVoxelModelAssetWindow>();
            window.titleContent = new GUIContent("Sphere Voxel");
            window.minSize = new Vector2(420.0f, 260.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Sphere VoxelModel", EditorStyles.boldLabel);
            _diameterInVoxels = Mathf.Max(1, EditorGUILayout.IntField("Diameter (Voxels)", _diameterInVoxels));
            _solidVoxelValue = EditorGUILayout.IntSlider("Solid Voxel Value", _solidVoxelValue, 1, 255);
            _targetAsset = (VoxelModelAsset)EditorGUILayout.ObjectField("Target Asset", _targetAsset, typeof(VoxelModelAsset), false);

            EditorGUILayout.Space();
            DrawSummary(_diameterInVoxels);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The generated model uses the opaque volume only. Each occupied chunk receives one conservative AABB that covers all filled voxels in that chunk.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(_diameterInVoxels <= 0))
            {
                if (GUILayout.Button(_targetAsset == null ? "Create Asset" : "Overwrite Asset", GUILayout.Height(32.0f)))
                {
                    GenerateAsset();
                }
            }
        }

        private static void DrawSummary(int diameterInVoxels)
        {
            int chunkResolution = Mathf.Max(1, Mathf.CeilToInt((float)diameterInVoxels / VoxelVolume.ChunkDimension));
            int chunkCapacity = checked(chunkResolution * chunkResolution * chunkResolution);
            float radius = diameterInVoxels * 0.5f;
            float approximateFilledVoxels = (4.0f / 3.0f) * Mathf.PI * radius * radius * radius;

            EditorGUILayout.LabelField("Chunk Grid", $"{chunkResolution} x {chunkResolution} x {chunkResolution}");
            EditorGUILayout.LabelField("Chunk Capacity", chunkCapacity.ToString());
            EditorGUILayout.LabelField("Approx. Filled Voxels", approximateFilledVoxels.ToString("N0"));

            if (approximateFilledVoxels > 500_000.0f)
            {
                EditorGUILayout.HelpBox("This sphere is fairly large. Generation and import may take longer.", MessageType.Warning);
            }
        }

        private void GenerateAsset()
        {
            string assetPath = ResolveTargetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            try
            {
                byte[] serializedBytes = BuildSphereBytes(_diameterInVoxels, checked((byte)_solidVoxelValue));
                _targetAsset = WriteAsset(assetPath, serializedBytes);
                if (_targetAsset != null)
                {
                    Selection.activeObject = _targetAsset;
                    EditorGUIUtility.PingObject(_targetAsset);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Sphere VoxelModel Asset", exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private string ResolveTargetPath()
        {
            if (_targetAsset != null)
            {
                string existingAssetPath = AssetDatabase.GetAssetPath(_targetAsset);
                if (string.IsNullOrWhiteSpace(existingAssetPath))
                {
                    EditorUtility.DisplayDialog(
                        "Sphere VoxelModel Asset",
                        "Target asset must be a persistent VoxelModelAsset.",
                        "OK");
                    return null;
                }

                return existingAssetPath;
            }

            return EditorUtility.SaveFilePanelInProject(
                "Create Sphere VoxelModel Asset",
                $"sphere_{_diameterInVoxels}",
                "asset",
                "Choose where to save the generated VoxelModel asset.",
                "Assets");
        }

        private static VoxelModelAsset WriteAsset(string assetPath, byte[] serializedBytes)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("Asset path must be a non-empty string.", nameof(assetPath));
            }

            if (serializedBytes == null)
            {
                throw new ArgumentNullException(nameof(serializedBytes));
            }

            VoxelModelAsset asset = AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<VoxelModelAsset>();
                asset.SetSerializedData(serializedBytes);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.SetSerializedData(serializedBytes);
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(assetPath);
        }

        private static byte[] BuildSphereBytes(int diameterInVoxels, byte solidVoxelValue)
        {
            if (diameterInVoxels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(diameterInVoxels), "Diameter must be greater than zero.");
            }

            if (solidVoxelValue == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(solidVoxelValue), "Solid voxel value must be non-zero.");
            }

            int chunkResolution = Mathf.Max(1, Mathf.CeilToInt((float)diameterInVoxels / VoxelVolume.ChunkDimension));
            int initialOpaqueChunkCapacity = checked(chunkResolution * chunkResolution * chunkResolution);

            using VoxelModel model = VoxelModel.Create(initialOpaqueChunkCapacity, 1, Allocator.Temp);
            FillSphere(model.OpaqueVolume, diameterInVoxels, solidVoxelValue);
            return VoxelModelSerializer.SerializeToBytes(model);
        }

        private static void FillSphere(VoxelVolume volume, int diameterInVoxels, byte solidVoxelValue)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            float radius = diameterInVoxels * 0.5f;
            float radiusSquared = radius * radius;
            float3 sphereCenter = new float3(radius, radius, radius);
            long totalVoxelCount = (long)diameterInVoxels * diameterInVoxels * diameterInVoxels;
            long processedVoxelCount = 0L;
            var chunkStates = new Dictionary<int3, ChunkBuildState>();

            for (int z = 0; z < diameterInVoxels; z++)
            {
                for (int y = 0; y < diameterInVoxels; y++)
                {
                    for (int x = 0; x < diameterInVoxels; x++)
                    {
                        processedVoxelCount++;
                        if (ShouldUpdateProgress(processedVoxelCount, totalVoxelCount))
                        {
                            bool canceled = EditorUtility.DisplayCancelableProgressBar(
                                "Sphere VoxelModel Asset",
                                $"Rasterizing {processedVoxelCount:N0} / {totalVoxelCount:N0} voxels\nOccupied chunks: {chunkStates.Count:N0}",
                                Mathf.Clamp01((float)processedVoxelCount / totalVoxelCount));
                            if (canceled)
                            {
                                throw new OperationCanceledException("Sphere generation was canceled.");
                            }
                        }

                        float3 voxelCenter = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
                        if (math.lengthsq(voxelCenter - sphereCenter) > radiusSquared)
                        {
                            continue;
                        }

                        int3 chunkCoordinate = new int3(
                            x / VoxelVolume.ChunkDimension,
                            y / VoxelVolume.ChunkDimension,
                            z / VoxelVolume.ChunkDimension);

                        if (!chunkStates.TryGetValue(chunkCoordinate, out ChunkBuildState chunkState))
                        {
                            if (!volume.TryAllocateChunk(chunkCoordinate, out int chunkIndex))
                            {
                                throw new InvalidOperationException($"Chunk {chunkCoordinate} was allocated more than once.");
                            }

                            chunkState = new ChunkBuildState(chunkIndex);
                            chunkStates.Add(chunkCoordinate, chunkState);
                        }

                        int3 localVoxelCoordinate = new int3(
                            x % VoxelVolume.ChunkDimension,
                            y % VoxelVolume.ChunkDimension,
                            z % VoxelVolume.ChunkDimension);

                        volume.SetVoxel(
                            chunkState.ChunkIndex,
                            localVoxelCoordinate.x,
                            localVoxelCoordinate.y,
                            localVoxelCoordinate.z,
                            solidVoxelValue);
                        chunkState.IncludeVoxel(localVoxelCoordinate);
                    }
                }
            }

            if (chunkStates.Count == 0)
            {
                throw new InvalidOperationException("Sphere generation produced no occupied chunks.");
            }

            foreach (ChunkBuildState chunkState in chunkStates.Values)
            {
                if (!volume.TryAllocateAabbSlot(chunkState.ChunkIndex, out int aabbIndex))
                {
                    throw new InvalidOperationException($"Chunk {chunkState.ChunkIndex} has no free AABB slots.");
                }

                volume.SetAabb(chunkState.ChunkIndex, aabbIndex, chunkState.Min, chunkState.MaxExclusive);
            }
        }

        private static bool ShouldUpdateProgress(long processedVoxelCount, long totalVoxelCount)
        {
            if (processedVoxelCount <= 1L || processedVoxelCount >= totalVoxelCount)
            {
                return true;
            }

            return (processedVoxelCount % VoxelRasterProgressInterval) == 0L;
        }

        private sealed class ChunkBuildState
        {
            public ChunkBuildState(int chunkIndex)
            {
                ChunkIndex = chunkIndex;
            }

            public int ChunkIndex { get; }

            public int3 Min { get; private set; }

            public int3 MaxInclusive { get; private set; }

            public int3 MaxExclusive => MaxInclusive + new int3(1, 1, 1);

            public bool HasVoxels { get; private set; }

            public void IncludeVoxel(int3 localVoxelCoordinate)
            {
                if (!HasVoxels)
                {
                    Min = localVoxelCoordinate;
                    MaxInclusive = localVoxelCoordinate;
                    HasVoxels = true;
                    return;
                }

                Min = math.min(Min, localVoxelCoordinate);
                MaxInclusive = math.max(MaxInclusive, localVoxelCoordinate);
            }
        }
    }
}
