using System;

namespace VoxelEngine.Render.RenderBackend
{
    public readonly struct VoxelEngineRenderInstanceHandle : IEquatable<VoxelEngineRenderInstanceHandle>
    {
        public const int InvalidValue = 0;

        internal VoxelEngineRenderInstanceHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != InvalidValue;

        public bool Equals(VoxelEngineRenderInstanceHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelEngineRenderInstanceHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(VoxelEngineRenderInstanceHandle left, VoxelEngineRenderInstanceHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelEngineRenderInstanceHandle left, VoxelEngineRenderInstanceHandle right)
        {
            return !left.Equals(right);
        }
    }
}
