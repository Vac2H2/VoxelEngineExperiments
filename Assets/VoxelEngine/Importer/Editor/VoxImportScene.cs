using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Importer
{
    internal sealed class VoxScene
    {
        private const int SyntheticNodeIdBase = 1_000_000;

        public VoxScene(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }

        public int? ExpectedModelCount { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        public List<Vector3Int> PendingModelSizes { get; } = new List<Vector3Int>();

        public List<VoxModelDefinition> Models { get; } = new List<VoxModelDefinition>();

        public Dictionary<int, VoxNode> Nodes { get; } = new Dictionary<int, VoxNode>();

        public Dictionary<int, VoxLayer> Layers { get; } = new Dictionary<int, VoxLayer>();

        public List<int> RootNodeIds { get; } = new List<int>();

        public Color32[] Palette { get; set; }

        public void AddWarning(string warning)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                Warnings.Add(warning);
            }
        }

        public void ResolveRoots()
        {
            RootNodeIds.Clear();

            if (Nodes.Count == 0 && Models.Count > 0)
            {
                for (int i = 0; i < Models.Count; i++)
                {
                    int nodeId = SyntheticNodeIdBase + i;
                    Nodes[nodeId] = new VoxShapeNode(nodeId, $"Model_{Models[i].Id}", hidden: false, Models[i].Id);
                    RootNodeIds.Add(nodeId);
                }

                return;
            }

            HashSet<int> referencedNodeIds = new HashSet<int>();
            foreach (VoxNode node in Nodes.Values)
            {
                switch (node)
                {
                    case VoxTransformNode transformNode:
                        referencedNodeIds.Add(transformNode.ChildId);
                        break;
                    case VoxGroupNode groupNode:
                        for (int i = 0; i < groupNode.ChildIds.Count; i++)
                        {
                            referencedNodeIds.Add(groupNode.ChildIds[i]);
                        }

                        break;
                }
            }

            List<int> sortedNodeIds = Nodes.Keys.ToList();
            sortedNodeIds.Sort();
            for (int i = 0; i < sortedNodeIds.Count; i++)
            {
                int nodeId = sortedNodeIds[i];
                if (!referencedNodeIds.Contains(nodeId))
                {
                    RootNodeIds.Add(nodeId);
                }
            }

            if (RootNodeIds.Count == 0)
            {
                RootNodeIds.AddRange(sortedNodeIds);
            }
        }
    }

    internal abstract class VoxNode
    {
        protected VoxNode(int nodeId, string name, bool hidden)
        {
            NodeId = nodeId;
            Name = name ?? string.Empty;
            Hidden = hidden;
        }

        public int NodeId { get; }

        public string Name { get; }

        public bool Hidden { get; }
    }

    internal sealed class VoxTransformNode : VoxNode
    {
        public VoxTransformNode(
            int nodeId,
            string name,
            bool hidden,
            int childId,
            int layerId,
            Vector3 translation,
            Quaternion rotation)
            : base(nodeId, name, hidden)
        {
            ChildId = childId;
            LayerId = layerId;
            Translation = translation;
            Rotation = rotation;
        }

        public int ChildId { get; }

        public int LayerId { get; }

        public Vector3 Translation { get; }

        public Quaternion Rotation { get; }
    }

    internal sealed class VoxGroupNode : VoxNode
    {
        public VoxGroupNode(int nodeId, string name, bool hidden, IReadOnlyList<int> childIds)
            : base(nodeId, name, hidden)
        {
            ChildIds = childIds ?? Array.Empty<int>();
        }

        public IReadOnlyList<int> ChildIds { get; }
    }

    internal sealed class VoxShapeNode : VoxNode
    {
        public VoxShapeNode(int nodeId, string name, bool hidden, int modelId)
            : base(nodeId, name, hidden)
        {
            ModelId = modelId;
        }

        public int ModelId { get; }
    }

    internal readonly struct VoxLayer
    {
        public VoxLayer(string name, bool hidden)
        {
            Name = name ?? string.Empty;
            Hidden = hidden;
        }

        public string Name { get; }

        public bool Hidden { get; }
    }

    internal sealed class VoxModelDefinition
    {
        public VoxModelDefinition(int id, Vector3Int size, IReadOnlyList<VoxVoxel> voxels)
        {
            Id = id;
            Size = size;
            Voxels = voxels ?? Array.Empty<VoxVoxel>();
        }

        public int Id { get; }

        public Vector3Int Size { get; }

        public IReadOnlyList<VoxVoxel> Voxels { get; }
    }

    internal readonly struct VoxVoxel
    {
        public VoxVoxel(Vector3Int position, byte colorIndex)
        {
            Position = position;
            ColorIndex = colorIndex;
        }

        public Vector3Int Position { get; }

        public byte ColorIndex { get; }
    }

    internal readonly struct ImportedPaletteAsset
    {
        public ImportedPaletteAsset(VoxelPaletteAsset asset, AssetReferenceVoxelPalette reference)
        {
            Asset = asset;
            Reference = reference;
        }

        public VoxelPaletteAsset Asset { get; }

        public AssetReferenceVoxelPalette Reference { get; }
    }

    internal readonly struct ImportedModelAsset
    {
        public ImportedModelAsset(
            VoxelModelAsset asset,
            AssetReferenceVoxelModel modelReference,
            Vector3 scenePivotOffset)
        {
            Asset = asset;
            ModelReference = modelReference;
            ScenePivotOffset = scenePivotOffset;
        }

        public VoxelModelAsset Asset { get; }

        public AssetReferenceVoxelModel ModelReference { get; }

        public Vector3 ScenePivotOffset { get; }
    }

    internal sealed class ChunkBuildState
    {
        private readonly bool[] _occupiedVoxels = new bool[VoxelVolume.VoxelsPerChunk];

        public ChunkBuildState(int chunkIndex)
        {
            ChunkIndex = chunkIndex;
        }

        public int ChunkIndex { get; }

        public int3 Min { get; private set; }

        public int3 MaxInclusive { get; private set; }

        public int3 MaxExclusive => MaxInclusive + new int3(1, 1, 1);

        public bool HasVoxels { get; private set; }

        public int OccupiedVoxelCount { get; private set; }

        public void IncludeVoxel(int3 localVoxelCoordinate)
        {
            int flatIndex = VoxelVolume.FlattenChunkVoxelIndex(
                localVoxelCoordinate.x,
                localVoxelCoordinate.y,
                localVoxelCoordinate.z);
            if (!_occupiedVoxels[flatIndex])
            {
                _occupiedVoxels[flatIndex] = true;
                OccupiedVoxelCount++;
            }

            if (!HasVoxels)
            {
                Min = localVoxelCoordinate;
                MaxInclusive = localVoxelCoordinate;
                HasVoxels = true;
                return;
            }

            Min = math.min(Min, localVoxelCoordinate);
            MaxInclusive = math.max(MaxInclusive, localVoxelCoordinate);
        }

        public bool IsOccupied(int x, int y, int z)
        {
            return _occupiedVoxels[VoxelVolume.FlattenChunkVoxelIndex(x, y, z)];
        }
    }

    internal readonly struct Int3x3
    {
        public static readonly Int3x3 Identity = new Int3x3(
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1));

        public Int3x3(Vector3Int row0, Vector3Int row1, Vector3Int row2)
        {
            Row0 = row0;
            Row1 = row1;
            Row2 = row2;
        }

        public Vector3Int Row0 { get; }

        public Vector3Int Row1 { get; }

        public Vector3Int Row2 { get; }

        public int Determinant =>
            (Row0.x * ((Row1.y * Row2.z) - (Row1.z * Row2.y)))
            - (Row0.y * ((Row1.x * Row2.z) - (Row1.z * Row2.x)))
            + (Row0.z * ((Row1.x * Row2.y) - (Row1.y * Row2.x)));
    }
}
