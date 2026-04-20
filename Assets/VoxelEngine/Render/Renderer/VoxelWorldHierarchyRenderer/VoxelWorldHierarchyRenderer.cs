using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using VoxelEngine.Data.VoxelWorldHierarchy;
using VoxelEngine.Render.RenderBackend;
using VoxelEngine.Render.RenderPipeline;

namespace VoxelEngine.Render.Renderer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Render/Renderer/Voxel World Hierarchy Renderer")]
    public sealed class VoxelWorldHierarchyRenderer : MonoBehaviour
    {
        [SerializeField] private AssetReferenceVoxelWorldHierarchy _hierarchyAsset;

        [NonSerialized] private readonly List<PendingRenderableNode> _pendingNodes = new List<PendingRenderableNode>();
        [NonSerialized] private readonly List<VoxelEngineRenderInstanceHandle> _activeHandles = new List<VoxelEngineRenderInstanceHandle>();
        [NonSerialized] private VoxelEngineRenderBackend _registeredBackend;
        [NonSerialized] private string _registeredHierarchyGuid = string.Empty;
        [NonSerialized] private VoxelEngineRenderBackend _lastFailedBackend;
        [NonSerialized] private string _lastFailedHierarchyGuid = string.Empty;
        [NonSerialized] private string _lastLoggedState = string.Empty;
        [NonSerialized] private bool _hasRegistrationFailure;
        [NonSerialized] private int _skippedEmptyNodeCount;
        [NonSerialized] private AsyncOperationHandle<VoxelWorldHierarchy> _loadedHierarchyHandle;
        [NonSerialized] private string _loadedHierarchyGuid = string.Empty;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            transform.hasChanged = false;
            TryRefreshRegistration();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (transform.hasChanged)
            {
                transform.hasChanged = false;
                ReleaseRegistration(logRelease: false);
            }

            TryRefreshRegistration();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ReleaseRegistration();
            ReleaseLoadedHierarchy();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            TryRefreshRegistration();
        }

        private void TryRefreshRegistration()
        {
            string hierarchyGuid = GetHierarchyGuid();

            if (!HasValidHierarchyReference())
            {
                LogStateOnce("missing-hierarchy", "Waiting for a VoxelWorldHierarchy Addressable reference before loading hierarchy instances.");
                ClearRegistrationFailure();
                ReleaseRegistration(logRelease: false);
                ReleaseLoadedHierarchy(logRelease: false);
                return;
            }

            VoxelEngineRenderBackend currentBackend = TryGetCurrentBackend();
            if (currentBackend == null)
            {
                LogStateOnce("missing-backend", "Waiting for an active VoxelEngineRenderPipeline before loading hierarchy instances.");
                ReleaseRegistration(logRelease: false);
                return;
            }

            bool backendChanged = _registeredBackend != null && !ReferenceEquals(_registeredBackend, currentBackend);
            bool hierarchyChanged = !StringComparer.Ordinal.Equals(_registeredHierarchyGuid, hierarchyGuid);
            if (backendChanged || hierarchyChanged)
            {
                ReleaseRegistration(logRelease: false);
            }

            if (!StringComparer.Ordinal.Equals(_loadedHierarchyGuid, hierarchyGuid))
            {
                ReleaseLoadedHierarchy(logRelease: false);
            }

            if (!TryEnsureHierarchyLoaded(hierarchyGuid, out VoxelWorldHierarchy hierarchy))
            {
                return;
            }

            if (_activeHandles.Count > 0 &&
                ReferenceEquals(_registeredBackend, currentBackend) &&
                StringComparer.Ordinal.Equals(_registeredHierarchyGuid, hierarchyGuid))
            {
                ClearRegistrationFailure();
                LogStateOnce(
                    $"registered:{hierarchyGuid}:{_activeHandles.Count}:{_skippedEmptyNodeCount}",
                    $"Loaded hierarchy '{hierarchy.name}' with {_activeHandles.Count} voxel instances and {_skippedEmptyNodeCount} empty nodes skipped.");
                return;
            }

            if (IsSameFailure(currentBackend, hierarchyGuid))
            {
                return;
            }

            try
            {
                RegisterAllInstances(currentBackend, hierarchyGuid, hierarchy);
                ClearRegistrationFailure();
                LogStateOnce(
                    $"registered:{hierarchyGuid}:{_activeHandles.Count}:{_skippedEmptyNodeCount}",
                    $"Loaded hierarchy '{hierarchy.name}' with {_activeHandles.Count} voxel instances and {_skippedEmptyNodeCount} empty nodes skipped.");
            }
            catch (Exception exception)
            {
                RecordRegistrationFailure(currentBackend, hierarchyGuid);
                ReleaseRegistration(logRelease: false);
                LogStateOnce(
                    $"registration-failed:{hierarchyGuid}",
                    $"Failed while loading hierarchy '{hierarchy.name}'.");
                Debug.LogException(exception, this);
            }
        }

        private void RegisterAllInstances(VoxelEngineRenderBackend backend, string hierarchyGuid, VoxelWorldHierarchy hierarchy)
        {
            if (backend == null)
            {
                throw new ArgumentNullException(nameof(backend));
            }

            if (hierarchy == null)
            {
                throw new ArgumentNullException(nameof(hierarchy));
            }

            PreparePendingNodes(hierarchy);
            _registeredBackend = backend;
            _registeredHierarchyGuid = hierarchyGuid;
            _skippedEmptyNodeCount = 0;

            if (_pendingNodes.Count == 0)
            {
                LogStateOnce(
                    $"completed-empty:{hierarchyGuid}",
                    $"VoxelWorldHierarchy '{hierarchy.name}' produced no renderable nodes.");
                return;
            }

            for (int i = 0; i < _pendingNodes.Count; i++)
            {
                PendingRenderableNode pendingNode = _pendingNodes[i];

                try
                {
                    VoxelEngineRenderInstanceHandle handle = backend.AddInstance(
                        pendingNode.ModelReference,
                        pendingNode.PaletteReference,
                        pendingNode.LocalToWorld);
                    _activeHandles.Add(handle);
                }
                catch (InvalidOperationException exception) when (IsEmptyModelException(exception))
                {
                    _skippedEmptyNodeCount++;
                    Debug.LogWarning(
                        $"[{nameof(VoxelWorldHierarchyRenderer)}] Skipped node '{pendingNode.NodeName}' ({pendingNode.NodeIndex}) because model '{pendingNode.ModelReference.AssetGUID}' contains no active AABBs.",
                        this);
                }
            }
        }

        private void PreparePendingNodes(VoxelWorldHierarchy hierarchy)
        {
            _pendingNodes.Clear();

            Matrix4x4 rootLocalToWorld = transform.localToWorldMatrix;
            int[] rootNodeIndices = hierarchy.RootNodeIndices;
            for (int i = 0; i < rootNodeIndices.Length; i++)
            {
                AppendNodeRecursive(hierarchy, rootNodeIndices[i], rootLocalToWorld, ancestorHidden: false);
            }

            LogStateOnce(
                $"prepared:{GetHierarchyGuid()}:{_pendingNodes.Count}",
                $"Prepared {_pendingNodes.Count} renderable nodes from hierarchy '{hierarchy.name}'.");
        }

        private void AppendNodeRecursive(VoxelWorldHierarchy hierarchy, int nodeIndex, Matrix4x4 parentLocalToWorld, bool ancestorHidden)
        {
            if (!hierarchy.TryGetNode(nodeIndex, out VoxelWorldHierarchyNode node))
            {
                return;
            }

            bool isHidden = ancestorHidden || node.Hidden;
            Matrix4x4 nodeLocalMatrix = Matrix4x4.TRS(node.LocalPosition, node.LocalRotation, node.LocalScale);
            Matrix4x4 nodeLocalToWorld = parentLocalToWorld * nodeLocalMatrix;

            if (!isHidden && node.HasRenderableContent)
            {
                Matrix4x4 renderLocalToWorld = nodeLocalToWorld * Matrix4x4.Translate(node.RenderLocalOffset);
                _pendingNodes.Add(new PendingRenderableNode(
                    nodeIndex,
                    node.Name,
                    node.ModelReference,
                    node.PaletteReference,
                    renderLocalToWorld));
            }

            int[] childIndices = node.ChildIndices;
            for (int i = 0; i < childIndices.Length; i++)
            {
                AppendNodeRecursive(hierarchy, childIndices[i], nodeLocalToWorld, isHidden);
            }
        }

        private void ReleaseRegistration(bool logRelease = true)
        {
            VoxelEngineRenderBackend releasedBackend = _registeredBackend;
            int releasedCount = _activeHandles.Count;
            string releasedHierarchyName = _loadedHierarchyHandle.IsValid() && _loadedHierarchyHandle.Status == AsyncOperationStatus.Succeeded && _loadedHierarchyHandle.Result != null
                ? _loadedHierarchyHandle.Result.name
                : string.Empty;
            bool hadRegistration = releasedBackend != null && releasedCount > 0;

            if (releasedBackend != null)
            {
                for (int i = 0; i < _activeHandles.Count; i++)
                {
                    try
                    {
                        releasedBackend.RemoveInstance(_activeHandles[i]);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                }
            }

            _pendingNodes.Clear();
            _activeHandles.Clear();
            _registeredBackend = null;
            _registeredHierarchyGuid = string.Empty;
            _skippedEmptyNodeCount = 0;

            if (logRelease && hadRegistration)
            {
                LogStateOnce(
                    $"released:{releasedHierarchyName}:{releasedCount}",
                    $"Released {releasedCount} loaded voxel instances for hierarchy '{releasedHierarchyName}'.");
            }
        }

        private bool IsSameFailure(VoxelEngineRenderBackend backend, string hierarchyGuid)
        {
            return _hasRegistrationFailure &&
                ReferenceEquals(_lastFailedBackend, backend) &&
                StringComparer.Ordinal.Equals(_lastFailedHierarchyGuid, hierarchyGuid);
        }

        private void RecordRegistrationFailure(VoxelEngineRenderBackend backend, string hierarchyGuid)
        {
            _lastFailedBackend = backend;
            _lastFailedHierarchyGuid = hierarchyGuid ?? string.Empty;
            _hasRegistrationFailure = true;
        }

        private void ClearRegistrationFailure()
        {
            _lastFailedBackend = null;
            _lastFailedHierarchyGuid = string.Empty;
            _hasRegistrationFailure = false;
        }

        private void LogStateOnce(string stateKey, string message)
        {
            if (StringComparer.Ordinal.Equals(_lastLoggedState, stateKey))
            {
                return;
            }

            _lastLoggedState = stateKey;
            Debug.Log($"[{nameof(VoxelWorldHierarchyRenderer)}] {message}", this);
        }

        private static bool IsEmptyModelException(InvalidOperationException exception)
        {
            return exception != null &&
                StringComparer.Ordinal.Equals(exception.Message, VoxelEngineRenderBackend.EmptyModelRtasErrorMessage);
        }

        private bool HasValidHierarchyReference()
        {
            return _hierarchyAsset != null && _hierarchyAsset.RuntimeKeyIsValid();
        }

        private string GetHierarchyGuid()
        {
            return _hierarchyAsset == null ? string.Empty : _hierarchyAsset.AssetGUID ?? string.Empty;
        }

        private bool TryEnsureHierarchyLoaded(string hierarchyGuid, out VoxelWorldHierarchy hierarchy)
        {
            if (!HasValidHierarchyReference())
            {
                hierarchy = null;
                return false;
            }

            if (!_loadedHierarchyHandle.IsValid())
            {
                _loadedHierarchyHandle = Addressables.LoadAssetAsync<VoxelWorldHierarchy>(_hierarchyAsset);
                _loadedHierarchyGuid = hierarchyGuid;
            }

            if (!_loadedHierarchyHandle.IsDone)
            {
                LogStateOnce(
                    $"loading-hierarchy:{hierarchyGuid}",
                    $"Loading hierarchy asset '{hierarchyGuid}' from Addressables.");
                hierarchy = null;
                return false;
            }

            if (_loadedHierarchyHandle.Status != AsyncOperationStatus.Succeeded || _loadedHierarchyHandle.Result == null)
            {
                throw new InvalidOperationException(
                    $"Failed to load VoxelWorldHierarchy from Addressables GUID '{hierarchyGuid}'.");
            }

            hierarchy = _loadedHierarchyHandle.Result;
            return true;
        }

        private void ReleaseLoadedHierarchy(bool logRelease = true)
        {
            if (!_loadedHierarchyHandle.IsValid())
            {
                _loadedHierarchyGuid = string.Empty;
                return;
            }

            string hierarchyName = _loadedHierarchyHandle.Status == AsyncOperationStatus.Succeeded && _loadedHierarchyHandle.Result != null
                ? _loadedHierarchyHandle.Result.name
                : _loadedHierarchyGuid;

            Addressables.Release(_loadedHierarchyHandle);
            _loadedHierarchyHandle = default;
            _loadedHierarchyGuid = string.Empty;

            if (logRelease)
            {
                LogStateOnce(
                    $"released-hierarchy:{hierarchyName}",
                    $"Released hierarchy asset '{hierarchyName}'.");
            }
        }

        private static VoxelEngineRenderBackend TryGetCurrentBackend()
        {
            try
            {
                if (!(RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline))
                {
                    return null;
                }

                return renderPipeline.RenderBackend;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        private readonly struct PendingRenderableNode
        {
            public PendingRenderableNode(
                int nodeIndex,
                string nodeName,
                Data.Voxel.AssetReferenceVoxelModel modelReference,
                Data.Voxel.AssetReferenceVoxelPalette paletteReference,
                Matrix4x4 localToWorld)
            {
                NodeIndex = nodeIndex;
                NodeName = nodeName ?? string.Empty;
                ModelReference = modelReference ?? throw new ArgumentNullException(nameof(modelReference));
                PaletteReference = paletteReference ?? throw new ArgumentNullException(nameof(paletteReference));
                LocalToWorld = localToWorld;
            }

            public int NodeIndex { get; }

            public string NodeName { get; }

            public Data.Voxel.AssetReferenceVoxelModel ModelReference { get; }

            public Data.Voxel.AssetReferenceVoxelPalette PaletteReference { get; }

            public Matrix4x4 LocalToWorld { get; }
        }
    }
}
