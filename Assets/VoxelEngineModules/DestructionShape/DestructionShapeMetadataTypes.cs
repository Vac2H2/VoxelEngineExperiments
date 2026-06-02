namespace VoxelEngineModules.DestructionShape
{
    public struct DestructionShapeMetadata
    {
        public byte IsUsed;

        public readonly bool Used => IsUsed != 0;
    }
}
