using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Render.Cores;
using VoxelEngine.Render.Debugging;
using VoxelEngine.Render.NRD.Cores;
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

        private static readonly int GbufferPreviewModeId = Shader.PropertyToID("_VoxelEngineGbufferPreviewMode");

        private readonly VoxelEngineRenderPipelineAsset _asset;
        private readonly VoxelEngineRenderBackend _renderBackend;
        private readonly GbufferCore _gbufferCore;
        private readonly RtaoCore _rtaoCore;
        private readonly bool _enableRtaoDenoiseInSrp;
        private readonly RtaoDenoiseCore _rtaoDenoiseCore;
        private Material _gbufferPreviewMaterial;
        private bool _isDisposed;

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
            _enableRtaoDenoiseInSrp = asset.EnableRtaoDenoiseInSrp;
            _rtaoDenoiseCore = asset.RtaoDenoiseCore;
        }

        public VoxelEngineRenderPipeline(VoxelEngineRenderBackend renderBackend, GbufferCore gbufferCore)
        {
            _renderBackend = renderBackend ?? throw new ArgumentNullException(nameof(renderBackend));
            _gbufferCore = gbufferCore ?? new GbufferCore();
            _rtaoCore = null;
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
                _rtaoDenoiseCore?.Dispose();
                _renderBackend.Dispose();
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
            CommandBuffer denoiseCommandBuffer = null;

            try
            {
                commandBuffer.SetGlobalFloat(VoxelNrdIds.PreviewAvailableId, 0.0f);

                GetClearFlags(camera, out bool clearDepth, out bool clearColor);
                if (clearDepth || clearColor)
                {
                    commandBuffer.ClearRenderTarget(
                        clearDepth,
                        clearColor,
                        clearColor ? camera.backgroundColor.linear : Color.clear);
                }

                if (_gbufferCore != null && _gbufferCore.Record(commandBuffer, camera, _renderBackend))
                {
                    bool recordedRtao = _rtaoCore != null && _rtaoCore.Record(commandBuffer, camera, _renderBackend, _gbufferCore);
                    bool wantsDenoisedAoPreview = VoxelGbufferDebugView.PreviewTarget == VoxelGbufferPreviewTarget.DenoisedAo;
                    bool needsDeferredDenoise = recordedRtao &&
                                               _rtaoDenoiseCore != null &&
                                               (_enableRtaoDenoiseInSrp || wantsDenoisedAoPreview);
                    bool boundDenoisedAoPreview = recordedRtao &&
                                                  wantsDenoisedAoPreview &&
                                                  _rtaoDenoiseCore != null &&
                                                  _rtaoDenoiseCore.BindLatestDenoisedAoPreview(commandBuffer, _rtaoCore);
                    bool recordedNormHitDistPreview = recordedRtao &&
                                                      _rtaoDenoiseCore != null &&
                                                      VoxelGbufferDebugView.PreviewTarget == VoxelGbufferPreviewTarget.NormHitDist &&
                                                      _rtaoDenoiseCore.RecordNormHitDistancePreview(commandBuffer, _gbufferCore, _rtaoCore);
                    commandBuffer.SetGlobalFloat(VoxelNrdIds.PreviewAvailableId, (boundDenoisedAoPreview || recordedNormHitDistPreview) ? 1.0f : 0.0f);

                    if (recordedRtao && !boundDenoisedAoPreview && _rtaoCore.OutputTexture != null)
                    {
                        commandBuffer.SetGlobalTexture(VoxelRtaoIds.OutputTextureId, _rtaoCore.OutputTexture);
                    }

                    RecordPreview(commandBuffer);
                    if (recordedRtao)
                    {
                        _rtaoCore.ReleaseTemporaryTargets(commandBuffer);
                    }

                    _gbufferCore.ReleaseTemporaryTargets(commandBuffer);

                    if (needsDeferredDenoise)
                    {
                        denoiseCommandBuffer = new CommandBuffer
                        {
                            name = $"{nameof(VoxelEngineRenderPipeline)}.{nameof(RtaoDenoiseCore)}"
                        };
                    }
                }

                context.ExecuteCommandBuffer(commandBuffer);

#if UNITY_EDITOR
                if (Handles.ShouldRenderGizmos())
                {
                    context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
                    context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
                }
#endif

                if (denoiseCommandBuffer != null &&
                    _rtaoDenoiseCore != null &&
                    _rtaoDenoiseCore.QueueDeferredDenoise(denoiseCommandBuffer, camera, _gbufferCore, _rtaoCore))
                {
                    context.ExecuteCommandBuffer(denoiseCommandBuffer);
                }
            }
            finally
            {
                denoiseCommandBuffer?.Clear();
                denoiseCommandBuffer?.Release();
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

        private void RecordPreview(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (EnsurePreviewMaterial())
            {
                commandBuffer.SetGlobalFloat(GbufferPreviewModeId, (float)VoxelGbufferDebugView.PreviewTarget);
                commandBuffer.Blit(VoxelGbufferIds.AlbedoTextureId, BuiltinRenderTextureType.CameraTarget, _gbufferPreviewMaterial);
                return;
            }

            commandBuffer.Blit(ResolvePreviewSource(), BuiltinRenderTextureType.CameraTarget);
        }

        private bool EnsurePreviewMaterial()
        {
            if (_gbufferPreviewMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find(GbufferPreviewShaderName);
            if (shader == null)
            {
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
                VoxelGbufferPreviewTarget.NormHitDist => VoxelNrdIds.PackedDiffuseHitDistanceTarget,
                VoxelGbufferPreviewTarget.DenoisedAo => VoxelRtaoIds.OutputTarget,
                _ => VoxelGbufferIds.AlbedoTarget,
            };
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
