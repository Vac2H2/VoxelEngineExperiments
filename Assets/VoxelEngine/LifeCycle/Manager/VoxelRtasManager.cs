using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Render.RenderBackend;
using VoxelExperiments.Runtime.Rendering.RayTracingScene;

namespace VoxelEngine.LifeCycle.Manager
{
    public sealed class VoxelRtasManager : IDisposable
    {
        private const int InvalidShaderPropertyId = int.MinValue;
        public const uint OpaqueInstanceMask = 1u << 0;
        public const uint TransparentInstanceMask = 1u << 1;
        public const uint DebugAabbOverlayInstanceMask = 1u << 2;
        public const uint AllInstanceMask = OpaqueInstanceMask | TransparentInstanceMask;

        private readonly Material _material;
        private readonly RayTracingScene _rayTracingScene;
        private readonly VoxelRtasManagerConfiguration _configuration;
        private readonly Dictionary<int, MaterialPropertyBlock> _materialPropertiesByHandle =
            new Dictionary<int, MaterialPropertyBlock>();
        private readonly int _volumeBufferPropertyId;
        private readonly int _aabbDescBufferPropertyId;
        private readonly int _aabbBufferPropertyId;
        private readonly int _chunkCountPropertyId;
        private readonly int _aabbCountPropertyId;
        private readonly int _paletteColorBufferPropertyId;
        private readonly int _paletteColorCountPropertyId;
        private readonly int _opaqueMaterialPropertyId;
        private readonly int _debugAabbOverlayPropertyId;
        private readonly int _debugAabbLineWidthPropertyId;
        private bool _isDisposed;

        public VoxelRtasManager(Material material)
            : this(material, VoxelRtasManagerConfiguration.Default)
        {
        }

        public VoxelRtasManager(Material material, VoxelRtasManagerConfiguration configuration)
        {
            _material = material ?? throw new ArgumentNullException(nameof(material));
            _configuration = configuration;
            _rayTracingScene = new RayTracingScene(configuration.SceneConfiguration);
            _volumeBufferPropertyId = ResolveShaderPropertyId(configuration.VolumeBufferPropertyName);
            _aabbDescBufferPropertyId = ResolveShaderPropertyId(configuration.AabbDescBufferPropertyName);
            _aabbBufferPropertyId = ResolveShaderPropertyId(configuration.AabbBufferPropertyName);
            _chunkCountPropertyId = ResolveShaderPropertyId(configuration.ChunkCountPropertyName);
            _aabbCountPropertyId = ResolveShaderPropertyId(configuration.AabbCountPropertyName);
            _paletteColorBufferPropertyId = ResolveShaderPropertyId(configuration.PaletteColorBufferPropertyName);
            _paletteColorCountPropertyId = ResolveShaderPropertyId(configuration.PaletteColorCountPropertyName);
            _opaqueMaterialPropertyId = ResolveShaderPropertyId(configuration.OpaqueMaterialPropertyName);
            _debugAabbOverlayPropertyId = ResolveShaderPropertyId(configuration.DebugAabbOverlayPropertyName);
            _debugAabbLineWidthPropertyId = ResolveShaderPropertyId(configuration.DebugAabbLineWidthPropertyName);
        }

        public RayTracingAccelerationStructure AccelerationStructure
        {
            get
            {
                EnsureNotDisposed();
                return _rayTracingScene.AccelerationStructure;
            }
        }

        public IRayTracingScene Scene
        {
            get
            {
                EnsureNotDisposed();
                return _rayTracingScene;
            }
        }

        public bool HasPendingBuild
        {
            get
            {
                EnsureNotDisposed();
                return _rayTracingScene.HasPendingBuild;
            }
        }

        public VoxelRtasHandle AddInstance(in VoxelBlasGpuView blasGpuView, Matrix4x4 localToWorld, bool isOpaque)
        {
            EnsureNotDisposed();
            VoxelVolumeGpuView volumeGpuView = blasGpuView.GetVolume(isOpaque);
            VoxelPaletteGpuView paletteGpuView = blasGpuView.Palette;
            ValidateVolumeGpuView(in volumeGpuView, isOpaque);

            MaterialPropertyBlock materialPropertyBlock = CreateMaterialPropertyBlock(
                in volumeGpuView,
                in paletteGpuView,
                isOpaque,
                isDebugAabbOverlay: false,
                debugAabbLineWidth: 0.0f);
            RayTracingAABBsInstanceConfig instanceConfig = CreateInstanceConfig(
                in volumeGpuView,
                materialPropertyBlock,
                isOpaque,
                ResolveInstanceMask(isOpaque));

            int rawHandle = _rayTracingScene.AddInstance(in instanceConfig, localToWorld);

            try
            {
                _materialPropertiesByHandle.Add(rawHandle, materialPropertyBlock);
                return new VoxelRtasHandle(rawHandle);
            }
            catch
            {
                _rayTracingScene.RemoveInstance(rawHandle);
                throw;
            }
        }

        public VoxelRtasHandle AddDebugAabbOverlayInstance(
            in VoxelBlasGpuView blasGpuView,
            Matrix4x4 localToWorld,
            float lineWidth)
        {
            EnsureNotDisposed();
            VoxelVolumeGpuView volumeGpuView = blasGpuView.Model.Opaque;
            VoxelPaletteGpuView paletteGpuView = blasGpuView.Palette;
            ValidateVolumeGpuView(in volumeGpuView, isOpaque: true);

            MaterialPropertyBlock materialPropertyBlock = CreateMaterialPropertyBlock(
                in volumeGpuView,
                in paletteGpuView,
                isOpaque: true,
                isDebugAabbOverlay: true,
                debugAabbLineWidth: lineWidth);
            RayTracingAABBsInstanceConfig instanceConfig = CreateInstanceConfig(
                in volumeGpuView,
                materialPropertyBlock,
                isOpaque: true,
                DebugAabbOverlayInstanceMask);

            int rawHandle = _rayTracingScene.AddInstance(in instanceConfig, localToWorld);

            try
            {
                _materialPropertiesByHandle.Add(rawHandle, materialPropertyBlock);
                return new VoxelRtasHandle(rawHandle);
            }
            catch
            {
                _rayTracingScene.RemoveInstance(rawHandle);
                throw;
            }
        }

        public void RemoveInstance(VoxelRtasHandle handle)
        {
            EnsureNotDisposed();
            ValidateHandle(handle);

            if (!_materialPropertiesByHandle.Remove(handle.Value))
            {
                throw new ArgumentException("VoxelRtasHandle is not registered.", nameof(handle));
            }

            _rayTracingScene.RemoveInstance(handle.Value);
        }

        public void Clear()
        {
            EnsureNotDisposed();
            _rayTracingScene.Clear();
            _materialPropertiesByHandle.Clear();
        }

        public void Build()
        {
            EnsureNotDisposed();
            _rayTracingScene.Build();
        }

        public void Build(Vector3 relativeOrigin)
        {
            EnsureNotDisposed();
            _rayTracingScene.Build(relativeOrigin);
        }

        public void Build(CommandBuffer commandBuffer)
        {
            EnsureNotDisposed();
            _rayTracingScene.Build(commandBuffer);
        }

        public void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin)
        {
            EnsureNotDisposed();
            _rayTracingScene.Build(commandBuffer, relativeOrigin);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _materialPropertiesByHandle.Clear();
            _rayTracingScene.Dispose();
            _isDisposed = true;
        }

        private RayTracingAABBsInstanceConfig CreateInstanceConfig(
            in VoxelVolumeGpuView volumeGpuView,
            MaterialPropertyBlock materialPropertyBlock,
            bool isOpaque,
            uint mask)
        {
            return new RayTracingAABBsInstanceConfig
            {
                aabbBuffer = volumeGpuView.AabbBuffer,
                aabbCount = volumeGpuView.AabbCount,
                aabbOffset = 0,
                material = _material,
                materialProperties = materialPropertyBlock,
                opaqueMaterial = isOpaque,
                dynamicGeometry = _configuration.DynamicGeometry,
                accelerationStructureBuildFlagsOverride = _configuration.OverrideBuildFlags,
                accelerationStructureBuildFlags = _configuration.BuildFlags,
                mask = mask,
                layer = _configuration.Layer,
            };
        }

        private static uint ResolveInstanceMask(bool isOpaque)
        {
            return isOpaque
                ? OpaqueInstanceMask
                : TransparentInstanceMask;
        }

        private MaterialPropertyBlock CreateMaterialPropertyBlock(
            in VoxelVolumeGpuView volumeGpuView,
            in VoxelPaletteGpuView paletteGpuView,
            bool isOpaque,
            bool isDebugAabbOverlay,
            float debugAabbLineWidth)
        {
            MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
            ApplyBufferProperty(materialPropertyBlock, _volumeBufferPropertyId, volumeGpuView.VolumeBuffer);
            ApplyBufferProperty(materialPropertyBlock, _aabbDescBufferPropertyId, volumeGpuView.AabbDescBuffer);
            ApplyBufferProperty(materialPropertyBlock, _aabbBufferPropertyId, volumeGpuView.AabbBuffer);
            ApplyIntegerProperty(materialPropertyBlock, _chunkCountPropertyId, volumeGpuView.ChunkCount);
            ApplyIntegerProperty(materialPropertyBlock, _aabbCountPropertyId, volumeGpuView.AabbCount);
            ApplyBufferProperty(materialPropertyBlock, _paletteColorBufferPropertyId, paletteGpuView.ColorBuffer);
            ApplyIntegerProperty(materialPropertyBlock, _paletteColorCountPropertyId, paletteGpuView.ColorCount);
            ApplyFloatProperty(materialPropertyBlock, _opaqueMaterialPropertyId, isOpaque ? 1.0f : 0.0f);
            ApplyFloatProperty(materialPropertyBlock, _debugAabbOverlayPropertyId, isDebugAabbOverlay ? 1.0f : 0.0f);
            ApplyFloatProperty(materialPropertyBlock, _debugAabbLineWidthPropertyId, Mathf.Max(debugAabbLineWidth, 0.0f));
            return materialPropertyBlock;
        }

        private static int ResolveShaderPropertyId(string propertyName)
        {
            return string.IsNullOrWhiteSpace(propertyName)
                ? InvalidShaderPropertyId
                : Shader.PropertyToID(propertyName);
        }

        private static void ApplyBufferProperty(
            MaterialPropertyBlock materialPropertyBlock,
            int propertyId,
            GraphicsBuffer buffer)
        {
            if (propertyId == InvalidShaderPropertyId)
            {
                return;
            }

            materialPropertyBlock.SetBuffer(propertyId, buffer);
        }

        private static void ApplyIntegerProperty(MaterialPropertyBlock materialPropertyBlock, int propertyId, int value)
        {
            if (propertyId == InvalidShaderPropertyId)
            {
                return;
            }

            materialPropertyBlock.SetInteger(propertyId, value);
        }

        private static void ApplyFloatProperty(MaterialPropertyBlock materialPropertyBlock, int propertyId, float value)
        {
            if (propertyId == InvalidShaderPropertyId)
            {
                return;
            }

            materialPropertyBlock.SetFloat(propertyId, value);
        }

        private static void ValidateVolumeGpuView(in VoxelVolumeGpuView volumeGpuView, bool isOpaque)
        {
            if (volumeGpuView.AabbCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(volumeGpuView),
                    $"VoxelBlasGpuView does not contain any {(isOpaque ? "opaque" : "transparent")} AABBs.");
            }
        }

        private static void ValidateHandle(VoxelRtasHandle handle)
        {
            if (!handle.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), "VoxelRtasHandle must be non-zero.");
            }
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelRtasManager));
            }
        }
    }

    public readonly struct VoxelRtasManagerConfiguration
    {
        public VoxelRtasManagerConfiguration(
            RayTracingSceneConfiguration sceneConfiguration,
            bool dynamicGeometry,
            bool overrideBuildFlags,
            RayTracingAccelerationStructureBuildFlags buildFlags,
            uint mask,
            int layer,
            string volumeBufferPropertyName,
            string aabbDescBufferPropertyName,
            string aabbBufferPropertyName,
            string chunkCountPropertyName,
            string aabbCountPropertyName,
            string paletteColorBufferPropertyName,
            string paletteColorCountPropertyName,
            string opaqueMaterialPropertyName,
            string debugAabbOverlayPropertyName,
            string debugAabbLineWidthPropertyName)
        {
            if (layer < 0 || layer > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer must be between 0 and 31.");
            }

            SceneConfiguration = sceneConfiguration;
            DynamicGeometry = dynamicGeometry;
            OverrideBuildFlags = overrideBuildFlags;
            BuildFlags = buildFlags;
            Mask = mask;
            Layer = layer;
            VolumeBufferPropertyName = volumeBufferPropertyName;
            AabbDescBufferPropertyName = aabbDescBufferPropertyName;
            AabbBufferPropertyName = aabbBufferPropertyName;
            ChunkCountPropertyName = chunkCountPropertyName;
            AabbCountPropertyName = aabbCountPropertyName;
            PaletteColorBufferPropertyName = paletteColorBufferPropertyName;
            PaletteColorCountPropertyName = paletteColorCountPropertyName;
            OpaqueMaterialPropertyName = opaqueMaterialPropertyName;
            DebugAabbOverlayPropertyName = debugAabbOverlayPropertyName;
            DebugAabbLineWidthPropertyName = debugAabbLineWidthPropertyName;
        }

        public RayTracingSceneConfiguration SceneConfiguration { get; }

        public bool DynamicGeometry { get; }

        public bool OverrideBuildFlags { get; }

        public RayTracingAccelerationStructureBuildFlags BuildFlags { get; }

        public uint Mask { get; }

        public int Layer { get; }

        public string VolumeBufferPropertyName { get; }

        public string AabbDescBufferPropertyName { get; }

        public string AabbBufferPropertyName { get; }

        public string ChunkCountPropertyName { get; }

        public string AabbCountPropertyName { get; }

        public string PaletteColorBufferPropertyName { get; }

        public string PaletteColorCountPropertyName { get; }

        public string OpaqueMaterialPropertyName { get; }

        public string DebugAabbOverlayPropertyName { get; }

        public string DebugAabbLineWidthPropertyName { get; }

        public static VoxelRtasManagerConfiguration Default =>
            new VoxelRtasManagerConfiguration(
                RayTracingSceneConfiguration.Default,
                dynamicGeometry: true,
                overrideBuildFlags: false,
                buildFlags: RayTracingAccelerationStructureBuildFlags.None,
                mask: 0xFFu,
                layer: 0,
                volumeBufferPropertyName: "_VoxelVolumeBuffer",
                aabbDescBufferPropertyName: "_VoxelAabbDescBuffer",
                aabbBufferPropertyName: "_VoxelAabbBuffer",
                chunkCountPropertyName: "_VoxelChunkCount",
                aabbCountPropertyName: "_VoxelAabbCount",
                paletteColorBufferPropertyName: "_VoxelPaletteColorBuffer",
                paletteColorCountPropertyName: "_VoxelPaletteColorCount",
                opaqueMaterialPropertyName: "_VoxelOpaqueMaterial",
                debugAabbOverlayPropertyName: "_VoxelDebugAabbOverlay",
                debugAabbLineWidthPropertyName: "_VoxelDebugAabbLineWidth");
    }

    public readonly struct VoxelRtasHandle : IEquatable<VoxelRtasHandle>
    {
        public const int InvalidValue = 0;

        internal VoxelRtasHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != InvalidValue;

        public bool Equals(VoxelRtasHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelRtasHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(VoxelRtasHandle left, VoxelRtasHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelRtasHandle left, VoxelRtasHandle right)
        {
            return !left.Equals(right);
        }
    }
}
