using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Data.VoxelWorldHierarchy
{
    [Serializable]
    public sealed class AssetReferenceVoxelWorldHierarchy : AssetReferenceT<VoxelWorldHierarchy>
    {
        public AssetReferenceVoxelWorldHierarchy(string guid)
            : base(guid)
        {
        }
    }

    [PreferBinarySerialization]
    public sealed class VoxelWorldHierarchy : ScriptableObject
    {
        [SerializeField] private string _sourceFilePath = string.Empty;
        [SerializeField] private VoxelWorldHierarchyNode[] _nodes = Array.Empty<VoxelWorldHierarchyNode>();
        [SerializeField] private int[] _rootNodeIndices = Array.Empty<int>();

        public string SourceFilePath => _sourceFilePath ?? string.Empty;

        public int NodeCount => _nodes?.Length ?? 0;

        public VoxelWorldHierarchyNode[] Nodes => _nodes ?? Array.Empty<VoxelWorldHierarchyNode>();

        public int[] RootNodeIndices => _rootNodeIndices ?? Array.Empty<int>();

        public bool TryGetNode(int nodeIndex, out VoxelWorldHierarchyNode node)
        {
            if (_nodes != null && (uint)nodeIndex < _nodes.Length)
            {
                node = _nodes[nodeIndex];
                return node != null;
            }

            node = null;
            return false;
        }

        public void SetImportedData(string sourceFilePath, VoxelWorldHierarchyNode[] nodes, int[] rootNodeIndices)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (rootNodeIndices == null)
            {
                throw new ArgumentNullException(nameof(rootNodeIndices));
            }

            _sourceFilePath = sourceFilePath ?? string.Empty;
            _nodes = CloneNodes(nodes);
            _rootNodeIndices = (int[])rootNodeIndices.Clone();
        }

        private static VoxelWorldHierarchyNode[] CloneNodes(VoxelWorldHierarchyNode[] nodes)
        {
            if (nodes.Length == 0)
            {
                return Array.Empty<VoxelWorldHierarchyNode>();
            }

            VoxelWorldHierarchyNode[] clone = new VoxelWorldHierarchyNode[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                clone[i] = nodes[i] == null ? null : new VoxelWorldHierarchyNode(nodes[i]);
            }

            return clone;
        }
    }

    [Serializable]
    public sealed class VoxelWorldHierarchyNode
    {
        [SerializeField] private int _sourceNodeId;
        [SerializeField] private string _name = string.Empty;
        [SerializeField] private int _parentIndex = -1;
        [SerializeField] private int[] _childIndices = Array.Empty<int>();
        [SerializeField] private Vector3 _localPosition = Vector3.zero;
        [SerializeField] private Quaternion _localRotation = Quaternion.identity;
        [SerializeField] private Vector3 _localScale = Vector3.one;
        [SerializeField] private Vector3 _renderLocalOffset = Vector3.zero;
        [SerializeField] private bool _hidden;
        [SerializeField] private AssetReferenceVoxelModel _modelReference;
        [SerializeField] private AssetReferenceVoxelPalette _paletteReference;

        public VoxelWorldHierarchyNode()
        {
        }

        public VoxelWorldHierarchyNode(
            int sourceNodeId,
            string name,
            int parentIndex,
            int[] childIndices,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Vector3 renderLocalOffset,
            bool hidden,
            AssetReferenceVoxelModel modelReference,
            AssetReferenceVoxelPalette paletteReference)
        {
            _sourceNodeId = sourceNodeId;
            _name = name ?? string.Empty;
            _parentIndex = parentIndex;
            _childIndices = childIndices == null || childIndices.Length == 0
                ? Array.Empty<int>()
                : (int[])childIndices.Clone();
            _localPosition = localPosition;
            _localRotation = localRotation;
            _localScale = localScale;
            _renderLocalOffset = renderLocalOffset;
            _hidden = hidden;
            _modelReference = CloneReference(modelReference);
            _paletteReference = CloneReference(paletteReference);
        }

        public VoxelWorldHierarchyNode(VoxelWorldHierarchyNode other)
            : this(
                other?._sourceNodeId ?? 0,
                other?._name ?? string.Empty,
                other?._parentIndex ?? -1,
                other?._childIndices,
                other?._localPosition ?? Vector3.zero,
                other?._localRotation ?? Quaternion.identity,
                other?._localScale ?? Vector3.one,
                other?._renderLocalOffset ?? Vector3.zero,
                other?._hidden ?? false,
                other?._modelReference,
                other?._paletteReference)
        {
        }

        public int SourceNodeId => _sourceNodeId;

        public string Name => _name ?? string.Empty;

        public int ParentIndex => _parentIndex;

        public int[] ChildIndices => _childIndices ?? Array.Empty<int>();

        public Vector3 LocalPosition => _localPosition;

        public Quaternion LocalRotation => _localRotation;

        public Vector3 LocalScale => _localScale;

        public Vector3 RenderLocalOffset => _renderLocalOffset;

        public bool Hidden => _hidden;

        public AssetReferenceVoxelModel ModelReference => _modelReference;

        public AssetReferenceVoxelPalette PaletteReference => _paletteReference;

        public bool HasRenderableContent =>
            _modelReference != null &&
            _paletteReference != null &&
            _modelReference.RuntimeKeyIsValid() &&
            _paletteReference.RuntimeKeyIsValid();

        private static AssetReferenceVoxelModel CloneReference(AssetReferenceVoxelModel source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.AssetGUID))
            {
                return null;
            }

            return new AssetReferenceVoxelModel(source.AssetGUID);
        }

        private static AssetReferenceVoxelPalette CloneReference(AssetReferenceVoxelPalette source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.AssetGUID))
            {
                return null;
            }

            return new AssetReferenceVoxelPalette(source.AssetGUID);
        }
    }
}
