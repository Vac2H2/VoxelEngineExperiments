# ShapeUpdatePipeline Jobs

This folder will contain jobs that operate on `VoxelEngineModules.ShapeManagement.ShapeDataContainer`.

Job design rules:

- Use local chunk indices, not global shape offsets.
- Read and write one Shape container's native buffers unless the job explicitly documents a batching strategy.
- Use `Chunks` as authoritative Shape data.
- Use `Fragments` as derived connectivity data.
- Keep `Chunks.Positions`, `Chunks.Used`, and `Chunks.IndexByPosition` consistent when modifying chunk topology.
- Rebuild or patch `Fragments` after modifying authoritative chunk topology.
- Avoid dependencies on `VoxelEngineModules.Shape.ShapeDataView` or the old continuous Shape memory layout.

No jobs are defined yet. Add concrete job READMEs or update the parent pipeline README when the first stage is designed.
