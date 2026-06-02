using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Physics.Destruction
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)]
    [AddComponentMenu("VoxelEngine/Physics/Mini Destruction Engine")]
    public sealed class MiniDestructionEngine : MonoBehaviour
    {
        private static readonly int3[] Directions6 =
        {
            new int3(1, 0, 0),
            new int3(-1, 0, 0),
            new int3(0, 1, 0),
            new int3(0, -1, 0),
            new int3(0, 0, 1),
            new int3(0, 0, -1)
        };

        [SerializeField] private DestructionDebugRenderer _renderer;
        [SerializeField] private bool _syncAfterSolve;
        [SerializeField] private bool _createColliderProxies = true;
        [SerializeField] private bool _destroyEmptyFragmentProxies = true;
        [SerializeField] private Color32 _ungroundedColor = new Color32(255, 220, 64, 255);
        [SerializeField] private bool _randomizeGroundedColors = true;
        [SerializeField] private Color32[] _groundedColors =
        {
            new Color32(63, 203, 82, 255),
            new Color32(83, 221, 126, 255),
            new Color32(38, 176, 95, 255),
            new Color32(120, 235, 98, 255),
            new Color32(46, 196, 144, 255),
            new Color32(104, 210, 65, 255)
        };

        private readonly List<DestructionDamageBatch> _damageBatches = new List<DestructionDamageBatch>();
        private readonly List<int3> _seeds = new List<int3>();
        private readonly HashSet<int3> _seedSet = new HashSet<int3>();
        private readonly Dictionary<int3, int> _ownerByVoxel = new Dictionary<int3, int>();
        private readonly List<Group> _groups = new List<Group>();
        private readonly List<List<int3>> _detachedComponents = new List<List<int3>>();
        private readonly HashSet<int> _assignedGroundedColors = new HashSet<int>();
        private int _nextColorIndex;

        private void Awake()
        {
            ResolveRenderer();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            DestructionDebugRenderer renderer = ResolveRenderer();
            if (renderer == null)
            {
                return;
            }

            _damageBatches.Clear();
            if (renderer.ConsumeDamageBatches(_damageBatches) == 0)
            {
                return;
            }

            for (int batchIndex = 0; batchIndex < _damageBatches.Count; batchIndex++)
            {
                DestructionDamageBatch batch = _damageBatches[batchIndex];
                if (batch == null ||
                    batch.DeletedVoxels.Count == 0 ||
                    !renderer.TryGetObject(batch.ObjectRuntimeId, out DestructionDebugObject debugObject))
                {
                    continue;
                }

                SolveBatch(renderer, debugObject, batch.DeletedVoxels);
            }

            renderer.MarkRenderDataDirty();
            if (_syncAfterSolve)
            {
                renderer.SyncRenderBackend();
            }
        }

        private bool SolveBatch(
            DestructionDebugRenderer renderer,
            DestructionDebugObject debugObject,
            List<int3> deletedVoxels)
        {
            if (!debugObject.HasAnyVoxel())
            {
                ReleaseObject(renderer, debugObject);
                return true;
            }

            CollectSeeds(debugObject, deletedVoxels);
            if (_seeds.Count <= 1)
            {
                ApplyGroundingColor(debugObject);
                ConfigureProxyForObject(renderer, debugObject);
                return false;
            }

            FindDetachedComponents(debugObject);
            if (_detachedComponents.Count == 0)
            {
                ApplyGroundingColor(debugObject);
                ConfigureProxyForObject(renderer, debugObject);
                return false;
            }

            for (int componentIndex = 0; componentIndex < _detachedComponents.Count; componentIndex++)
            {
                ExtractDetachedComponent(renderer, debugObject, _detachedComponents[componentIndex]);
            }

            if (!debugObject.HasAnyVoxel())
            {
                ReleaseObject(renderer, debugObject);
            }
            else
            {
                ApplyGroundingColor(debugObject);
                ConfigureProxyForObject(renderer, debugObject);
            }

            return true;
        }

        private void CollectSeeds(
            DestructionDebugObject debugObject,
            List<int3> deletedVoxels)
        {
            _seeds.Clear();
            _seedSet.Clear();

            for (int deletedIndex = 0; deletedIndex < deletedVoxels.Count; deletedIndex++)
            {
                int3 deletedVoxel = deletedVoxels[deletedIndex];
                for (int directionIndex = 0; directionIndex < Directions6.Length; directionIndex++)
                {
                    int3 neighbor = deletedVoxel + Directions6[directionIndex];
                    if (!debugObject.TryGetVoxel(neighbor, out _) || !_seedSet.Add(neighbor))
                    {
                        continue;
                    }

                    _seeds.Add(neighbor);
                }
            }
        }

        private void FindDetachedComponents(DestructionDebugObject debugObject)
        {
            _ownerByVoxel.Clear();
            _groups.Clear();
            _detachedComponents.Clear();

            for (int seedIndex = 0; seedIndex < _seeds.Count; seedIndex++)
            {
                int groupId = _groups.Count;
                Group group = new Group(groupId);
                group.Frontier.Enqueue(_seeds[seedIndex]);
                group.Voxels.Add(_seeds[seedIndex]);
                _groups.Add(group);
                _ownerByVoxel.Add(_seeds[seedIndex], groupId);
            }

            int activeGroupCount = _groups.Count;
            while (activeGroupCount > 1)
            {
                int groupId = SelectGroupWithSmallestFrontier();
                if (groupId < 0)
                {
                    break;
                }

                groupId = FindGroup(groupId);
                Group group = _groups[groupId];
                if (!group.Active)
                {
                    continue;
                }

                if (group.Frontier.Count == 0)
                {
                    group.Active = false;
                    activeGroupCount--;
                    _detachedComponents.Add(group.Voxels);
                    continue;
                }

                ExpandGroup(debugObject, groupId, ref activeGroupCount);
            }
        }

        private void ExpandGroup(
            DestructionDebugObject debugObject,
            int groupId,
            ref int activeGroupCount)
        {
            groupId = FindGroup(groupId);
            Group group = _groups[groupId];
            if (group.Frontier.Count == 0)
            {
                return;
            }

            int3 voxel = group.Frontier.Dequeue();
            for (int directionIndex = 0; directionIndex < Directions6.Length; directionIndex++)
            {
                int3 neighbor = voxel + Directions6[directionIndex];
                if (!debugObject.TryGetVoxel(neighbor, out _))
                {
                    continue;
                }

                if (!_ownerByVoxel.TryGetValue(neighbor, out int owner))
                {
                    groupId = FindGroup(groupId);
                    Group rootGroup = _groups[groupId];
                    _ownerByVoxel.Add(neighbor, groupId);
                    rootGroup.Frontier.Enqueue(neighbor);
                    rootGroup.Voxels.Add(neighbor);
                    continue;
                }

                int otherGroupId = FindGroup(owner);
                groupId = FindGroup(groupId);
                if (otherGroupId != groupId)
                {
                    groupId = MergeGroups(groupId, otherGroupId, ref activeGroupCount);
                }
            }
        }

        private int SelectGroupWithSmallestFrontier()
        {
            int bestGroupId = -1;
            for (int groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
            {
                int rootGroupId = FindGroup(groupIndex);
                if (rootGroupId != groupIndex)
                {
                    continue;
                }

                Group group = _groups[rootGroupId];
                if (!group.Active)
                {
                    continue;
                }

                if (bestGroupId < 0 || group.Frontier.Count < _groups[bestGroupId].Frontier.Count)
                {
                    bestGroupId = rootGroupId;
                }
            }

            return bestGroupId;
        }

        private int MergeGroups(int leftGroupId, int rightGroupId, ref int activeGroupCount)
        {
            int leftRoot = FindGroup(leftGroupId);
            int rightRoot = FindGroup(rightGroupId);
            if (leftRoot == rightRoot)
            {
                return leftRoot;
            }

            if (_groups[leftRoot].Voxels.Count < _groups[rightRoot].Voxels.Count)
            {
                int temp = leftRoot;
                leftRoot = rightRoot;
                rightRoot = temp;
            }

            Group destination = _groups[leftRoot];
            Group source = _groups[rightRoot];
            source.Parent = leftRoot;
            if (source.Active)
            {
                source.Active = false;
                activeGroupCount--;
            }

            while (source.Frontier.Count > 0)
            {
                destination.Frontier.Enqueue(source.Frontier.Dequeue());
            }

            destination.Voxels.AddRange(source.Voxels);
            source.Voxels.Clear();
            return leftRoot;
        }

        private int FindGroup(int groupId)
        {
            Group group = _groups[groupId];
            if (group.Parent == groupId)
            {
                return groupId;
            }

            group.Parent = FindGroup(group.Parent);
            return group.Parent;
        }

        private void ExtractDetachedComponent(
            DestructionDebugRenderer renderer,
            DestructionDebugObject sourceObject,
            List<int3> componentVoxels)
        {
            DestructionDebugObject fragmentObject = null;
            for (int voxelIndex = 0; voxelIndex < componentVoxels.Count; voxelIndex++)
            {
                int3 voxelPosition = componentVoxels[voxelIndex];
                if (!sourceObject.TryGetVoxel(voxelPosition, out byte value))
                {
                    continue;
                }

                fragmentObject ??= renderer.CreateObject(_ungroundedColor);
                fragmentObject.SetVoxel(voxelPosition, value);
                sourceObject.SetVoxel(voxelPosition, 0);
            }

            if (fragmentObject != null)
            {
                ApplyGroundingColor(fragmentObject);
                ConfigureProxyForObject(renderer, fragmentObject);
            }
        }

        private void ConfigureProxyForObject(
            DestructionDebugRenderer renderer,
            DestructionDebugObject debugObject)
        {
            if (!_createColliderProxies || renderer == null || debugObject == null)
            {
                return;
            }

            DestructionDebugObjectBinding binding = FindBinding(renderer, debugObject.RuntimeId);
            if (binding == null)
            {
                binding = CreateProxyBinding(renderer, debugObject);
            }
            else
            {
                binding.Bind(renderer, debugObject);
            }

            BoxCollider boxCollider = binding.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = binding.gameObject.AddComponent<BoxCollider>();
            }

            ConfigureCollider(renderer, debugObject, boxCollider);
        }

        private void ConfigureCollider(
            DestructionDebugRenderer renderer,
            DestructionDebugObject debugObject,
            BoxCollider boxCollider)
        {
            if (boxCollider == null)
            {
                return;
            }

            if (!debugObject.TryGetOccupiedVoxelBounds(out int3 minVoxel, out int3 maxVoxelExclusive))
            {
                boxCollider.enabled = false;
                return;
            }

            float voxelSize = Mathf.Max(0.000001f, renderer.VoxelSize);
            int3 sizeInVoxels = maxVoxelExclusive - minVoxel;
            boxCollider.enabled = true;
            boxCollider.isTrigger = false;
            boxCollider.center = ToVector3(minVoxel + maxVoxelExclusive) * (0.5f * voxelSize);
            boxCollider.size = ToVector3(sizeInVoxels) * voxelSize;
        }

        private DestructionDebugObjectBinding FindBinding(
            DestructionDebugRenderer renderer,
            int objectRuntimeId)
        {
            DestructionDebugObjectBinding[] bindings =
                renderer.GetComponentsInChildren<DestructionDebugObjectBinding>(true);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                DestructionDebugObjectBinding binding = bindings[bindingIndex];
                if (binding != null && binding.ObjectRuntimeId == objectRuntimeId)
                {
                    return binding;
                }
            }

            return null;
        }

        private DestructionDebugObjectBinding CreateProxyBinding(
            DestructionDebugRenderer renderer,
            DestructionDebugObject debugObject)
        {
            GameObject proxyObject = new GameObject($"Destruction Fragment {debugObject.RuntimeId}");
            proxyObject.layer = renderer.gameObject.layer;
            proxyObject.transform.SetParent(renderer.transform, false);
            proxyObject.transform.localPosition = Vector3.zero;
            proxyObject.transform.localRotation = Quaternion.identity;
            proxyObject.transform.localScale = Vector3.one;

            DestructionDebugObjectBinding binding = proxyObject.AddComponent<DestructionDebugObjectBinding>();
            binding.Bind(renderer, debugObject);
            return binding;
        }

        private void ReleaseObject(
            DestructionDebugRenderer renderer,
            DestructionDebugObject debugObject)
        {
            int runtimeId = debugObject.RuntimeId;
            DestructionDebugObjectBinding binding = FindBinding(renderer, runtimeId);
            if (binding != null)
            {
                BoxCollider boxCollider = binding.GetComponent<BoxCollider>();
                if (boxCollider != null)
                {
                    boxCollider.enabled = false;
                }

                binding.ObjectRuntimeId = 0;
                if (_destroyEmptyFragmentProxies && binding.gameObject != renderer.gameObject)
                {
                    Destroy(binding.gameObject);
                }
            }

            _assignedGroundedColors.Remove(runtimeId);
            renderer.RemoveObject(runtimeId);
        }

        private void ApplyGroundingColor(DestructionDebugObject debugObject)
        {
            if (debugObject == null)
            {
                return;
            }

            if (!debugObject.IsGrounded)
            {
                _assignedGroundedColors.Remove(debugObject.RuntimeId);
                debugObject.Color = _ungroundedColor;
                return;
            }

            if (_assignedGroundedColors.Add(debugObject.RuntimeId))
            {
                debugObject.Color = NextGroundedColor();
            }
        }

        private Color32 NextGroundedColor()
        {
            if (!_randomizeGroundedColors && _groundedColors != null && _groundedColors.Length > 0)
            {
                Color32 color = _groundedColors[_nextColorIndex % _groundedColors.Length];
                _nextColorIndex++;
                return color;
            }

            float hue = Mathf.Lerp(0.25f, 0.42f, Mathf.Repeat(_nextColorIndex * 0.6180339f, 1.0f));
            float saturation = Mathf.Lerp(0.58f, 0.9f, Mathf.Repeat(_nextColorIndex * 0.381966f, 1.0f));
            float value = Mathf.Lerp(0.72f, 1.0f, Mathf.Repeat(_nextColorIndex * 0.271828f, 1.0f));
            _nextColorIndex++;
            Color colorFromHue = Color.HSVToRGB(hue, saturation, value);
            colorFromHue.a = 1.0f;
            return colorFromHue;
        }

        private DestructionDebugRenderer ResolveRenderer()
        {
            if (_renderer != null)
            {
                return _renderer;
            }

            _renderer = GetComponent<DestructionDebugRenderer>();
            if (_renderer != null)
            {
                return _renderer;
            }

            _renderer = GetComponentInParent<DestructionDebugRenderer>();
            return _renderer;
        }

        private static Vector3 ToVector3(int3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private sealed class Group
        {
            public Group(int parent)
            {
                Parent = parent;
                Active = true;
            }

            public int Parent { get; set; }

            public bool Active { get; set; }

            public Queue<int3> Frontier { get; } = new Queue<int3>();

            public List<int3> Voxels { get; } = new List<int3>();
        }
    }
}
