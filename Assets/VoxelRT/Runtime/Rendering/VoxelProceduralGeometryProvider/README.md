# VoxelProceduralGeometryProvider

## Purpose

`VoxelProceduralGeometryProvider` is the voxel-path implementation of `IRayTracingGeometryProvider`.

It is responsible for:

- translating `modelResidencyId` into a procedural RTAS geometry descriptor
- reading model-domain procedural AABB resources from `IVoxelGpuResourceView`
- providing one shared procedural `Material` for voxel RTAS registration

It is not responsible for:

- retaining or releasing model resources
- retaining or releasing palette resources
- uploading AABB data
- binding global voxel shader buffers
- storing instance transform, mask, layer, or shader instance id
- registering instances into RTAS

## Identity

In the current design:

- `sharedGeometryId == modelResidencyId`

The provider does not allocate a second geometry id layer.

That is valid because:

- model procedural AABB lifetime is already owned by the model domain
- each resident model owns one independent procedural AABB buffer
- the provider is only a read-side adapter

## Inputs

The provider is constructed with:

- an `IVoxelGpuResourceView`
- one shared procedural `Material`

The material is shared geometry data.

Instance-specific data such as:

- mask
- layer
- transform
- shader instance id

still belongs to `RayTracingInstanceRegistry`.

## Output

`TryGetGeometryDescriptor(sharedGeometryId, out descriptor)` returns a `Procedural` descriptor built from:

- `VoxelModelResourceDescriptor.ProceduralAabbBuffer`
- `VoxelModelResourceDescriptor.ProceduralAabbCount`
- the provider's shared `Material`

`AabbOffset` is always `0` on this path because each model owns its own AABB buffer.

## Integration

Recommended flow:

1. retain model resources through `VoxelGpuResourceSystem`
2. use the returned `modelResidencyId` as `sharedGeometryId`
3. resolve geometry through `VoxelProceduralGeometryProvider`
4. register scene instances through `RayTracingInstanceRegistry`
5. bind global voxel shader buffers through `VoxelRayTracingResourceBinder`
