using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Tools
{
    public sealed class ChunkCubeVoxelModelAssetWindow : EditorWindow
    {
        private const int ChunkGridResolution = 2;
        private const int OpaqueChunkCount = 4;
        private const int TransparentChunkCount = 4;
        private const int TotalChunkCount = OpaqueChunkCount + TransparentChunkCount;

        [SerializeField] private VoxelModelAsset _targetAsset;

        [MenuItem("VoxelEngine/Tools/Create Chunk Cube VoxelModel Asset")]
        public static void Open()
        {
            ChunkCubeVoxelModelAssetWindow window = GetWindow<ChunkCubeVoxelModelAssetWindow>();
            window.titleContent = new GUIContent("Chunk Cube");
            window.minSize = new Vector2(420.0f, 280.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Chunk Cube VoxelModel", EditorStyles.boldLabel);
            _targetAsset = (VoxelModelAsset)EditorGUILayout.ObjectField("Target Asset", _targetAsset, typeof(VoxelModelAsset), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Chunk Grid", $"{ChunkGridResolution} x {ChunkGridResolution} x {ChunkGridResolution}");
            EditorGUILayout.LabelField("Opaque Chunks", OpaqueChunkCount.ToString());
            EditorGUILayout.LabelField("Transparent Chunks", TransparentChunkCount.ToString());
            EditorGUILayout.LabelField("Chunk Dimension", VoxelVolume.ChunkDimension.ToString());
            EditorGUILayout.LabelField("Chunk Voxel IDs", "Opaque = 1..4, Transparent = 5..8");

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Creates one solid 2x2x2 chunk cube. The z = 0 layer is opaque, the z = 1 layer is transparent. Each chunk is fully filled, has one full-chunk AABB, and uses a unique voxel ID.",
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
                byte[] serializedBytes = BuildChunkCubeBytes();
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
                EditorUtility.DisplayDialog("Chunk Cube VoxelModel Asset", exception.Message, "OK");
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
                        "Chunk Cube VoxelModel Asset",
                        "Target asset must be a persistent VoxelModelAsset.",
                        "OK");
                    return null;
                }

                return existingAssetPath;
            }

            return EditorUtility.SaveFilePanelInProject(
                "Create Chunk Cube VoxelModel Asset",
                "chunk_cube_2x2x2",
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

        private static byte[] BuildChunkCubeBytes()
        {
            using VoxelModel model = VoxelModel.Create(OpaqueChunkCount, TransparentChunkCount, Allocator.Temp);

            FillChunk(model.OpaqueVolume, new int3(0, 0, 0), 1);
            FillChunk(model.OpaqueVolume, new int3(1, 0, 0), 2);
            FillChunk(model.OpaqueVolume, new int3(0, 1, 0), 3);
            FillChunk(model.OpaqueVolume, new int3(1, 1, 0), 4);

            FillChunk(model.TransparentVolume, new int3(0, 0, 1), 5);
            FillChunk(model.TransparentVolume, new int3(1, 0, 1), 6);
            FillChunk(model.TransparentVolume, new int3(0, 1, 1), 7);
            FillChunk(model.TransparentVolume, new int3(1, 1, 1), 8);

            return VoxelModelSerializer.SerializeToBytes(model);
        }

        private static void FillChunk(VoxelVolume volume, int3 chunkCoordinate, byte voxelId)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            if (voxelId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelId), "Voxel ID must be non-zero.");
            }

            if (!volume.TryAllocateChunk(chunkCoordinate, out int chunkIndex))
            {
                throw new InvalidOperationException($"Chunk {chunkCoordinate} was allocated more than once.");
            }

            for (int z = 0; z < VoxelVolume.ChunkDimension; z++)
            {
                for (int y = 0; y < VoxelVolume.ChunkDimension; y++)
                {
                    for (int x = 0; x < VoxelVolume.ChunkDimension; x++)
                    {
                        volume.SetVoxel(chunkIndex, x, y, z, voxelId);
                    }
                }
            }

            if (!volume.TryAllocateAabbSlot(chunkIndex, out int aabbIndex))
            {
                throw new InvalidOperationException($"Chunk {chunkCoordinate} has no free AABB slots.");
            }

            volume.SetAabb(
                chunkIndex,
                aabbIndex,
                int3.zero,
                new int3(VoxelVolume.ChunkDimension, VoxelVolume.ChunkDimension, VoxelVolume.ChunkDimension));
        }
    }
}
