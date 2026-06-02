using System;
using Unity.Collections;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Physics.DebugRenderer
{
    [CreateAssetMenu(
        fileName = "MiniPhysicsDebugPalette",
        menuName = "VoxelEngine/Physics/Mini Physics Debug Palette")]
    public sealed class MiniPhysicsDebugPaletteAsset : ScriptableObject
    {
        public const byte EmptyStateIndex = 0;
        public const byte DefaultStateIndex = 1;
        public const byte BroadphaseCandidateStateIndex = 2;
        public const byte NarrowphaseHitStateIndex = 3;
        public const byte SleepingStateIndex = 4;

        [SerializeField] private Color32 _empty = new Color32(0, 0, 0, 0);
        [SerializeField] private Color32 _default = new Color32(255, 255, 255, 255);
        [SerializeField] private Color32 _broadphaseCandidate = new Color32(255, 214, 64, 255);
        [SerializeField] private Color32 _narrowphaseHit = new Color32(255, 76, 76, 255);
        [SerializeField] private Color32 _sleeping = new Color32(86, 190, 255, 255);

        public int ColorVersion
        {
            get
            {
                unchecked
                {
                    int hash = 17;
                    AppendColorHash(ref hash, _empty);
                    AppendColorHash(ref hash, _default);
                    AppendColorHash(ref hash, _broadphaseCandidate);
                    AppendColorHash(ref hash, _narrowphaseHit);
                    AppendColorHash(ref hash, _sleeping);
                    return hash;
                }
            }
        }

        public VoxelPalette CreateVoxelPalette(Allocator allocator)
        {
            VoxelPalette palette = CreateDefaultVoxelPalette(allocator);
            palette[EmptyStateIndex] = ToVoxelColor(_empty);
            palette[DefaultStateIndex] = ToVoxelColor(_default);
            palette[BroadphaseCandidateStateIndex] = ToVoxelColor(_broadphaseCandidate);
            palette[NarrowphaseHitStateIndex] = ToVoxelColor(_narrowphaseHit);
            palette[SleepingStateIndex] = ToVoxelColor(_sleeping);
            return palette;
        }

        public static VoxelPalette CreateDefaultVoxelPalette(Allocator allocator)
        {
            VoxelPalette palette = new VoxelPalette(allocator);
            palette[EmptyStateIndex] = new VoxelColor(0, 0, 0, 0);

            VoxelColor defaultColor = new VoxelColor(255, 255, 255, 255);
            for (int index = 1; index < VoxelPalette.ColorCount; index++)
            {
                palette[index] = defaultColor;
            }

            palette[BroadphaseCandidateStateIndex] = new VoxelColor(255, 214, 64, 255);
            palette[NarrowphaseHitStateIndex] = new VoxelColor(255, 76, 76, 255);
            palette[SleepingStateIndex] = new VoxelColor(86, 190, 255, 255);
            return palette;
        }

        public static int DefaultColorVersion
        {
            get
            {
                unchecked
                {
                    int hash = 17;
                    AppendColorHash(ref hash, new Color32(0, 0, 0, 0));
                    AppendColorHash(ref hash, new Color32(255, 255, 255, 255));
                    AppendColorHash(ref hash, new Color32(255, 214, 64, 255));
                    AppendColorHash(ref hash, new Color32(255, 76, 76, 255));
                    AppendColorHash(ref hash, new Color32(86, 190, 255, 255));
                    return hash;
                }
            }
        }

        private static void AppendColorHash(ref int hash, Color32 color)
        {
            hash = (hash * 31) + color.r;
            hash = (hash * 31) + color.g;
            hash = (hash * 31) + color.b;
            hash = (hash * 31) + color.a;
        }

        private static VoxelColor ToVoxelColor(Color32 color)
        {
            return new VoxelColor(color.r, color.g, color.b, color.a);
        }
    }
}
