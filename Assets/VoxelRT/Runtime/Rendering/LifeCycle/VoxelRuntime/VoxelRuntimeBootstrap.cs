using System;
using UnityEngine;
using VoxelRT.Runtime.Rendering.RayTracingScene;
using VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem;
using VoxelRT.Runtime.Rendering.VoxelRayTracingResourceBinder;

namespace VoxelRT.Runtime.Rendering.VoxelRuntime
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class VoxelRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private bool _autoBuildScene = true;
        [SerializeField] private bool _autoBindGlobals = true;

        private VoxelRuntime _runtime;
        private int _version;

        public bool IsInitialized => _runtime != null && !_runtime.IsDisposed;

        public int Version => _version;

        public bool IsPlayingWorld => Application.IsPlaying(gameObject);

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

        public IRayTracingScene OpaqueRayTracingScene => Runtime.OpaqueRayTracingScene;

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

            InitializeIfNeeded();
            _runtime.Tick(_autoBuildScene, _autoBindGlobals);
        }

        public void Shutdown()
        {
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
            IRayTracingScene opaqueRayTracingScene = null;

            try
            {
                gpuResourceSystem = new VoxelGpuResourceSystem.VoxelGpuResourceSystem();
                rayTracingScene = new RayTracingScene.RayTracingScene();
                opaqueRayTracingScene = new RayTracingScene.RayTracingScene();
                IVoxelRayTracingResourceBinder resourceBinder = new VoxelRayTracingResourceBinder.VoxelRayTracingResourceBinder(gpuResourceSystem);
                _runtime = new VoxelRuntime(gpuResourceSystem, rayTracingScene, opaqueRayTracingScene, resourceBinder);
                _version++;
                VoxelRuntimeUpdateUtility.RequestEditorUpdate();
            }
            catch
            {
                opaqueRayTracingScene?.Dispose();
                rayTracingScene?.Dispose();
                gpuResourceSystem?.Dispose();
                throw;
            }
        }
    }
}
