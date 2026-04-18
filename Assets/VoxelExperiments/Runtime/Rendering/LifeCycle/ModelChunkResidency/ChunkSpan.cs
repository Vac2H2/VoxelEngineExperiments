using System;

namespace VoxelExperiments.Runtime.Rendering.ModelChunkResidency
{
    internal readonly struct ChunkSpan : IEquatable<ChunkSpan>
    {
        public ChunkSpan(uint startSlot, uint count)
        {
            StartSlot = startSlot;
            Count = count;
        }

        public uint StartSlot { get; }

        public uint Count { get; }

        public uint EndSlotExclusive => checked(StartSlot + Count);

        public bool IsEmpty => Count == 0;

        public bool Equals(ChunkSpan other)
        {
            return StartSlot == other.StartSlot && Count == other.Count;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkSpan other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StartSlot, Count);
        }

        public override string ToString()
        {
            return $"[{StartSlot}, {EndSlotExclusive})";
        }

        public static bool operator ==(ChunkSpan left, ChunkSpan right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ChunkSpan left, ChunkSpan right)
        {
            return !left.Equals(right);
        }
    }
}
