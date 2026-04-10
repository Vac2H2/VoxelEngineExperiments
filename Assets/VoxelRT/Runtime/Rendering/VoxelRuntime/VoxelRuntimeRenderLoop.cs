using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.VoxelRuntime
{
    internal static class VoxelRuntimeRenderLoop
    {
        private static bool _isInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain()
        {
            if (!_isInitialized)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            _isInitialized = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InitializeRuntime()
        {
            EnsureInitialized();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            EnsureInitialized();
        }
#endif

        private static void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            _isInitialized = true;
        }

        private static void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            if (!VoxelRuntimeBootstrapResolver.TryResolve(camera, out VoxelRuntimeBootstrap bootstrap))
            {
                return;
            }

            bootstrap.Tick();
        }
    }
}
