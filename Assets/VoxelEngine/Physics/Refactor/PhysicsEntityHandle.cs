using System;

namespace VoxelEngine.Physics.Refactor
{
    public readonly struct PhysicsEntityHandle : IEquatable<PhysicsEntityHandle>
    {
        public const int InvalidIndex = -1;
        public const int InvalidVersion = 0;

        public static PhysicsEntityHandle Invalid => new PhysicsEntityHandle(
            InvalidIndex,
            InvalidVersion);

        public PhysicsEntityHandle(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public int Index { get; }

        public int Version { get; }

        public bool IsValid => Index >= 0 && Version > InvalidVersion;

        public bool Equals(PhysicsEntityHandle other)
        {
            return Index == other.Index &&
                   Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is PhysicsEntityHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ Version;
            }
        }

        public override string ToString()
        {
            return IsValid ? $"{Index}:{Version}" : "Invalid";
        }

        public static bool operator ==(
            PhysicsEntityHandle left,
            PhysicsEntityHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            PhysicsEntityHandle left,
            PhysicsEntityHandle right)
        {
            return !left.Equals(right);
        }
    }
}
