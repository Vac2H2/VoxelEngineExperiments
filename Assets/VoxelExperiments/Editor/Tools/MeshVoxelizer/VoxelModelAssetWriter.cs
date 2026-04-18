using System;
using UnityEditor;
using UnityEngine;
using VoxelExperiments.Runtime.Data;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VoxelExperiments.Editor.Tools.MeshVoxelizer
{
    internal static class VoxelModelAssetWriter
    {
        public static VoxelModel WriteAsset(VoxelModel targetModel, string assetPath, MeshVoxelizationResult result)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("Asset path is required.", nameof(assetPath));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            VoxelModel asset = targetModel;
            Stopwatch stopwatch = Stopwatch.StartNew();
            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset internal begin | path={assetPath}");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t=0 ms | WriteAsset internal begin | path={assetPath}");
            if (asset == null)
            {
                MeshVoxelizerGpu.AppendTraceLine("WriteAsset create asset begin");
                MeshVoxelizerGpu.EmitDebugLog("MeshVoxelizer write | CreateAsset begin");
                asset = ScriptableObject.CreateInstance<VoxelModel>();
                AssetDatabase.CreateAsset(asset, assetPath);
                MeshVoxelizerGpu.AppendTraceLine($"WriteAsset create asset end | t={stopwatch.ElapsedMilliseconds} ms");
                MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | CreateAsset end");
            }

            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset overwrite data begin | chunks={result.ChunkCount}");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | OverwriteData begin | chunks={result.ChunkCount}");
            asset.OverwriteData(
                result.MemoryLayout,
                result.ChunkCount,
                result.OccupancyBytes,
                result.VoxelBytes,
                result.ChunkAabbs);
            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset overwrite data end | t={stopwatch.ElapsedMilliseconds} ms");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | OverwriteData end");

            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset SetDirty begin | t={stopwatch.ElapsedMilliseconds} ms");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | SetDirty begin");
            EditorUtility.SetDirty(asset);
            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset SetDirty end | t={stopwatch.ElapsedMilliseconds} ms");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | SetDirty end");

            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset SaveAssetIfDirty begin | t={stopwatch.ElapsedMilliseconds} ms");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | SaveAssetIfDirty begin");
            AssetDatabase.SaveAssetIfDirty(asset);
            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset SaveAssetIfDirty end | t={stopwatch.ElapsedMilliseconds} ms");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | SaveAssetIfDirty end");

            MeshVoxelizerGpu.AppendTraceLine($"WriteAsset internal end | t={stopwatch.ElapsedMilliseconds} ms");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer write | t={stopwatch.ElapsedMilliseconds} ms | WriteAsset internal end");
            return asset;
        }
    }
}
