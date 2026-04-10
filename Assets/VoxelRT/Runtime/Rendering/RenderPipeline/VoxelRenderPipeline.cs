using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RenderPipeline
{
    public sealed class VoxelRenderPipeline : UnityEngine.Rendering.RenderPipeline
    {
        private readonly VoxelRenderPipelineAsset _asset;
        private readonly VoxelRenderPipelineModule[] _modules;
        private bool _isDisposed;

        internal VoxelRenderPipeline(VoxelRenderPipelineAsset asset)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _modules = asset.Modules;
            ValidateModuleGraph(_modules);

            try
            {
                InitializeModules();
            }
            catch
            {
                DisposeModules();
                _isDisposed = true;
                throw;
            }
        }

        public VoxelRenderPipelineAsset Asset => _asset;

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelRenderPipeline));
            }

            if (cameras == null)
            {
                throw new ArgumentNullException(nameof(cameras));
            }

            BeginContextRendering(context, cameras);
            VoxelRenderPipelineFrameContext frameContext = new(this, _asset, context, cameras);
            BeginFrame(in frameContext);

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
                    VoxelRenderPipelineCameraContext cameraContext = new(in frameContext, camera, i);
                    BeginCamera(in cameraContext);

                    try
                    {
                        if (!RenderWithModules(in cameraContext))
                        {
                            RenderFallback(in cameraContext);
                        }
                    }
                    finally
                    {
                        EndCamera(in cameraContext);
                        EndCameraRendering(context, camera);
                    }
                }
            }
            finally
            {
                EndFrame(in frameContext);
                EndContextRendering(context, cameras);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                DisposeModules();
            }

            _isDisposed = true;
            base.Dispose(disposing);
        }

        private void InitializeModules()
        {
            for (int i = 0; i < _modules.Length; i++)
            {
                VoxelRenderPipelineModule module = _modules[i];
                if (module == null)
                {
                    continue;
                }

                module.Create(this);
            }
        }

        private void DisposeModules()
        {
            for (int i = _modules.Length - 1; i >= 0; i--)
            {
                VoxelRenderPipelineModule module = _modules[i];
                if (module == null)
                {
                    continue;
                }

                module.Dispose();
            }
        }

        private void BeginFrame(in VoxelRenderPipelineFrameContext context)
        {
            for (int i = 0; i < _modules.Length; i++)
            {
                VoxelRenderPipelineModule module = _modules[i];
                if (module == null)
                {
                    continue;
                }

                module.BeginFrame(in context);
            }
        }

        private void EndFrame(in VoxelRenderPipelineFrameContext context)
        {
            for (int i = _modules.Length - 1; i >= 0; i--)
            {
                VoxelRenderPipelineModule module = _modules[i];
                if (module == null)
                {
                    continue;
                }

                module.EndFrame(in context);
            }
        }

        private void BeginCamera(in VoxelRenderPipelineCameraContext context)
        {
            for (int i = 0; i < _modules.Length; i++)
            {
                VoxelRenderPipelineModule module = _modules[i];
                if (module == null)
                {
                    continue;
                }

                module.BeginCamera(in context);
            }
        }

        private void EndCamera(in VoxelRenderPipelineCameraContext context)
        {
            for (int i = _modules.Length - 1; i >= 0; i--)
            {
                VoxelRenderPipelineModule module = _modules[i];
                if (module == null)
                {
                    continue;
                }

                module.EndCamera(in context);
            }
        }

        private bool RenderWithModules(in VoxelRenderPipelineCameraContext context)
        {
            for (int i = 0; i < _modules.Length; i++)
            {
                VoxelRenderPipelineModule module = _modules[i];
                if (module == null)
                {
                    continue;
                }

                if (module.Render(in context))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RenderFallback(in VoxelRenderPipelineCameraContext context)
        {
            ScriptableRenderContext renderContext = context.RenderContext;
            Camera camera = context.Camera;
            renderContext.SetupCameraProperties(camera);

            CommandBuffer commandBuffer = new()
            {
                name = nameof(VoxelRenderPipeline)
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

                renderContext.ExecuteCommandBuffer(commandBuffer);
            }
            finally
            {
                commandBuffer.Clear();
                commandBuffer.Release();
            }

            renderContext.Submit();
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

        private static void ValidateModuleGraph(VoxelRenderPipelineModule[] modules)
        {
            HashSet<VoxelRenderPipelineModule> visited = new();
            HashSet<VoxelRenderPipelineModule> recursionStack = new();

            for (int i = 0; i < modules.Length; i++)
            {
                ValidateModule(modules[i], visited, recursionStack);
            }
        }

        private static void ValidateModule(
            VoxelRenderPipelineModule module,
            HashSet<VoxelRenderPipelineModule> visited,
            HashSet<VoxelRenderPipelineModule> recursionStack)
        {
            if (module == null)
            {
                return;
            }

            if (!recursionStack.Add(module))
            {
                throw new InvalidOperationException(
                    $"Detected a circular render-pipeline module reference at '{module.name}'.");
            }

            if (!visited.Add(module))
            {
                recursionStack.Remove(module);
                throw new InvalidOperationException(
                    $"Render-pipeline module '{module.name}' is referenced more than once. Each module asset must have a single owner.");
            }

            VoxelRenderPipelineModule[] submodules = module.Submodules;
            for (int i = 0; i < submodules.Length; i++)
            {
                ValidateModule(submodules[i], visited, recursionStack);
            }

            recursionStack.Remove(module);
        }
    }
}
