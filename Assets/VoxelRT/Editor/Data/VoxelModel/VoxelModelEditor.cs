using UnityEditor;
using RuntimeVoxelModel = VoxelRT.Runtime.Data.VoxelModel;

namespace VoxelRT.Editor.Data.VoxelModel
{
    [CustomEditor(typeof(RuntimeVoxelModel))]
    [CanEditMultipleObjects]
    public sealed class VoxelModelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "VoxelModel data is read-only in the inspector. Modify it through the Mesh To Voxel Model tool or other generation tooling.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                if (targets.Length == 1)
                {
                    DrawSingleTarget((RuntimeVoxelModel)target);
                }
                else
                {
                    EditorGUILayout.LabelField("Selection", $"{targets.Length} VoxelModel assets");
                }
            }
        }

        private static void DrawSingleTarget(RuntimeVoxelModel voxelModel)
        {
            MonoScript script = MonoScript.FromScriptableObject(voxelModel);
            EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            EditorGUILayout.IntField("Chunk Count", checked((int)voxelModel.ChunkCount));
            EditorGUILayout.IntField("Occupancy Bytes", voxelModel.OccupancyByteCount);
            EditorGUILayout.IntField("Voxel Bytes", voxelModel.VoxelByteCount);
            EditorGUILayout.IntField("Chunk AABBs", voxelModel.ChunkAabbCount);
        }
    }
}
