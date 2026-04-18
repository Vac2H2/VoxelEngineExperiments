using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelExperiments.Runtime.Rendering.VoxelRuntime;

namespace VoxelExperiments.Editor.Tools.PlayMode
{
    [InitializeOnLoad]
    internal static class VoxelGracefulPlayModeStop
    {
        private static readonly HashSet<int> PendingBootstrapIds = new HashSet<int>();

        static VoxelGracefulPlayModeStop()
        {
            EditorApplication.update -= HandleEditorUpdate;
            EditorApplication.update += HandleEditorUpdate;

            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        [MenuItem("Tools/VoxelExperiments/Graceful Stop Play Mode")]
        private static void RequestGracefulStop()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            PendingBootstrapIds.Clear();

            VoxelRuntimeBootstrap[] bootstraps = Object.FindObjectsByType<VoxelRuntimeBootstrap>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < bootstraps.Length; i++)
            {
                VoxelRuntimeBootstrap bootstrap = bootstraps[i];
                if (bootstrap == null || !bootstrap.CanServeWorld(true))
                {
                    continue;
                }

                PendingBootstrapIds.Add(bootstrap.GetInstanceID());
                bootstrap.RequestGracefulShutdown();
            }

            if (PendingBootstrapIds.Count == 0)
            {
                EditorApplication.isPlaying = false;
            }
            else
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        [MenuItem("Tools/VoxelExperiments/Graceful Stop Play Mode", true)]
        private static bool ValidateRequestGracefulStop()
        {
            return EditorApplication.isPlaying;
        }

        private static void HandleEditorUpdate()
        {
            if (PendingBootstrapIds.Count == 0 || !EditorApplication.isPlaying)
            {
                return;
            }

            VoxelRuntimeBootstrap[] bootstraps = Object.FindObjectsByType<VoxelRuntimeBootstrap>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Dictionary<int, VoxelRuntimeBootstrap> bootstrapsById = new Dictionary<int, VoxelRuntimeBootstrap>(bootstraps.Length);
            for (int i = 0; i < bootstraps.Length; i++)
            {
                VoxelRuntimeBootstrap bootstrap = bootstraps[i];
                if (bootstrap != null)
                {
                    bootstrapsById[bootstrap.GetInstanceID()] = bootstrap;
                }
            }

            List<int> completedIds = null;
            foreach (int pendingId in PendingBootstrapIds)
            {
                if (!bootstrapsById.TryGetValue(pendingId, out VoxelRuntimeBootstrap bootstrap)
                    || (!bootstrap.IsInitialized && !bootstrap.IsGracefulShutdownRequested))
                {
                    completedIds ??= new List<int>();
                    completedIds.Add(pendingId);
                }
            }

            if (completedIds != null)
            {
                for (int i = 0; i < completedIds.Count; i++)
                {
                    PendingBootstrapIds.Remove(completedIds[i]);
                }
            }

            if (PendingBootstrapIds.Count == 0)
            {
                EditorApplication.isPlaying = false;
            }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode
                || state == PlayModeStateChange.EnteredPlayMode)
            {
                PendingBootstrapIds.Clear();
            }
        }
    }
}
