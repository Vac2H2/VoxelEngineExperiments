# ModelProceduralAabb

## Purpose

`ModelProceduralAabb` is the runtime module that owns procedural RTAS AABB resources for a resident model.

It is responsible for:

- deduplicating local-space chunk AABB data by `modelKey`
- uploading one stable `GraphicsBuffer` per live model AABB set
- exposing `aabbBuffer + aabbCount` through a descriptor
- managing retain/release lifetime for procedural geometry source buffers

It is not responsible for:

- model chunk data residency
- palette residency
- RTAS scene registration
- scene instance identity
- global shader buffer binding

## Public API

The main contract is [`IModelProceduralAabbService.cs`](./IModelProceduralAabbService.cs).

The service exposes:

- `int Retain(object modelKey, NativeArray<ModelChunkAabb> chunkAabbs)`
- `void Release(int residencyId)`
- `ModelProceduralAabbDescriptor GetDescriptor(int residencyId)`

## Data Layout

Each entry uses [`ModelChunkAabb.cs`](./ModelChunkAabb.cs):

- `Vector3 Min`
- `Vector3 Max`

The buffer stride is 24 bytes.

## Lifetime

This module owns one `GraphicsBuffer` per resident model AABB set.

Unlike chunk-data residency buffers, these AABB buffers are not compacted into a shared pool. They remain stable for the lifetime of the resident AABB entry and are released only on the final `Release(...)`.
