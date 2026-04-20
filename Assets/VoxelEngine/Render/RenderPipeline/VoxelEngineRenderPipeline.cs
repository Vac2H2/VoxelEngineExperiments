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

        private static readonly int GbufferPreviewModeId = Shader.PropertyToID("_VoxelEngineGbufferPreviewMode");

        private readonly VoxelEngineRenderPipelineAsset _asset;
        private readonly VoxelEngineRenderBackend _renderBackend;
        private readonly GbufferCore _gbufferCore;
        private readonly RtaoCore _rtaoCore;
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

                if (_gbufferCore != null && _gbufferCore.Record(commandBuffer, camera, _renderBackend))
                {
                    bool recordedRtao = _rtaoCore != null && _rtaoCore.Record(commandBuffer, camera, _renderBackend);
                    RecordPreview(commandBuffer);
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
                VoxelGbufferPreviewTarget.Rtao => VoxelRtaoIds.OutputTarget,
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
