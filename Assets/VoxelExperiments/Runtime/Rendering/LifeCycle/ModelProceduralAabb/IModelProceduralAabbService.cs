using System;
using Unity.Collections;

namespace VoxelExperiments.Runtime.Rendering.ModelProceduralAabb
{
    public interface IModelProceduralAabbService : IDisposable
    {
        uint AabbStrideBytes { get; }

        int Retain(
            object modelKey,
            NativeArray<ModelChunkAabb> chunkAabbs);

        void Release(int residencyId);

        ModelProceduralAabbDescriptor GetDescriptor(int residencyId);
    }
}
