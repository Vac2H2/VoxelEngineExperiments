using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Tools
{
    public sealed class DualAabbChunkVoxelModelAssetWindow : EditorWindow
    {
        private static readonly int3 ChunkCoordinate = int3.zero;
        private static readonly int3 LeftMin = new int3(0, 0, 0);
        private static readonly int3 LeftMaxExclusive = new int3(3, VoxelVolume.ChunkDimension, VoxelVolume.ChunkDimension);
        private static readonly int3 RightMin = new int3(5, 0, 0);
        private static readonly int3 RightMaxExclusive = new int3(VoxelVolume.ChunkDimension, VoxelVolume.ChunkDimension, VoxelVolume.ChunkDimension);
        private const int MinimumVoxelId = 1;
        private const int MaximumVoxelId = 255;

        [SerializeField] private VoxelModelAsset _targetAsset;
        [SerializeField] private bool _randomizeVoxelIds = true;
        [SerializeField] private int _randomSeed = 12345;
        [SerializeField] private int _leftVoxelId = 1;
        [SerializeField] private int _rightVoxelId = 2;

        [MenuItem("VoxelEngine/Tools/Create Dual AABB Chunk VoxelModel Asset")]
        public static void Open()
        {
            DualAabbChunkVoxelModelAssetWindow window = GetWindow<DualAabbChunkVoxelModelAssetWindow>();
            window.titleContent = new GUIContent("Dual AABB Chunk");
            window.minSize = new Vector2(440.0f, 300.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Dual AABB Chunk VoxelModel", EditorStyles.boldLabel);
            _randomizeVoxelIds = EditorGUILayout.Toggle("Randomize Voxel IDs", _randomizeVoxelIds);
            if (_randomizeVoxelIds)
            {
                _randomSeed = EditorGUILayout.IntField("Random Seed", _randomSeed);
            }
            else
            {
                _leftVoxelId = EditorGUILayout.IntSlider("Left Voxel ID", _leftVoxelId, MinimumVoxelId, MaximumVoxelId);
                _rightVoxelId = EditorGUILayout.IntSlider("Right Voxel ID", _rightVoxelId, MinimumVoxelId, MaximumVoxelId);
            }

            _targetAsset = (VoxelModelAsset)EditorGUILayout.ObjectField("Target Asset", _targetAsset, typeof(VoxelModelAsset), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Chunk Count", "1 opaque chunk");
            EditorGUILayout.LabelField("Chunk Coordinate", "(0, 0, 0)");
            EditorGUILayout.LabelField("AABB Count", "2");
            EditorGUILayout.LabelField("Left AABB", "[0, 0, 0] -> [3, 8, 8)");
            EditorGUILayout.LabelField("Right AABB", "[5, 0, 0] -> [8, 8, 8)");
            if (_randomizeVoxelIds)
            {
                EditorGUILayout.LabelField("Voxel IDs", "Per-voxel random IDs in [1, 255]");
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                _randomizeVoxelIds
                    ? "Creates one opaque chunk with two separated solid regions. Every occupied voxel gets a deterministic random ID in [1, 255] based on the seed, the gap in the middle remains empty, and the chunk gets exactly two AABBs."
                    : "Creates one opaque chunk with two separated solid regions. The left region uses the left voxel ID, the right region uses the right voxel ID, and the gap in the middle remains empty. The chunk gets exactly two AABBs.",
                MessageType.None);

            if (GUILayout.Button(_targetAsset == null ? "Create Asset" : "Overwrite Asset", GUILayout.Height(32.0f)))
            {
                GenerateAsset();
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
                byte[] serializedBytes = BuildModelBytes(
                    _randomizeVoxelIds,
                    _randomSeed,
                    checked((byte)_leftVoxelId),
                    checked((byte)_rightVoxelId));
                _targetAsset = WriteAsset(assetPath, serializedBytes);
                if (_targetAsset != null)
                {
                    Selection.activeObject = _targetAsset;
                    EditorGUIUtility.PingObject(_targetAsset);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Dual AABB Chunk VoxelModel Asset", exception.Message, "OK");
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
                        "Dual AABB Chunk VoxelModel Asset",
                        "Target asset must be a persistent VoxelModelAsset.",
                        "OK");
                    return null;
                }

                return existingAssetPath;
            }

            return EditorUtility.SaveFilePanelInProject(
                "Create Dual AABB Chunk VoxelModel Asset",
                "dual_aabb_chunk",
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

        private static byte[] BuildModelBytes(bool randomizeVoxelIds, int randomSeed, byte leftVoxelId, byte rightVoxelId)
        {
            if (!randomizeVoxelIds && leftVoxelId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(leftVoxelId), "Left voxel ID must be non-zero.");
            }

            if (!randomizeVoxelIds && rightVoxelId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rightVoxelId), "Right voxel ID must be non-zero.");
            }

            using VoxelModel model = VoxelModel.Create(1, 1, Allocator.Temp);
            FillDualAabbChunk(model.OpaqueVolume, randomizeVoxelIds, randomSeed, leftVoxelId, rightVoxelId);
            return VoxelModelSerializer.SerializeToBytes(model);
        }

        private static void FillDualAabbChunk(
            VoxelVolume volume,
            bool randomizeVoxelIds,
            int randomSeed,
            byte leftVoxelId,
            byte rightVoxelId)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            if (!volume.TryAllocateChunk(ChunkCoordinate, out int chunkIndex))
            {
                throw new InvalidOperationException("Chunk (0, 0, 0) was allocated more than once.");
            }

            System.Random random = randomizeVoxelIds ? new System.Random(randomSeed) : null;
            FillRegion(volume, chunkIndex, LeftMin, LeftMaxExclusive, leftVoxelId, random);
            FillRegion(volume, chunkIndex, RightMin, RightMaxExclusive, rightVoxelId, random);

            if (!volume.TryAllocateAabbSlot(chunkIndex, out int leftAabbIndex))
            {
                throw new InvalidOperationException("Failed to allocate the first AABB slot.");
            }

            volume.SetAabb(chunkIndex, leftAabbIndex, LeftMin, LeftMaxExclusive);

            if (!volume.TryAllocateAabbSlot(chunkIndex, out int rightAabbIndex))
            {
                throw new InvalidOperationException("Failed to allocate the second AABB slot.");
            }

            volume.SetAabb(chunkIndex, rightAabbIndex, RightMin, RightMaxExclusive);
        }

        private static void FillRegion(
            VoxelVolume volume,
            int chunkIndex,
            int3 minInclusive,
            int3 maxExclusive,
            byte voxelId,
            System.Random random)
        {
            for (int z = minInclusive.z; z < maxExclusive.z; z++)
            {
                for (int y = minInclusive.y; y < maxExclusive.y; y++)
                {
                    for (int x = minInclusive.x; x < maxExclusive.x; x++)
                    {
                        byte resolvedVoxelId = random == null
                            ? voxelId
                            : checked((byte)random.Next(MinimumVoxelId, MaximumVoxelId + 1));
                        volume.SetVoxel(chunkIndex, x, y, z, resolvedVoxelId);
                    }
                }
            }
        }
    }
}
