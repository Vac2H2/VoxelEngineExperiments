using UnityEditor;
using UnityEngine;
using VoxelRT.Runtime.Rendering.VoxelRuntime;

namespace VoxelRT.Editor.Rendering.VoxelRuntime
{
    [InitializeOnLoad]
    internal static class VoxelRuntimeEditorLifecycle
    {
        static VoxelRuntimeEditorLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownAllRuntimes;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode
                && state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            ShutdownAllRuntimes();
        }

        private static void ShutdownAllRuntimes()
        {
            VoxelRuntimeBootstrap[] bootstraps = Resources.FindObjectsOfTypeAll<VoxelRuntimeBootstrap>();
            for (int i = 0; i < bootstraps.Length; i++)
            {
                VoxelRuntimeBootstrap bootstrap = bootstraps[i];
                if (bootstrap == null || EditorUtility.IsPersistent(bootstrap))
                {
                    continue;
                }

                bootstrap.Shutdown();
            }
        }
    }
}
