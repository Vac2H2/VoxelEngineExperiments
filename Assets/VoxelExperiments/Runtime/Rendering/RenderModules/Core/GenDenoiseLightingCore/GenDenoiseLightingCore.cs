using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
{
    [Serializable]
    public sealed class GenDenoiseLightingCore : IDisposable
    {
        private const string TemporalShaderName = "Hidden/VoxelExperiments/Rendering/RtLightingTemporalDenoise";
        private const string SpatialShaderName = "Hidden/VoxelExperiments/Rendering/RtLightingSpatialDenoise";

        [SerializeField, Range(0.0f, 0.99f)] private float _historyWeight = 0.9f;
        [SerializeField, Min(0.0f)] private float _depthRejectRelativeThreshold = 0.05f;
        [SerializeField, Range(-1.0f, 1.0f)] private float _normalRejectCosThreshold = 0.8f;
        [SerializeField, Min(0.0f)] private float _spatialDepthSigma = 24.0f;
        [SerializeField, Min(0.0f)] private float _spatialNormalPower = 16.0f;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.B10G11R11_UFloatPack32;

        [NonSerialized] private Material _temporalMaterial;
        [NonSerialized] private Material _spatialMaterial;
        [NonSerialized] private readonly Dictionary<int, CameraHistoryState> _historyByCamera = new();

        private sealed class CameraHistoryState : IDisposable
        {
            public RenderTexture LightingTexture;
            public RenderTexture DepthTexture;
            public RenderTexture NormalTexture;
            public int Width;
            public int Height;
            public bool IsValid;
            public Vector3 PreviousCameraPosition;
            public Vector3 PreviousCameraForward;

            public void Dispose()
            {
                ReleaseTexture(ref LightingTexture);
                ReleaseTexture(ref DepthTexture);
                ReleaseTexture(ref NormalTexture);
                Width = 0;
                Height = 0;
                IsValid = false;
                PreviousCameraPosition = Vector3.zero;
                PreviousCameraForward = Vector3.forward;
            }
        }

        public readonly struct RenderData
        {
            public RenderData(
                Camera camera,
                int width,
                int height,
                Matrix4x4 pixelCoordToViewDirWs,
                Vector3 cameraPosition,
                Vector3 cameraForward,
                Vector3 previousCameraPosition,
                Vector3 previousCameraForward,
                bool hasHistory)
            {
                Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
                Width = width;
                Height = height;
                PixelCoordToViewDirWs = pixelCoordToViewDirWs;
                CameraPosition = cameraPosition;
                CameraForward = cameraForward;
                PreviousCameraPosition = previousCameraPosition;
                PreviousCameraForward = previousCameraForward;
                HasHistory = hasHistory;
            }

            public Camera Camera { get; }

            public int Width { get; }

            public int Height { get; }

            public Matrix4x4 PixelCoordToViewDirWs { get; }

            public Vector3 CameraPosition { get; }

            public Vector3 CameraForward { get; }

            public Vector3 PreviousCameraPosition { get; }

            public Vector3 PreviousCameraForward { get; }

            public bool HasHistory { get; }
        }

        public bool TryCreateRenderData(Camera camera, out RenderData renderData)
        {
            renderData = default;

            if (camera == null || !EnsureMaterials())
            {
                return false;
            }

            int width = Mathf.Max(camera.pixelWidth, 1);
            int height = Mathf.Max(camera.pixelHeight, 1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            CameraHistoryState historyState = GetOrCreateHistoryState(camera, width, height);
            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            Vector3 previousCameraPosition = historyState.IsValid ? historyState.PreviousCameraPosition : cameraPosition;
            Vector3 previousCameraForward = historyState.IsValid ? historyState.PreviousCameraForward : cameraForward;

            renderData = new RenderData(
                camera,
                width,
                height,
                ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, width, height),
                cameraPosition,
                cameraForward,
                previousCameraPosition,
                previousCameraForward,
                historyState.IsValid);
            return true;
        }

        public void RecordDenoise(CommandBuffer commandBuffer, in RenderData renderData)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (_temporalMaterial == null || _spatialMaterial == null)
            {
                throw new InvalidOperationException("Denoise materials have not been initialized.");
            }

            CameraHistoryState historyState = GetOrCreateHistoryState(renderData.Camera, renderData.Width, renderData.Height);

            commandBuffer.GetTemporaryRT(
                VoxelExperimentsLightingIds.AfterTemporalTextureId,
                CreateTextureDescriptor(renderData.Width, renderData.Height, ResolveOutputFormat()),
                FilterMode.Bilinear);
            commandBuffer.GetTemporaryRT(
                VoxelExperimentsLightingIds.AfterSpatialTextureId,
                CreateTextureDescriptor(renderData.Width, renderData.Height, ResolveOutputFormat()),
                FilterMode.Bilinear);

            ConfigureTemporalMaterial(historyState, in renderData);
            commandBuffer.Blit(
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.Raw),
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.AfterTemporal),
                _temporalMaterial);
            commandBuffer.SetGlobalTexture(
                VoxelExperimentsLightingIds.AfterTemporalTextureId,
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.AfterTemporal));

            ConfigureSpatialMaterial();
            commandBuffer.Blit(
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.AfterTemporal),
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.AfterSpatial),
                _spatialMaterial);
            commandBuffer.SetGlobalTexture(
                VoxelExperimentsLightingIds.AfterSpatialTextureId,
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.AfterSpatial));

            UpdateHistoryTextures(commandBuffer, historyState);
            historyState.PreviousCameraPosition = renderData.CameraPosition;
            historyState.PreviousCameraForward = renderData.CameraForward;
            historyState.IsValid = true;
        }

        public void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            commandBuffer.ReleaseTemporaryRT(VoxelExperimentsLightingIds.AfterTemporalTextureId);
            commandBuffer.ReleaseTemporaryRT(VoxelExperimentsLightingIds.AfterSpatialTextureId);
        }

        public void Dispose()
        {
            DestroyMaterial(ref _temporalMaterial);
            DestroyMaterial(ref _spatialMaterial);

            foreach (CameraHistoryState historyState in _historyByCamera.Values)
            {
                historyState.Dispose();
            }

            _historyByCamera.Clear();
        }

        private void ConfigureTemporalMaterial(CameraHistoryState historyState, in RenderData renderData)
        {
            _temporalMaterial.SetFloat("_HasLightingHistory", renderData.HasHistory ? 1.0f : 0.0f);
            _temporalMaterial.SetFloat("_HistoryWeight", Mathf.Clamp01(_historyWeight));
            _temporalMaterial.SetFloat("_DepthRejectRelativeThreshold", Mathf.Max(_depthRejectRelativeThreshold, 0.0f));
            _temporalMaterial.SetFloat("_NormalRejectCosThreshold", Mathf.Clamp(_normalRejectCosThreshold, -1.0f, 1.0f));
            _temporalMaterial.SetMatrix("_PixelCoordToViewDirWS", renderData.PixelCoordToViewDirWs);
            _temporalMaterial.SetVector("_CameraPositionWS", renderData.CameraPosition);
            _temporalMaterial.SetVector("_CameraForwardWS", renderData.CameraForward);
            _temporalMaterial.SetVector("_PrevCameraPositionWS", renderData.PreviousCameraPosition);
            _temporalMaterial.SetVector("_PrevCameraForwardWS", renderData.PreviousCameraForward);
            _temporalMaterial.SetTexture("_HistoryLightingTex", historyState.LightingTexture);
            _temporalMaterial.SetTexture("_HistoryDepthTex", historyState.DepthTexture);
            _temporalMaterial.SetTexture("_HistoryNormalTex", historyState.NormalTexture);
        }

        private void ConfigureSpatialMaterial()
        {
            _spatialMaterial.SetFloat("_SpatialDepthSigma", Mathf.Max(_spatialDepthSigma, 0.0f));
            _spatialMaterial.SetFloat("_SpatialNormalPower", Mathf.Max(_spatialNormalPower, 0.0f));
        }

        private void UpdateHistoryTextures(CommandBuffer commandBuffer, CameraHistoryState historyState)
        {
            commandBuffer.CopyTexture(
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.AfterSpatial),
                new RenderTargetIdentifier(historyState.LightingTexture));
            commandBuffer.CopyTexture(
                VoxelExperimentsGbufferIds.GetRenderTargetIdentifier(VoxelExperimentsGbufferTexture.Depth),
                new RenderTargetIdentifier(historyState.DepthTexture));
            commandBuffer.CopyTexture(
                VoxelExperimentsGbufferIds.GetRenderTargetIdentifier(VoxelExperimentsGbufferTexture.Normal),
                new RenderTargetIdentifier(historyState.NormalTexture));
        }

        private CameraHistoryState GetOrCreateHistoryState(Camera camera, int width, int height)
        {
            int cameraId = camera.GetInstanceID();
            if (!_historyByCamera.TryGetValue(cameraId, out CameraHistoryState historyState))
            {
                historyState = new CameraHistoryState();
                _historyByCamera.Add(cameraId, historyState);
            }

            EnsureHistoryTextures(historyState, width, height);
            return historyState;
        }

        private void EnsureHistoryTextures(CameraHistoryState historyState, int width, int height)
        {
            if (historyState.Width == width &&
                historyState.Height == height &&
                historyState.LightingTexture != null &&
                historyState.DepthTexture != null &&
                historyState.NormalTexture != null)
            {
                return;
            }

            historyState.Dispose();
            historyState.Width = width;
            historyState.Height = height;
            historyState.LightingTexture = CreateHistoryTexture(
                "VoxelExperimentsLightingHistory",
                width,
                height,
                ResolveOutputFormat(),
                FilterMode.Bilinear);
            historyState.DepthTexture = CreateHistoryTexture(
                "VoxelExperimentsDepthHistory",
                width,
                height,
                GraphicsFormat.R16_SFloat,
                FilterMode.Point);
            historyState.NormalTexture = CreateHistoryTexture(
                "VoxelExperimentsNormalHistory",
                width,
                height,
                GraphicsFormat.R8G8B8A8_UNorm,
                FilterMode.Point);
        }

        private bool EnsureMaterials()
        {
            return EnsureMaterial(ref _temporalMaterial, TemporalShaderName) &&
                   EnsureMaterial(ref _spatialMaterial, SpatialShaderName);
        }

        private static bool EnsureMaterial(ref Material material, string shaderName)
        {
            if (material != null)
            {
                return true;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return false;
            }

            material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.B10G11R11_UFloatPack32
                : _outputFormat;
        }

        private static RenderTextureDescriptor CreateTextureDescriptor(int width, int height, GraphicsFormat graphicsFormat)
        {
            return new RenderTextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex2D,
                depthBufferBits = 0,
                msaaSamples = 1,
                graphicsFormat = graphicsFormat,
                enableRandomWrite = false,
                useMipMap = false,
                autoGenerateMips = false
            };
        }

        private static RenderTexture CreateHistoryTexture(
            string textureName,
            int width,
            int height,
            GraphicsFormat graphicsFormat,
            FilterMode filterMode)
        {
            RenderTexture texture = new(CreateTextureDescriptor(width, height, graphicsFormat))
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            return texture;
        }

        private static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera camera, int width, int height)
        {
            Vector4 screenSize = new(width, height, 1.0f / width, 1.0f / height);
            Matrix4x4 viewSpaceRasterTransform;

            if (camera.orthographic)
            {
                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(-2.0f * screenSize.z, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, -2.0f * screenSize.w, 0.0f, 0.0f),
                    new Vector4(1.0f, 1.0f, -1.0f, 0.0f),
                    Vector4.zero);
            }
            else
            {
                float aspectRatio = width / (float)height;
                float tanHalfVerticalFov = Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad);
                Vector2 lensShift = camera.usePhysicalProperties ? camera.lensShift : Vector2.zero;

                float m21 = (1.0f - 2.0f * lensShift.y) * tanHalfVerticalFov;
                float m11 = -2.0f * screenSize.w * tanHalfVerticalFov;
                float m20 = (1.0f - 2.0f * lensShift.x) * tanHalfVerticalFov * aspectRatio;
                float m00 = -2.0f * screenSize.z * tanHalfVerticalFov * aspectRatio;

                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(m00, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, m11, 0.0f, 0.0f),
                    new Vector4(m20, m21, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            }

            Matrix4x4 worldToViewMatrix = camera.worldToCameraMatrix;
            worldToViewMatrix.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            worldToViewMatrix.SetRow(2, -worldToViewMatrix.GetRow(2));
            return Matrix4x4.Transpose(worldToViewMatrix.transpose * viewSpaceRasterTransform);
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            material = null;
        }

        private static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            if (texture.IsCreated())
            {
                texture.Release();
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            texture = null;
        }
    }
}
