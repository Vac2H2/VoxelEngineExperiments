using Unity.Entities;

namespace VoxelEngineDOTS
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VoxelEngineRootSystem : SystemBase
    {
        protected override void OnCreate()
        {
        }

        protected override void OnUpdate()
        {
            // Future DOTS voxel engine modules will be ticked here in explicit order.
        }

        protected override void OnDestroy()
        {
        }
    }
}
