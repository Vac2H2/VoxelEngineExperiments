using System.Diagnostics;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxelExperiments.Runtime.Rendering.VoxelRuntime
{
    internal static class VoxelRuntimeUpdateUtility
    {
        [Conditional("UNITY_EDITOR")]
        public static void RequestEditorUpdate()
        {
#if UNITY_EDITOR
            if (EditorApplication.isCompiling)
            {
                return;
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
#endif
        }
    }
}
