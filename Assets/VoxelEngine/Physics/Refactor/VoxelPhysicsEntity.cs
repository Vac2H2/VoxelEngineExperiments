using System;

namespace VoxelEngine.Physics.Refactor
{
    public enum VoxelPhysicsEntityArchetype
    {
        None = 0,
        StaticBody = 1,
        DynamicBody = 2,
        KinematicBody = 3,
    }

    public readonly struct VoxelPhysicsEntityDescriptor
    {
        public static VoxelPhysicsEntityDescriptor Static =>
            new VoxelPhysicsEntityDescriptor(VoxelPhysicsEntityArchetype.StaticBody);

        public static VoxelPhysicsEntityDescriptor Dynamic =>
            new VoxelPhysicsEntityDescriptor(VoxelPhysicsEntityArchetype.DynamicBody);

        public static VoxelPhysicsEntityDescriptor Kinematic =>
            new VoxelPhysicsEntityDescriptor(VoxelPhysicsEntityArchetype.KinematicBody);

        public VoxelPhysicsEntityDescriptor(
            VoxelPhysicsEntityArchetype archetype,
            bool isEnabled = true)
        {
            ValidateArchetype(archetype);

            Archetype = archetype;
            IsEnabled = isEnabled;
        }

        public VoxelPhysicsEntityArchetype Archetype { get; }

        public bool IsEnabled { get; }

        internal static void ValidateArchetype(VoxelPhysicsEntityArchetype archetype)
        {
            if (archetype == VoxelPhysicsEntityArchetype.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(archetype),
                    "Physics entity archetype must be explicit.");
            }
        }
    }

    public readonly struct VoxelPhysicsEntity
    {
        internal VoxelPhysicsEntity(
            PhysicsEntityHandle handle,
            VoxelPhysicsEntityArchetype archetype,
            bool isEnabled)
        {
            if (!handle.IsValid)
            {
                throw new ArgumentException(
                    "Physics entity handle must be valid.",
                    nameof(handle));
            }

            VoxelPhysicsEntityDescriptor.ValidateArchetype(archetype);

            Handle = handle;
            Archetype = archetype;
            IsEnabled = isEnabled;
        }

        public PhysicsEntityHandle Handle { get; }

        public VoxelPhysicsEntityArchetype Archetype { get; }

        public bool IsEnabled { get; }

        public bool IsDynamic => Archetype == VoxelPhysicsEntityArchetype.DynamicBody;

        public bool IsKinematic => Archetype == VoxelPhysicsEntityArchetype.KinematicBody;

        public bool IsStatic => Archetype == VoxelPhysicsEntityArchetype.StaticBody;

        public VoxelPhysicsEntity WithArchetype(VoxelPhysicsEntityArchetype archetype)
        {
            return new VoxelPhysicsEntity(Handle, archetype, IsEnabled);
        }

        public VoxelPhysicsEntity WithEnabled(bool isEnabled)
        {
            return new VoxelPhysicsEntity(Handle, Archetype, isEnabled);
        }
    }
}
