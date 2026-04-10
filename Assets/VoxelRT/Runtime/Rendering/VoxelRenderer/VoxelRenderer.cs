using System;
using System.Text;
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
        private const int InvalidHandle = 0;
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
        private GraphicsBuffer _chunkAabbBuffer;
        private int _modelResidencyId = InvalidResidencyId;
        private int _paletteResidencyId = InvalidResidencyId;
        private int _rtasHandle = InvalidHandle;
        private int _resolvedBootstrapVersion = InvalidBootstrapVersion;
        private VoxelFilter _subscribedFilter;
        private VoxelRuntimeBootstrap _boundBootstrap;
        private bool _needsFullRefresh = true;
        private bool _needsPaletteRefresh;
        private string _lastRegistrationFailureDiagnostics;

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
            bool hasModelDescriptor = false;
            VoxelModelResourceDescriptor modelDescriptor = default;
            RayTracingAABBsInstanceConfig instanceConfig = default;

            try
            {
                retainedModelId = RetainModel(runtime.GpuResourceSystem);
                retainedPaletteId = RetainPalette(runtime.GpuResourceSystem);

                _modelResidencyId = retainedModelId;
                _paletteResidencyId = retainedPaletteId;
                modelDescriptor = runtime.GpuResourceSystem.GetModelResourceDescriptor(_modelResidencyId);
                hasModelDescriptor = true;
                _chunkAabbBuffer = modelDescriptor.ProceduralAabbBuffer;
                RebuildScenePropertyBlock();

                instanceConfig = BuildInstanceConfig(modelDescriptor);
                _rtasHandle = runtime.RayTracingScene.AddInstance(in instanceConfig, transform.localToWorldMatrix);
                _lastRegistrationFailureDiagnostics = null;
                _boundBootstrap = bootstrap;
                _resolvedBootstrapVersion = bootstrap.Version;
                transform.hasChanged = false;
            }
            catch (Exception exception)
            {
                LogRegistrationFailure(runtime, bootstrap, hasModelDescriptor, modelDescriptor, instanceConfig, exception);

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

        private RayTracingAABBsInstanceConfig BuildInstanceConfig(VoxelModelResourceDescriptor modelDescriptor)
        {
            return new RayTracingAABBsInstanceConfig
            {
                aabbBuffer = modelDescriptor.ProceduralAabbBuffer,
                aabbCount = modelDescriptor.ProceduralAabbCount,
                aabbOffset = 0,
                material = _material,
                materialProperties = _scenePropertyBlock,
                opaqueMaterial = _opaqueMaterial,
                dynamicGeometry = _dynamicGeometry,
                accelerationStructureBuildFlagsOverride = _overrideBuildFlags,
                accelerationStructureBuildFlags = _buildFlags,
                mask = checked((uint)_mask),
                layer = gameObject.layer,
            };
        }

        private void RebuildScenePropertyBlock()
        {
            EnsureBlocks();
            MaterialPropertyBlockUtility.CopyShaderProperties(_material, _userPropertyBlock, _scenePropertyBlock);
            _scenePropertyBlock.SetInteger(VoxelMaterialPropertyIds.ModelResidencyId, _modelResidencyId);
            _scenePropertyBlock.SetInteger(VoxelMaterialPropertyIds.PaletteResidencyId, _paletteResidencyId);
            _scenePropertyBlock.SetFloat(VoxelMaterialPropertyIds.OpaqueMaterial, _opaqueMaterial ? 1.0f : 0.0f);
            if (_chunkAabbBuffer != null)
            {
                _scenePropertyBlock.SetBuffer(VoxelMaterialPropertyIds.ChunkAabbBuffer, _chunkAabbBuffer);
            }
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
            _chunkAabbBuffer = null;
            _resolvedBootstrapVersion = InvalidBootstrapVersion;
            _boundBootstrap = null;
        }

        private void LogRegistrationFailure(
            VoxelRuntimeServices runtime,
            VoxelRuntimeBootstrap bootstrap,
            bool hasModelDescriptor,
            VoxelModelResourceDescriptor modelDescriptor,
            RayTracingAABBsInstanceConfig instanceConfig,
            Exception exception)
        {
            StringBuilder builder = new StringBuilder(1024);
            builder.AppendLine("VoxelRenderer registration failed.");
            builder.AppendLine($"Exception={exception.GetType().Name}: {exception.Message}");
            builder.AppendLine($"Renderer='{name}', Active={isActiveAndEnabled}, Layer={gameObject.layer}, Mask={_mask}");
            builder.AppendLine(
                $"Bootstrap='{(bootstrap != null ? bootstrap.name : "<null>")}', " +
                $"BootstrapInitialized={(bootstrap != null && bootstrap.IsInitialized)}, " +
                $"BootstrapVersion={(bootstrap != null ? bootstrap.Version : InvalidBootstrapVersion)}");
            builder.AppendLine(
                $"SupportsRayTracing={SystemInfo.supportsRayTracing}, GraphicsDeviceType={SystemInfo.graphicsDeviceType}");
            builder.AppendLine(
                $"Material='{(_material != null ? _material.name : "<null>")}', " +
                $"Shader='{(_material != null && _material.shader != null ? _material.shader.name : "<null>")}', " +
                $"OpaqueMaterial={_opaqueMaterial}, DynamicGeometry={_dynamicGeometry}, " +
                $"OverrideBuildFlags={_overrideBuildFlags}, BuildFlags={_buildFlags}");
            builder.AppendLine(
                $"Residency: ModelId={_modelResidencyId}, PaletteId={_paletteResidencyId}, " +
                $"ScenePropertyBlockReady={_scenePropertyBlock != null}, ChunkAabbBufferReady={_chunkAabbBuffer != null}");

            if (_material != null && _material.shader != null)
            {
                Shader shader = _material.shader;
                ShaderTagId lightModeTag = new ShaderTagId("LightMode");
                builder.AppendLine($"ShaderPassCount={shader.passCount}");
                for (int passIndex = 0; passIndex < shader.passCount; passIndex++)
                {
                    string passName = _material.GetPassName(passIndex);
                    string lightMode = shader.FindPassTagValue(passIndex, lightModeTag).name;
                    builder.AppendLine($"  Pass[{passIndex}]: Name='{passName}', LightMode='{lightMode}'");
                }
            }

            if (hasModelDescriptor)
            {
                builder.AppendLine(
                    $"ProceduralAabb: Count={modelDescriptor.ProceduralAabbCount}, " +
                    $"Buffer={DescribeBuffer(modelDescriptor.ProceduralAabbBuffer)}");
            }

            builder.AppendLine(
                $"ConfigMaterialProperties={(instanceConfig.materialProperties != null)}, " +
                $"ConfigMask={instanceConfig.mask}, ConfigLayer={instanceConfig.layer}, " +
                $"ConfigOpaque={instanceConfig.opaqueMaterial}, ConfigDynamic={instanceConfig.dynamicGeometry}, " +
                $"ConfigAabbOffset={instanceConfig.aabbOffset}");

            if (runtime != null && !runtime.IsDisposed)
            {
                builder.AppendLine("GlobalBuffers:");
                builder.AppendLine($"  OccupancyChunkBuffer={DescribeBuffer(runtime.GpuResourceSystem.OccupancyChunkBuffer)}");
                builder.AppendLine($"  VoxelDataChunkBuffer={DescribeBuffer(runtime.GpuResourceSystem.VoxelDataChunkBuffer)}");
                builder.AppendLine($"  ModelChunkStartBuffer={DescribeBuffer(runtime.GpuResourceSystem.ModelChunkStartBuffer)}");
                builder.AppendLine($"  PaletteChunkBuffer={DescribeBuffer(runtime.GpuResourceSystem.PaletteChunkBuffer)}");
                builder.AppendLine($"  PaletteChunkStartBuffer={DescribeBuffer(runtime.GpuResourceSystem.PaletteChunkStartBuffer)}");
                builder.AppendLine($"  SurfaceTypeTableBuffer={DescribeBuffer(runtime.GpuResourceSystem.SurfaceTypeTableBuffer)}");
                builder.AppendLine(
                    $"  Strides: ModelChunkStart={runtime.GpuResourceSystem.ModelChunkStartStrideBytes}, " +
                    $"PaletteChunkStart={runtime.GpuResourceSystem.PaletteChunkStartStrideBytes}, " +
                    $"SurfaceTypeTable={runtime.GpuResourceSystem.SurfaceTypeTableStrideBytes}, " +
                    $"SurfaceTypeEntryCount={runtime.GpuResourceSystem.SurfaceTypeEntryCount}");
            }

            string diagnostics = builder.ToString();
            if (!string.Equals(_lastRegistrationFailureDiagnostics, diagnostics, StringComparison.Ordinal))
            {
                _lastRegistrationFailureDiagnostics = diagnostics;
                Debug.LogError(diagnostics, this);
            }
        }

        private static string DescribeBuffer(GraphicsBuffer buffer)
        {
            if (buffer == null)
            {
                return "<null>";
            }

            return $"count={buffer.count}, stride={buffer.stride}, target={buffer.target}";
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
