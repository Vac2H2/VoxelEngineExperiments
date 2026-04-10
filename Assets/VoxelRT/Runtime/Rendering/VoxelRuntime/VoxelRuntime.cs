using System;
using VoxelRT.Runtime.Rendering.RayTracingScene;
using VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem;
using VoxelRT.Runtime.Rendering.VoxelRayTracingResourceBinder;

namespace VoxelRT.Runtime.Rendering.VoxelRuntime
{
    public sealed class VoxelRuntime : IDisposable
    {
        private IVoxelGpuResourceSystem _gpuResourceSystem;
        private IRayTracingScene _rayTracingScene;
        private IVoxelRayTracingResourceBinder _resourceBinder;
        private bool _isDisposed;

        public VoxelRuntime(
            IVoxelGpuResourceSystem gpuResourceSystem,
            IRayTracingScene rayTracingScene,
            IVoxelRayTracingResourceBinder resourceBinder)
        {
            _gpuResourceSystem = gpuResourceSystem ?? throw new ArgumentNullException(nameof(gpuResourceSystem));
            _rayTracingScene = rayTracingScene ?? throw new ArgumentNullException(nameof(rayTracingScene));
            _resourceBinder = resourceBinder ?? throw new ArgumentNullException(nameof(resourceBinder));
        }

        public bool IsDisposed => _isDisposed;

        public IVoxelGpuResourceSystem GpuResourceSystem
        {
            get
            {
                EnsureAlive();
                return _gpuResourceSystem;
            }
        }

        public IRayTracingScene RayTracingScene
        {
            get
            {
                EnsureAlive();
                return _rayTracingScene;
            }
        }

        public IVoxelRayTracingResourceBinder ResourceBinder
        {
            get
            {
                EnsureAlive();
                return _resourceBinder;
            }
        }

        public void Tick(bool autoBuildScene, bool autoBindGlobals)
        {
            EnsureAlive();

            if (autoBuildScene && _rayTracingScene.HasPendingBuild)
            {
                _rayTracingScene.Build();
            }

            if (autoBindGlobals)
            {
                _resourceBinder.BindGlobals();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _resourceBinder = null;
            _rayTracingScene.Dispose();
            _gpuResourceSystem.Dispose();
            _rayTracingScene = null;
            _gpuResourceSystem = null;
            _isDisposed = true;
        }

        private void EnsureAlive()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelRuntime));
            }
        }
    }
}
