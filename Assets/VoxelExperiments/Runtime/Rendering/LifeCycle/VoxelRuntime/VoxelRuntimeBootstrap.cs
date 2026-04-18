using System;
using UnityEngine;
using VoxelExperiments.Runtime.Rendering.RayTracingScene;
using VoxelExperiments.Runtime.Rendering.VoxelGpuResourceSystem;
using VoxelExperiments.Runtime.Rendering.VoxelRayTracingResourceBinder;
using VoxelRendererComponent = VoxelExperiments.Runtime.Rendering.VoxelRenderer.VoxelRenderer;

namespace VoxelExperiments.Runtime.Rendering.VoxelRuntime
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class VoxelRuntimeBootstrap : MonoBehaviour
    {
        private const int GracefulShutdownBatchSize = 32;

        [SerializeField] private bool _autoBuildScene = true;
        [SerializeField] private bool _autoBindGlobals = true;

        private VoxelRuntime _runtime;
        private int _version;
        private bool _gracefulShutdownRequested;
        private bool _gracefulShutdownInitialized;
        private VoxelRendererComponent[] _gracefulShutdownRenderers = Array.Empty<VoxelRendererComponent>();
        private int _gracefulShutdownNextRendererIndex;

        public bool IsInitialized => _runtime != null && !_runtime.IsDisposed;

        public int Version => _version;

        public bool IsPlayingWorld => Application.IsPlaying(gameObject);

        public bool IsGracefulShutdownRequested => _gracefulShutdownRequested;

        public VoxelRuntime Runtime
        {
            get
            {
                InitializeIfNeeded();
                return _runtime;
            }
        }

        public IVoxelGpuResourceSystem GpuResourceSystem => Runtime.GpuResourceSystem;

        public IRayTracingScene RayTracingScene => Runtime.RayTracingScene;

        public IVoxelRayTracingResourceBinder ResourceBinder => Runtime.ResourceBinder;

        public bool CanServe(GameObject owner)
        {
            return owner != null && CanServeWorld(Application.IsPlaying(owner));
        }

        public bool CanServeWorld(bool isPlayingWorld)
        {
            return isActiveAndEnabled
                && gameObject.activeInHierarchy
                && IsPlayingWorld == isPlayingWorld;
        }

        public void Tick()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_gracefulShutdownRequested)
            {
                TickGracefulShutdown();
                return;
            }

            InitializeIfNeeded();
            _runtime.Tick(_autoBuildScene, _autoBindGlobals);
        }

        public void RequestGracefulShutdown()
        {
            if (!Application.IsPlaying(gameObject))
            {
                Shutdown();
                return;
            }

            _gracefulShutdownRequested = true;
            VoxelRuntimeUpdateUtility.RequestEditorUpdate();
        }

        public void Shutdown()
        {
            ResetGracefulShutdownState();

            if (!IsInitialized)
            {
                return;
            }

            _runtime.Dispose();
            _runtime = null;
            _version++;
            VoxelRuntimeUpdateUtility.RequestEditorUpdate();
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
            VoxelRuntimeUpdateUtility.RequestEditorUpdate();
        }

        private void LateUpdate()
        {
            Tick();
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        public void InitializeIfNeeded()
        {
            if (IsInitialized)
            {
                return;
            }

            IVoxelGpuResourceSystem gpuResourceSystem = null;
            IRayTracingScene rayTracingScene = null;

            try
            {
                gpuResourceSystem = new VoxelGpuResourceSystem.VoxelGpuResourceSystem();
                rayTracingScene = new RayTracingScene.RayTracingScene();
                IVoxelRayTracingResourceBinder resourceBinder = new VoxelRayTracingResourceBinder.VoxelRayTracingResourceBinder(gpuResourceSystem);
                _runtime = new VoxelRuntime(gpuResourceSystem, rayTracingScene, resourceBinder);
                _version++;
                VoxelRuntimeUpdateUtility.RequestEditorUpdate();
            }
            catch
            {
                rayTracingScene?.Dispose();
                gpuResourceSystem?.Dispose();
                throw;
            }
        }

        private void TickGracefulShutdown()
        {
            if (!_gracefulShutdownInitialized)
            {
                InitializeGracefulShutdownQueue();
            }

            int processedCount = 0;
            while (_gracefulShutdownNextRendererIndex < _gracefulShutdownRenderers.Length
                && processedCount < GracefulShutdownBatchSize)
            {
                VoxelRendererComponent renderer = _gracefulShutdownRenderers[_gracefulShutdownNextRendererIndex++];
                if (renderer != null)
                {
                    renderer.PrepareForGracefulShutdown(this);
                }

                processedCount++;
            }

            if (_gracefulShutdownNextRendererIndex < _gracefulShutdownRenderers.Length)
            {
                VoxelRuntimeUpdateUtility.RequestEditorUpdate();
                return;
            }

            Shutdown();
        }

        private void InitializeGracefulShutdownQueue()
        {
            VoxelRendererComponent[] candidates = UnityEngine.Object.FindObjectsByType<VoxelRendererComponent>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            VoxelRendererComponent[] filtered = new VoxelRendererComponent[candidates.Length];
            int count = 0;

            for (int i = 0; i < candidates.Length; i++)
            {
                VoxelRendererComponent renderer = candidates[i];
                if (renderer == null || renderer.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                filtered[count++] = renderer;
            }

            if (count == filtered.Length)
            {
                _gracefulShutdownRenderers = filtered;
            }
            else
            {
                _gracefulShutdownRenderers = new VoxelRendererComponent[count];
                Array.Copy(filtered, _gracefulShutdownRenderers, count);
            }

            _gracefulShutdownInitialized = true;
            _gracefulShutdownNextRendererIndex = 0;
        }

        private void ResetGracefulShutdownState()
        {
            _gracefulShutdownRequested = false;
            _gracefulShutdownInitialized = false;
            _gracefulShutdownRenderers = Array.Empty<VoxelRendererComponent>();
            _gracefulShutdownNextRendererIndex = 0;
        }
    }
}
