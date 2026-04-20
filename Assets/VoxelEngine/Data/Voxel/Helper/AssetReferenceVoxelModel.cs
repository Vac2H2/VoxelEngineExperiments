using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace VoxelEngine.Data.Voxel
{
    [Serializable]
    public sealed class AssetReferenceVoxelModel : AssetReferenceT<VoxelModelAsset>
    {
        public AssetReferenceVoxelModel(string guid)
            : base(guid)
        {
        }
    }
}
