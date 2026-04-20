using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Tools
{
    [CustomEditor(typeof(VoxelModelAsset))]
    public sealed class VoxelModelAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            VoxelModelAsset asset = (VoxelModelAsset)target;

            using (new EditorGUI.DisabledScope(true))
            {
                MonoScript script = MonoScript.FromScriptableObject(asset);
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Serialized Bytes", asset.SerializedByteCount.ToString("N0"));
            EditorGUILayout.HelpBox(
                "VoxelModelAsset stores an internal serialized payload. Modify it through the generation/import tools, not through the inspector.",
                MessageType.Info);
        }
    }

    [CustomEditor(typeof(VoxelPaletteAsset))]
    public sealed class VoxelPaletteAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            VoxelPaletteAsset asset = (VoxelPaletteAsset)target;

            using (new EditorGUI.DisabledScope(true))
            {
                MonoScript script = MonoScript.FromScriptableObject(asset);
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            }

            EditorGUILayout.Space();
            EditorGUILayout.IntField("Palette Entries", VoxelPalette.ColorCount);
            EditorGUILayout.LabelField("Serialized Bytes", asset.SerializedByteCount.ToString("N0"));
            EditorGUILayout.HelpBox(
                "VoxelPaletteAsset always represents a fixed 256-color palette. The serialized payload is internal and not editable in the inspector.",
                MessageType.Info);
        }
    }
}
