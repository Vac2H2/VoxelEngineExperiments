using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Render.Cores;
using VoxelEngine.Render.Debugging;
using VoxelEngine.Render.RenderBackend;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxelEngine.Render.RenderPipeline
{
    internal static class VoxelEngineFrameRateBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DisableFrameRateLimits()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
    }

    public sealed class VoxelEngineRenderPipeline : UnityEngine.Rendering.RenderPipeline
    {
        private const string GbufferPreviewShaderName = "Hidden/VoxelEngine/Rendering/GbufferPreview";

        private static readonly int PreviewColorTextureId = Shader.PropertyToID("_VoxelEnginePreviewColor");
        private static readonly int GbufferPreviewModeId = Shader.PropertyToID("_VoxelEngineGbufferPreviewMode");
        private static readonly int PreviewPixelCoordToViewDirWsId = Shader.PropertyToID("_VoxelEnginePreviewPixelCoordToViewDirWS");
        private static readonly int PreviewScreenSizeId = Shader.PropertyToID("_VoxelEnginePreviewScreenSize");
        private static readonly int PreviewBackgroundColorId = Shader.PropertyToID("_VoxelEnginePreviewBackgroundColor");

        private readonly VoxelEngineRenderPipelineAsset _asset;
        private readonly VoxelEngineRenderBackend _renderBackend;
        private readonly GbufferCore _gbufferCore;
        private readonly RtaoCore _rtaoCore;
        private readonly SunLightCore _sunLightCore;
        private readonly ReflectionCore _reflectionCore;
        private readonly RtaoFrameAverageCore _rtaoFrameAverageCore;
        private readonly TaaCore _taaCore;
        private Material _gbufferPreviewMaterial;
        private bool _isDisposed;

        public static VoxelEngineRenderPipeline ActivePipeline { get; private set; }

        public VoxelEngineRenderPipeline(VoxelEngineRenderPipelineAsset asset)
            : this(asset, new VoxelEngineRenderBackend(asset != null ? asset.RayTracingMaterial : null))
        {
        }

        public VoxelEngineRenderPipeline(Material rayTracingMaterial)
            : this(new VoxelEngineRenderBackend(rayTracingMaterial), new GbufferCore())
        {
        }

        public VoxelEngineRenderPipeline(VoxelEngineRenderPipelineAsset asset, VoxelEngineRenderBackend renderBackend)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _renderBackend = renderBackend ?? throw new ArgumentNullException(nameof(renderBackend));
            _gbufferCore = asset.GbufferCore;
            _rtaoCore = asset.RtaoCore;
            _sunLightCore = asset.SunLightCore;
            _reflectionCore = asset.ReflectionCore;
            _rtaoFrameAverageCore = asset.RtaoFrameAverageCore;
            _taaCore = asset.TaaCore;
        }

        public VoxelEngineRenderPipeline(VoxelEngineRenderBackend renderBackend, GbufferCore gbufferCore)
        {
            _renderBackend = renderBackend ?? throw new ArgumentNullException(nameof(renderBackend));
            _gbufferCore = gbufferCore ?? new GbufferCore();
            _rtaoCore = null;
            _sunLightCore = null;
            _reflectionCore = null;
            _rtaoFrameAverageCore = null;
            _taaCore = null;
        }

        public VoxelEngineRenderPipelineAsset Asset
        {
            get
            {
                EnsureNotDisposed();
                return _asset;
            }
        }

        public VoxelEngineRenderBackend RenderBackend
        {
            get
            {
                EnsureNotDisposed();
                return _renderBackend;
            }
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            EnsureNotDisposed();
            ActivePipeline = this;

            if (cameras == null)
            {
                throw new ArgumentNullException(nameof(cameras));
            }

            BeginContextRendering(context, cameras);

            try
            {
                for (int i = 0; i < cameras.Count; i++)
                {
                    Camera camera = cameras[i];
                    if (camera == null)
                    {
                        continue;
                    }

                    BeginCameraRendering(context, camera);

                    try
                    {
                        RenderCamera(context, camera);
                    }
                    finally
                    {
                        EndCameraRendering(context, camera);
                    }
                }
            }
            finally
            {
                EndContextRendering(context, cameras);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                DestroyMaterial(ref _gbufferPreviewMaterial);
                _gbufferCore?.Dispose();
                _rtaoCore?.Dispose();
                _sunLightCore?.Dispose();
                _reflectionCore?.Dispose();
                _rtaoFrameAverageCore?.Dispose();
                _taaCore?.Dispose();
                _renderBackend.Dispose();
            }

            if (ReferenceEquals(ActivePipeline, this))
            {
                ActivePipeline = null;
            }

            _isDisposed = true;
            base.Dispose(disposing);
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelEngineRenderPipeline));
            }
        }

        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
            {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            }
#endif

            context.SetupCameraProperties(camera);

            CommandBuffer commandBuffer = new CommandBuffer
            {
                name = nameof(VoxelEngineRenderPipeline)
            };

            try
            {
                GetClearFlags(camera, out bool clearDepth, out bool clearColor);
                if (clearDepth || clearColor)
                {
                    commandBuffer.ClearRenderTarget(
                        clearDepth,
                        clearColor,
                        clearColor ? camera.backgroundColor.linear : Color.clear);
                }

                bool applyTaa = ShouldApplyTaa(camera);
                Vector2 projectionJitter = applyTaa && _taaCore != null
                    ? _taaCore.GetProjectionJitter(camera)
                    : Vector2.zero;
                if (!applyTaa)
                {
                    _taaCore?.ResetHistory(camera);
                }

                if (_gbufferCore != null && _gbufferCore.Record(commandBuffer, camera, _renderBackend, projectionJitter))
                {
                    bool recordedSunLight = _sunLightCore != null &&
                                            _sunLightCore.Record(commandBuffer, camera, _renderBackend, _gbufferCore, projectionJitter);
                    if (!recordedSunLight)
                    {
                        commandBuffer.SetGlobalTexture(VoxelSunLightIds.SunLightTextureId, Texture2D.blackTexture);
                    }

                    bool recordedReflection = _reflectionCore != null &&
                                              _reflectionCore.Record(
                                                  commandBuffer,
                                                  camera,
                                                   _renderBackend,
                                                   _gbufferCore,
                                                   SkyTexture,
                                                   SkyExposure,
                                                   SkyRotation,
                                                   projectionJitter);
                    if (!recordedReflection)
                    {
                        commandBuffer.SetGlobalTexture(VoxelReflectionIds.RawReflectionTextureId, Texture2D.blackTexture);
                        commandBuffer.SetGlobalTexture(VoxelReflectionIds.ReflectionTextureId, Texture2D.blackTexture);
                    }

                    bool recordedRtao = _rtaoCore != null &&
                                        _rtaoCore.Record(commandBuffer, camera, _renderBackend, _gbufferCore, projectionJitter);
                    bool recordedAoOutput = recordedRtao &&
                                            _rtaoFrameAverageCore != null &&
                                            ShouldRecordAoOutput(camera) &&
                                            _rtaoFrameAverageCore.Record(commandBuffer, camera, _rtaoCore, _gbufferCore);
                    RenderTexture rtaoOutputTexture = recordedAoOutput
                        ? _rtaoFrameAverageCore.OutputTexture
                        : _rtaoCore?.OutputTexture;
                    if (recordedRtao && rtaoOutputTexture != null)
                    {
                        commandBuffer.SetGlobalTexture(VoxelRtaoIds.OutputTextureId, rtaoOutputTexture);
                    }

                    RecordPreview(commandBuffer, camera, applyTaa);
                    if (recordedRtao)
                    {
                        _rtaoCore.ReleaseTemporaryTargets(commandBuffer);
                    }

                    _gbufferCore.ReleaseTemporaryTargets(commandBuffer);
                }

                context.ExecuteCommandBuffer(commandBuffer);

#if UNITY_EDITOR
                if (Handles.ShouldRenderGizmos())
                {
                    context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
                    context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
                }
#endif
                if (ShouldDrawUiOverlay(camera))
                {
                    context.DrawUIOverlay(camera);
                }

            }
            finally
            {
                commandBuffer.Clear();
                commandBuffer.Release();
            }

            context.Submit();
        }

        private static void GetClearFlags(Camera camera, out bool clearDepth, out bool clearColor)
        {
            switch (camera.clearFlags)
            {
                case CameraClearFlags.Color:
                case CameraClearFlags.Skybox:
                    clearDepth = true;
                    clearColor = true;
                    break;
                case CameraClearFlags.Depth:
                    clearDepth = true;
                    clearColor = false;
                    break;
                default:
                    clearDepth = false;
                    clearColor = false;
                    break;
            }
        }

        private bool ShouldApplyTaa(Camera camera)
        {
            return _taaCore != null &&
                   _taaCore.Enabled &&
                   camera != null &&
                   camera.cameraType == CameraType.Game &&
                   VoxelGbufferDebugView.PreviewTarget == VoxelGbufferPreviewTarget.LitColor;
        }

        private static bool ShouldRecordAoOutput(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }

            if (camera.cameraType == CameraType.Game)
            {
                return true;
            }

#if UNITY_EDITOR
            return camera.cameraType == CameraType.SceneView;
#else
            return false;
#endif
        }

        private static bool ShouldDrawUiOverlay(Camera camera)
        {
            return camera != null && camera.cameraType == CameraType.Game;
        }

        private void RecordPreview(CommandBuffer commandBuffer, Camera camera, bool allowTaa)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            int width = Mathf.Max(camera.pixelWidth, 1);
            int height = Mathf.Max(camera.pixelHeight, 1);
            if (allowTaa && _taaCore != null)
            {
                commandBuffer.GetTemporaryRT(
                    PreviewColorTextureId,
                    CreatePreviewDescriptor(width, height),
                    FilterMode.Point);
                RenderTargetIdentifier previewTarget = new RenderTargetIdentifier(PreviewColorTextureId);
                RecordPreviewToTarget(commandBuffer, camera, previewTarget, width, height);
                if (_taaCore.Record(commandBuffer, camera, previewTarget, _gbufferCore, width, height) &&
                    _taaCore.OutputTexture != null)
                {
                    commandBuffer.Blit(_taaCore.OutputTexture, BuiltinRenderTextureType.CameraTarget);
                }
                else
                {
                    commandBuffer.Blit(previewTarget, BuiltinRenderTextureType.CameraTarget);
                }

                commandBuffer.ReleaseTemporaryRT(PreviewColorTextureId);
                return;
            }

            RecordPreviewToTarget(
                commandBuffer,
                camera,
                new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget),
                width,
                height);
        }

        private void RecordPreviewToTarget(
            CommandBuffer commandBuffer,
            Camera camera,
            RenderTargetIdentifier target,
            int width,
            int height)
        {
            if (EnsurePreviewMaterial())
            {
                Color backgroundColor = camera.backgroundColor.linear;
                commandBuffer.SetGlobalFloat(GbufferPreviewModeId, (float)VoxelGbufferDebugView.PreviewTarget);
                BindSkyGlobals(commandBuffer);
                commandBuffer.SetGlobalMatrix(
                    PreviewPixelCoordToViewDirWsId,
                    ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, width, height));
                commandBuffer.SetGlobalVector(
                    PreviewScreenSizeId,
                    new Vector4(width, height, 1.0f / width, 1.0f / height));
                commandBuffer.SetGlobalVector(
                    PreviewBackgroundColorId,
                    new Vector4(
                        backgroundColor.r,
                        backgroundColor.g,
                        backgroundColor.b,
                        backgroundColor.a));
                commandBuffer.Blit(VoxelGbufferIds.AlbedoTextureId, target, _gbufferPreviewMaterial);
                return;
            }

            commandBuffer.Blit(ResolvePreviewSource(), target);
        }

        private bool EnsurePreviewMaterial()
        {
            if (_gbufferPreviewMaterial != null)
            {
                return true;
            }

            Shader shader = _asset != null && _asset.GbufferPreviewShader != null
                ? _asset.GbufferPreviewShader
                : Shader.Find(GbufferPreviewShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"Missing '{GbufferPreviewShaderName}'. Assign it on {nameof(VoxelEngineRenderPipelineAsset)} so Player builds include the preview shader.");
                return false;
            }

            _gbufferPreviewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private static RenderTargetIdentifier ResolvePreviewSource()
        {
            return VoxelGbufferDebugView.PreviewTarget switch
            {
                VoxelGbufferPreviewTarget.Normal => VoxelGbufferIds.NormalTarget,
                VoxelGbufferPreviewTarget.Depth => VoxelGbufferIds.DepthTarget,
                VoxelGbufferPreviewTarget.Motion => VoxelGbufferIds.MotionTarget,
                VoxelGbufferPreviewTarget.HitDist => VoxelRtaoIds.HitDistanceTarget,
                VoxelGbufferPreviewTarget.RawAo => VoxelRtaoIds.HitDistanceTarget,
                VoxelGbufferPreviewTarget.RawLight => VoxelRtaoIds.RawLightTarget,
                VoxelGbufferPreviewTarget.DenoisedLight => VoxelRtaoIds.DenoisedLightTarget,
                VoxelGbufferPreviewTarget.RawReflection => VoxelReflectionIds.RawReflectionTarget,
                VoxelGbufferPreviewTarget.Reflection => VoxelReflectionIds.ReflectionTarget,
                _ => VoxelGbufferIds.AlbedoTarget,
            };
        }

        private static RenderTextureDescriptor CreatePreviewDescriptor(int width, int height)
        {
            return new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 0)
            {
                dimension = TextureDimension.Tex2D,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
        }

        private Texture2D SkyTexture => _asset != null ? _asset.SkyTexture : null;

        private float SkyExposure => _asset != null ? _asset.SkyExposure : 1.0f;

        private float SkyRotation => _asset != null ? _asset.SkyRotation : 0.0f;

        private void BindSkyGlobals(CommandBuffer commandBuffer)
        {
            Texture2D skyTexture = SkyTexture;
            commandBuffer.SetGlobalTexture(VoxelSkyIds.SkyTextureId, skyTexture != null ? skyTexture : Texture2D.blackTexture);
            commandBuffer.SetGlobalFloat(VoxelSkyIds.SkyEnabledId, skyTexture != null ? 1.0f : 0.0f);
            commandBuffer.SetGlobalFloat(VoxelSkyIds.SkyExposureId, Mathf.Max(SkyExposure, 0.0f));
            commandBuffer.SetGlobalFloat(VoxelSkyIds.SkyRotationId, SkyRotation * Mathf.Deg2Rad);
        }

        private static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera camera, int width, int height)
        {
            Vector4 screenSize = new Vector4(width, height, 1.0f / width, 1.0f / height);
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
    }
}
