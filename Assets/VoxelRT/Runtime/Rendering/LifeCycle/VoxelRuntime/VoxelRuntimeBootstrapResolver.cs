using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoxelRT.Runtime.Rendering.VoxelRuntime
{
    internal static class VoxelRuntimeBootstrapResolver
    {
        public static bool TryResolve(GameObject owner, VoxelRuntimeBootstrap explicitBootstrap, out VoxelRuntimeBootstrap bootstrap)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (IsUsable(owner, explicitBootstrap))
            {
                bootstrap = explicitBootstrap;
                return true;
            }

            return TryResolve(owner.scene, Application.IsPlaying(owner), out bootstrap);
        }

        public static bool TryResolve(Camera camera, out VoxelRuntimeBootstrap bootstrap)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            return TryResolve(camera.gameObject.scene, Application.IsPlaying(camera.gameObject), out bootstrap);
        }

        private static bool TryResolve(Scene preferredScene, bool isPlayingWorld, out VoxelRuntimeBootstrap bootstrap)
        {
            bootstrap = null;
            int bestScore = int.MinValue;

            VoxelRuntimeBootstrap[] candidates = UnityEngine.Object.FindObjectsByType<VoxelRuntimeBootstrap>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < candidates.Length; i++)
            {
                VoxelRuntimeBootstrap candidate = candidates[i];
                if (!candidate.CanServeWorld(isPlayingWorld))
                {
                    continue;
                }

                int score = Score(preferredScene, candidate);
                if (score <= bestScore)
                {
                    continue;
                }

                bootstrap = candidate;
                bestScore = score;
            }

            return bootstrap != null;
        }

        private static bool IsUsable(GameObject owner, VoxelRuntimeBootstrap bootstrap)
        {
            return owner != null && bootstrap != null && bootstrap.CanServe(owner);
        }

        private static int Score(Scene preferredScene, VoxelRuntimeBootstrap candidate)
        {
            int score = 0;

            if (preferredScene.IsValid() && candidate.gameObject.scene == preferredScene)
            {
                score += 1000;
            }

            if (candidate.gameObject.scene == SceneManager.GetActiveScene())
            {
                score += 100;
            }

            score -= candidate.transform.GetSiblingIndex();
            return score;
        }
    }
}
