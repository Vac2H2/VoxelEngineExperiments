using System;
using UnityEngine;
using VoxelRT.Runtime.Rendering.RenderPipeline;

namespace VoxelRT.Runtime.Rendering.RenderModules
{
    public abstract class VoxelRenderPipelineModule : ScriptableObject
    {
        [SerializeField] private bool _isEnabled = true;
        [SerializeField] private VoxelRenderPipelineModule[] _submodules = Array.Empty<VoxelRenderPipelineModule>();

        [NonSerialized] private bool _isCreated;

        public bool IsEnabled => _isEnabled;

        internal VoxelRenderPipelineModule[] Submodules => _submodules ?? Array.Empty<VoxelRenderPipelineModule>();

        internal void Create(VoxelRenderPipeline pipeline)
        {
            if (_isCreated)
            {
                return;
            }

            _isCreated = true;

            try
            {
                OnPipelineCreated(pipeline);
                VoxelRenderPipelineModule[] submodules = Submodules;
                for (int i = 0; i < submodules.Length; i++)
                {
                    VoxelRenderPipelineModule submodule = submodules[i];
                    if (submodule == null)
                    {
                        continue;
                    }

                    submodule.Create(pipeline);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Dispose()
        {
            if (!_isCreated)
            {
                return;
            }

            try
            {
                VoxelRenderPipelineModule[] submodules = Submodules;
                for (int i = submodules.Length - 1; i >= 0; i--)
                {
                    VoxelRenderPipelineModule submodule = submodules[i];
                    if (submodule == null)
                    {
                        continue;
                    }

                    submodule.Dispose();
                }

                OnPipelineDisposed();
            }
            finally
            {
                _isCreated = false;
            }
        }

        internal void BeginFrame(in VoxelRenderPipelineFrameContext context)
        {
            if (!ShouldExecute())
            {
                return;
            }

            OnFrameBegin(in context);

            VoxelRenderPipelineModule[] submodules = Submodules;
            for (int i = 0; i < submodules.Length; i++)
            {
                VoxelRenderPipelineModule submodule = submodules[i];
                if (submodule == null)
                {
                    continue;
                }

                submodule.BeginFrame(in context);
            }
        }

        internal void EndFrame(in VoxelRenderPipelineFrameContext context)
        {
            if (!ShouldExecute())
            {
                return;
            }

            VoxelRenderPipelineModule[] submodules = Submodules;
            for (int i = submodules.Length - 1; i >= 0; i--)
            {
                VoxelRenderPipelineModule submodule = submodules[i];
                if (submodule == null)
                {
                    continue;
                }

                submodule.EndFrame(in context);
            }

            OnFrameEnd(in context);
        }

        internal void BeginCamera(in VoxelRenderPipelineCameraContext context)
        {
            if (!ShouldExecute())
            {
                return;
            }

            OnCameraBegin(in context);

            VoxelRenderPipelineModule[] submodules = Submodules;
            for (int i = 0; i < submodules.Length; i++)
            {
                VoxelRenderPipelineModule submodule = submodules[i];
                if (submodule == null)
                {
                    continue;
                }

                submodule.BeginCamera(in context);
            }
        }

        internal void EndCamera(in VoxelRenderPipelineCameraContext context)
        {
            if (!ShouldExecute())
            {
                return;
            }

            VoxelRenderPipelineModule[] submodules = Submodules;
            for (int i = submodules.Length - 1; i >= 0; i--)
            {
                VoxelRenderPipelineModule submodule = submodules[i];
                if (submodule == null)
                {
                    continue;
                }

                submodule.EndCamera(in context);
            }

            OnCameraEnd(in context);
        }

        internal bool Render(in VoxelRenderPipelineCameraContext context)
        {
            if (!ShouldExecute())
            {
                return false;
            }

            return OnRender(in context);
        }

        protected bool RenderSubmodules(in VoxelRenderPipelineCameraContext context)
        {
            VoxelRenderPipelineModule[] submodules = Submodules;
            for (int i = 0; i < submodules.Length; i++)
            {
                VoxelRenderPipelineModule submodule = submodules[i];
                if (submodule == null)
                {
                    continue;
                }

                if (submodule.Render(in context))
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual void OnPipelineCreated(VoxelRenderPipeline pipeline)
        {
        }

        protected virtual void OnPipelineDisposed()
        {
        }

        protected virtual void OnFrameBegin(in VoxelRenderPipelineFrameContext context)
        {
        }

        protected virtual void OnFrameEnd(in VoxelRenderPipelineFrameContext context)
        {
        }

        protected virtual void OnCameraBegin(in VoxelRenderPipelineCameraContext context)
        {
        }

        protected virtual void OnCameraEnd(in VoxelRenderPipelineCameraContext context)
        {
        }

        protected virtual bool OnRender(in VoxelRenderPipelineCameraContext context)
        {
            return RenderSubmodules(in context);
        }

        private bool ShouldExecute()
        {
            return _isCreated && _isEnabled;
        }
    }
}
