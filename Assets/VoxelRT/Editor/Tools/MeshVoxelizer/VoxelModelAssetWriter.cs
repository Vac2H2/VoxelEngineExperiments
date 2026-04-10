using System;
using UnityEditor;
using UnityEngine;
using VoxelRT.Runtime.Data;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    internal static class VoxelModelAssetWriter
    {
        public static VoxelModel WriteAsset(VoxelModel targetModel, string assetPath, MeshVoxelizationResult result)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("Asset path is required.", nameof(assetPath));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            VoxelModel asset = targetModel;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<VoxelModel>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            SerializedObject serializedObject = new SerializedObject(asset);
            serializedObject.Update();

            serializedObject.FindProperty("_chunkCount").intValue = result.ChunkCount;
            SetByteArray(serializedObject.FindProperty("_occupancyBytes"), result.OccupancyBytes);
            SetByteArray(serializedObject.FindProperty("_voxelBytes"), result.VoxelBytes);
            SetChunkAabbArray(serializedObject.FindProperty("_chunkAabbs"), result.ChunkAabbs);

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            asset.InvalidateResidency();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(asset));
            return asset;
        }

        private static void SetByteArray(SerializedProperty property, byte[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).intValue = values[i];
            }
        }

        private static void SetChunkAabbArray(SerializedProperty property, ModelChunkAabb[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Min").vector3Value = values[i].Min;
                element.FindPropertyRelative("Max").vector3Value = values[i].Max;
            }
        }
    }
}
