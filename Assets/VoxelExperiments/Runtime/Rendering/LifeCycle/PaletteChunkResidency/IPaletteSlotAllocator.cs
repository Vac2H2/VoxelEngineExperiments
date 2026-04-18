namespace VoxelExperiments.Runtime.Rendering.PaletteChunkResidency
{
    internal interface IPaletteSlotAllocator
    {
        uint Allocate();

        void Free(uint slot);

        void Reset();
    }
}
