using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace VoxelEngine.Data.Voxel
{
    [Serializable]
    public sealed class AssetReferenceVoxelPalette : AssetReferenceT<VoxelPaletteAsset>
    {
        public AssetReferenceVoxelPalette(string guid)
            : base(guid)
        {
        }
    }
}
