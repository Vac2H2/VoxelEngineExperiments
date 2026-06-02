using System;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Render.RenderPipeline;

namespace VoxelEngine
{
    public static class VoxelEngineSettings
    {
        public const float DefaultVoxelSize = 1.0f;
        public const float MinimumVoxelSize = 0.000001f;

        public static float GlobalVoxelSize
        {
            get
            {
                if (TryGetActiveRenderPipelineAsset(out VoxelEngineRenderPipelineAsset asset))
                {
                    return SanitizeVoxelSize(asset.GlobalVoxelSize);
                }

                return DefaultVoxelSize;
            }
        }

        public static float GlobalChunkSize => Data.Voxel.VoxelVolume.ChunkDimension * GlobalVoxelSize;

        public static float SanitizeVoxelSize(float voxelSize)
        {
            return voxelSize > 0.0f && float.IsFinite(voxelSize)
                ? voxelSize
                : DefaultVoxelSize;
        }

        public static bool TryGetActiveRenderPipelineAsset(out VoxelEngineRenderPipelineAsset asset)
        {
            asset = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null)
                {
                    asset = renderPipeline.Asset;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset graphicsAsset)
            {
                asset = graphicsAsset;
                return true;
            }

            if (TryGetGraphicsSettingsRenderPipelineAsset("defaultRenderPipeline", out asset) ||
                TryGetGraphicsSettingsRenderPipelineAsset("renderPipelineAsset", out asset))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetGraphicsSettingsRenderPipelineAsset(
            string propertyName,
            out VoxelEngineRenderPipelineAsset asset)
        {
            asset = null;
            object value = typeof(GraphicsSettings).GetProperty(propertyName)?.GetValue(null);
            if (value is VoxelEngineRenderPipelineAsset voxelEngineAsset)
            {
                asset = voxelEngineAsset;
                return true;
            }

            return false;
        }
    }
}
