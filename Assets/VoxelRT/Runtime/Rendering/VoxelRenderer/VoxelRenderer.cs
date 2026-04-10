using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;
using VoxelRT.Runtime.Rendering.RayTracingScene;
using VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem;
using VoxelRT.Runtime.Rendering.VoxelRuntime;
using VoxelRuntimeServices = VoxelRT.Runtime.Rendering.VoxelRuntime.VoxelRuntime;

namespace VoxelRT.Runtime.Rendering.VoxelRenderer
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VoxelFilter))]
    public sealed class VoxelRenderer : MonoBehaviour
    {
        private const int InvalidHandle = -1;
        private const int InvalidResidencyId = -1;
        private const int InvalidBootstrapVersion = -1;

        [SerializeField] private VoxelFilter _voxelFilter;
        [FormerlySerializedAs("_context")]
        [SerializeField] private VoxelRuntimeBootstrap _bootstrap;
        [SerializeField] private Material _material;
        [SerializeField] private int _mask = 0xFF;
        [SerializeField] private bool _opaqueMaterial = true;
        [SerializeField] private bool _dynamicGeometry;
        [SerializeField] private bool _overrideBuildFlags;
        [SerializeField] private RayTracingAccelerationStructureBuildFlags _buildFlags = RayTracingAccelerationStructureBuildFlags.None;

        private MaterialPropertyBlock _userPropertyBlock;
        private MaterialPropertyBlock _scenePropertyBlock;
        private int _modelResidencyId = InvalidResidencyId;
        private int _paletteResidencyId = InvalidResidencyId;
        private int _rtasHandle = InvalidHandle;
        private int _resolvedBootstrapVersion = InvalidBootstrapVersion;
        private VoxelFilter _subscribedFilter;
        private VoxelRuntimeBootstrap _boundBootstrap;
        private bool _needsFullRefresh = true;
        private bool _needsPaletteRefresh;

        public int ModelResidencyId => _modelResidencyId;

        public int PaletteResidencyId => _paletteResidencyId;

        public int RtasHandle => _rtasHandle;

        public bool IsRegistered => _rtasHandle != InvalidHandle;

        public Material Material => _material;

        public void GetPropertyBlock(MaterialPropertyBlock properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            EnsureBlocks();
            MaterialPropertyBlockUtility.CopyShaderProperties(_material, _userPropertyBlock, properties);
        }

        public MaterialPropertyBlock GetPropertyBlock()
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            GetPropertyBlock(properties);
            return properties;
        }

        public void SetPropertyBlock(MaterialPropertyBlock properties)
        {
            EnsureBlocks();
            MaterialPropertyBlockUtility.CopyShaderProperties(_material, properties, _userPropertyBlock);
            RebuildScenePropertyBlock();
            PushMaterialPropertyBlock();
            VoxelRuntimeUpdateUtility.RequestEditorUpdate();
        }

        public void Refresh()
        {
            _needsFullRefresh = true;
            ProcessPendingChanges();
        }

        public void MarkGeometryDirty()
        {
            ProcessPendingChanges();

            if (!IsRegistered || !TryResolveRuntime(out VoxelRuntimeServices runtime))
            {
                return;
            }

            runtime.RayTracingScene.MarkGeometryDirty(_rtasHandle);
            VoxelRuntimeUpdateUtility.RequestEditorUpdate();
        }

        private void Reset()
        {
            _voxelFilter = GetComponent<VoxelFilter>();
            _needsFullRefresh = true;
        }

        private void Awake()
        {
            EnsureBlocks();
            TryEnsureComponentReferences();
            SynchronizeFilterSubscription();
        }

        private void OnEnable()
        {
            EnsureBlocks();
            TryEnsureComponentReferences();
            SynchronizeFilterSubscription();
            _needsFullRefresh = true;
            ProcessPendingChanges();
        }

        private void LateUpdate()
        {
            ProcessPendingChanges();

            if (!IsRegistered || !TryResolveRuntime(out VoxelRuntimeServices runtime))
            {
                return;
            }

            if (!transform.hasChanged)
            {
                return;
            }

            runtime.RayTracingScene.UpdateTransform(_rtasHandle, transform.localToWorldMatrix);
            transform.hasChanged = false;
            VoxelRuntimeUpdateUtility.RequestEditorUpdate();
        }

        private void OnDisable()
        {
            UnregisterAndReleaseResources();
            ClearFilterSubscription();
        }

        private void OnValidate()
        {
            EnsureBlocks();
            TryEnsureComponentReferences();
            SynchronizeFilterSubscription();
            _needsFullRefresh = true;
            VoxelRuntimeUpdateUtility.RequestEditorUpdate();
        }

        private void HandleFilterChanged(VoxelFilterChangeFlags changeFlags)
        {
            if ((changeFlags & VoxelFilterChangeFlags.Model) != 0)
            {
                _needsFullRefresh = true;
            }

            if ((changeFlags & VoxelFilterChangeFlags.Palette) != 0)
            {
                _needsPaletteRefresh = true;
            }

            ProcessPendingChanges();
        }

        private void ProcessPendingChanges()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            EnsureBlocks();

            if (!TryEnsureComponentReferences())
            {
                UnregisterAndReleaseResources();
                return;
            }

            SynchronizeFilterSubscription();

            if (!TryResolveBootstrap(out VoxelRuntimeBootstrap bootstrap))
            {
                ForgetRuntimeBinding();
                return;
            }

            if (IsRegistered && (_boundBootstrap != bootstrap || bootstrap.Version != _resolvedBootstrapVersion))
            {
                UnregisterAndReleaseResources();
            }

            VoxelRuntimeServices runtime = bootstrap.Runtime;
            if (_needsFullRefresh)
            {
                if (IsRegistered)
                {
                    UnregisterAndReleaseResources(runtime);
                }

                RegisterAndRetainResources(runtime, bootstrap);
                _needsFullRefresh = false;
                _needsPaletteRefresh = false;
                VoxelRuntimeUpdateUtility.RequestEditorUpdate();
                return;
            }

            if (!IsRegistered)
            {
                RegisterAndRetainResources(runtime, bootstrap);
                _needsPaletteRefresh = false;
                VoxelRuntimeUpdateUtility.RequestEditorUpdate();
                return;
            }

            if (_needsPaletteRefresh)
            {
                RefreshPaletteResidency(runtime);
                _needsPaletteRefresh = false;
                VoxelRuntimeUpdateUtility.RequestEditorUpdate();
            }
        }

        private void RegisterAndRetainResources(VoxelRuntimeServices runtime, VoxelRuntimeBootstrap bootstrap)
        {
            if (IsRegistered)
            {
                return;
            }

            int retainedModelId = InvalidResidencyId;
            int retainedPaletteId = InvalidResidencyId;

            try
            {
                retainedModelId = RetainModel(runtime.GpuResourceSystem);
                retainedPaletteId = RetainPalette(runtime.GpuResourceSystem);

                _modelResidencyId = retainedModelId;
                _paletteResidencyId = retainedPaletteId;
                RebuildScenePropertyBlock();

                RayTracingSceneInstanceDescriptor descriptor = BuildDescriptor(runtime.GpuResourceSystem);
                _rtasHandle = runtime.RayTracingScene.AddInstance(in descriptor);
                _boundBootstrap = bootstrap;
                _resolvedBootstrapVersion = bootstrap.Version;
                transform.hasChanged = false;
            }
            catch
            {
                if (retainedPaletteId != InvalidResidencyId)
                {
                    runtime.GpuResourceSystem.ReleasePalette(retainedPaletteId);
                }

                if (retainedModelId != InvalidResidencyId)
                {
                    runtime.GpuResourceSystem.ReleaseModel(retainedModelId);
                }

                ForgetRuntimeBinding();
                throw;
            }
        }

        private void UnregisterAndReleaseResources()
        {
            if (_boundBootstrap != null
                && _boundBootstrap.IsInitialized
                && _boundBootstrap.Version == _resolvedBootstrapVersion)
            {
                UnregisterAndReleaseResources(_boundBootstrap.Runtime);
                return;
            }

            ForgetRuntimeBinding();
        }

        private void UnregisterAndReleaseResources(VoxelRuntimeServices runtime)
        {
            if (runtime == null || runtime.IsDisposed)
            {
                ForgetRuntimeBinding();
                return;
            }

            if (IsRegistered)
            {
                runtime.RayTracingScene.RemoveInstance(_rtasHandle);
            }

            if (_paletteResidencyId != InvalidResidencyId)
            {
                runtime.GpuResourceSystem.ReleasePalette(_paletteResidencyId);
            }

            if (_modelResidencyId != InvalidResidencyId)
            {
                runtime.GpuResourceSystem.ReleaseModel(_modelResidencyId);
            }

            ForgetRuntimeBinding();
        }

        private int RetainModel(IVoxelGpuResourceSystem resourceSystem)
        {
            NativeArray<byte> occupancyBytes = default;
            NativeArray<byte> voxelBytes = default;
            NativeArray<ModelChunkAabb> chunkAabbs = default;

            try
            {
                VoxelModelUpload upload = _voxelFilter.CreateModelUpload(
                    Allocator.Temp,
                    out occupancyBytes,
                    out voxelBytes,
                    out chunkAabbs);
                return resourceSystem.RetainModel(_voxelFilter.ModelKey, in upload);
            }
            finally
            {
                if (chunkAabbs.IsCreated)
                {
                    chunkAabbs.Dispose();
                }

                if (voxelBytes.IsCreated)
                {
                    voxelBytes.Dispose();
                }

                if (occupancyBytes.IsCreated)
                {
                    occupancyBytes.Dispose();
                }
            }
        }

        private int RetainPalette(IVoxelGpuResourceSystem resourceSystem)
        {
            using NativeArray<byte> paletteBytes = _voxelFilter.CreatePaletteBytes(Allocator.Temp);
            return resourceSystem.RetainPalette(_voxelFilter.PaletteKey, paletteBytes);
        }

        private RayTracingSceneInstanceDescriptor BuildDescriptor(IVoxelGpuResourceView resourceView)
        {
            VoxelModelResourceDescriptor modelDescriptor = resourceView.GetModelResourceDescriptor(_modelResidencyId);
            RayTracingProceduralGeometryDescriptor procedural = new RayTracingProceduralGeometryDescriptor(
                modelDescriptor.ProceduralAabbBuffer,
                modelDescriptor.ProceduralAabbCount,
                0u,
                _material,
                null,
                _opaqueMaterial,
                _dynamicGeometry,
                _overrideBuildFlags,
                _buildFlags);

            return new RayTracingSceneInstanceDescriptor(
                RayTracingGeometryDescriptor.FromProcedural(procedural),
                transform.localToWorldMatrix,
                0u)
            {
                Mask = checked((uint)_mask),
                Layer = gameObject.layer,
                MaterialProperties = _scenePropertyBlock,
            };
        }

        private void RebuildScenePropertyBlock()
        {
            EnsureBlocks();
            MaterialPropertyBlockUtility.CopyShaderProperties(_material, _userPropertyBlock, _scenePropertyBlock);
            _scenePropertyBlock.SetInteger(VoxelMaterialPropertyIds.ModelResidencyId, _modelResidencyId);
            _scenePropertyBlock.SetInteger(VoxelMaterialPropertyIds.PaletteResidencyId, _paletteResidencyId);
        }

        private void PushMaterialPropertyBlock()
        {
            if (!IsRegistered || !TryResolveRuntime(out VoxelRuntimeServices runtime))
            {
                return;
            }

            runtime.RayTracingScene.UpdateMaterialPropertyBlock(_rtasHandle, _scenePropertyBlock);
        }

        private void RefreshPaletteResidency(VoxelRuntimeServices runtime)
        {
            if (!IsRegistered)
            {
                return;
            }

            int previousPaletteId = _paletteResidencyId;
            int retainedPaletteId = InvalidResidencyId;

            try
            {
                retainedPaletteId = RetainPalette(runtime.GpuResourceSystem);
                _paletteResidencyId = retainedPaletteId;
                RebuildScenePropertyBlock();
                PushMaterialPropertyBlock();

                if (previousPaletteId != InvalidResidencyId)
                {
                    runtime.GpuResourceSystem.ReleasePalette(previousPaletteId);
                }
            }
            catch
            {
                if (retainedPaletteId != InvalidResidencyId)
                {
                    runtime.GpuResourceSystem.ReleasePalette(retainedPaletteId);
                }

                _paletteResidencyId = previousPaletteId;
                RebuildScenePropertyBlock();
                throw;
            }
        }

        private void ForgetRuntimeBinding()
        {
            _rtasHandle = InvalidHandle;
            _modelResidencyId = InvalidResidencyId;
            _paletteResidencyId = InvalidResidencyId;
            _resolvedBootstrapVersion = InvalidBootstrapVersion;
            _boundBootstrap = null;
        }

        private bool TryResolveBootstrap(out VoxelRuntimeBootstrap bootstrap)
        {
            if (VoxelRuntimeBootstrapResolver.TryResolve(gameObject, _bootstrap, out bootstrap))
            {
                _bootstrap = bootstrap;
                return true;
            }

            bootstrap = null;
            return false;
        }

        private bool TryResolveRuntime(out VoxelRuntimeServices runtime)
        {
            if (_boundBootstrap != null
                && _boundBootstrap.IsInitialized
                && _boundBootstrap.Version == _resolvedBootstrapVersion)
            {
                runtime = _boundBootstrap.Runtime;
                return true;
            }

            if (TryResolveBootstrap(out VoxelRuntimeBootstrap bootstrap))
            {
                runtime = bootstrap.Runtime;
                return true;
            }

            runtime = null;
            return false;
        }

        private void EnsureBlocks()
        {
            _userPropertyBlock ??= new MaterialPropertyBlock();
            _scenePropertyBlock ??= new MaterialPropertyBlock();
        }

        private bool TryEnsureComponentReferences()
        {
            if (_voxelFilter == null)
            {
                _voxelFilter = GetComponent<VoxelFilter>();
            }

            return _voxelFilter != null && _material != null;
        }

        private void SynchronizeFilterSubscription()
        {
            if (_subscribedFilter == _voxelFilter)
            {
                return;
            }

            ClearFilterSubscription();

            if (_voxelFilter == null)
            {
                return;
            }

            _voxelFilter.Changed += HandleFilterChanged;
            _subscribedFilter = _voxelFilter;
        }

        private void ClearFilterSubscription()
        {
            if (_subscribedFilter == null)
            {
                return;
            }

            _subscribedFilter.Changed -= HandleFilterChanged;
            _subscribedFilter = null;
        }
    }
}
