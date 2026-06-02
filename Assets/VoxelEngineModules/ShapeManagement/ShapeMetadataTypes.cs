namespace VoxelEngineModules.ShapeManagement
{
    public struct ShapeMetadata
    {
        public int BodyHandle;
        public byte IsUsed;

        public readonly bool Used => IsUsed != 0;
    }
}
