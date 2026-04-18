using System;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.ModelProceduralAabb
{
    public readonly struct ModelProceduralAabbDescriptor
    {
        public ModelProceduralAabbDescriptor(
            int residencyId,
            GraphicsBuffer aabbBuffer,
            int aabbCount)
        {
            if (aabbCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(aabbCount), "AABB count must be greater than zero.");
            }

            ResidencyId = residencyId;
            AabbBuffer = aabbBuffer ?? throw new ArgumentNullException(nameof(aabbBuffer));
            AabbCount = aabbCount;
        }

        public int ResidencyId { get; }

        public GraphicsBuffer AabbBuffer { get; }

        public int AabbCount { get; }
    }
}
