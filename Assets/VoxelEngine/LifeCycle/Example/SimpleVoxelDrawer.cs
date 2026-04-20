using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Data.Voxel;
using VoxelEngine.Render.RenderBackend;
using VoxelEngine.Render.RenderPipeline;

namespace VoxelEngine.LifeCycle.Example
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/LifeCycle/Example/Simple Voxel Drawer")]
    public sealed class SimpleVoxelDrawer : MonoBehaviour
    {
        [SerializeField] private AssetReferenceVoxelModel _modelAsset;
        [SerializeField] private AssetReferenceVoxelPalette _paletteAsset;

        private VoxelEngineRenderBackend _registeredBackend;
        private VoxelEngineRenderInstanceHandle _instanceHandle;
        private string _registeredModelGuid = string.Empty;
        private string _registeredPaletteGuid = string.Empty;
        private VoxelEngineRenderBackend _lastFailedBackend;
        private string _lastFailedModelGuid = string.Empty;
        private string _lastFailedPaletteGuid = string.Empty;
        private string _lastLoggedState = string.Empty;
        private bool _hasRegistrationFailure;

        private void OnEnable()
        {
            TryRefreshRegistration();
        }

        private void Update()
        {
            TryRefreshRegistration();
        }

        private void OnDisable()
        {
            ReleaseRegistration();
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
            string modelGuid = GetAssetGuid(_modelAsset);
            string paletteGuid = GetAssetGuid(_paletteAsset);

            if (!HasValidAssetReferences())
            {
                LogStateOnce("missing-assets", "Waiting for both VoxelModelAsset and VoxelPaletteAsset references before registering.");
                ClearRegistrationFailure();
                ReleaseRegistration();
                return;
            }

            VoxelEngineRenderBackend currentBackend = TryGetCurrentBackend();
            if (currentBackend == null)
            {
                LogStateOnce("missing-backend", "Waiting for an active VoxelEngineRenderPipeline before registering.");
                ReleaseRegistration();
                return;
            }

            bool backendChanged = _registeredBackend != null && !ReferenceEquals(_registeredBackend, currentBackend);
            bool assetsChanged = _instanceHandle.IsValid && !IsRegisteredWith(modelGuid, paletteGuid);

            if (backendChanged || assetsChanged)
            {
                ReleaseRegistration();
            }

            if (_instanceHandle.IsValid)
            {
                ClearRegistrationFailure();
                LogStateOnce(
                    $"registered:{_registeredModelGuid}:{_registeredPaletteGuid}:{_instanceHandle.Value}",
                    $"Registered voxel instance {_instanceHandle.Value} with model '{_registeredModelGuid}' and palette '{_registeredPaletteGuid}'.");
                return;
            }

            if (IsSameFailure(currentBackend, modelGuid, paletteGuid))
            {
                return;
            }

            try
            {
                VoxelEngineRenderInstanceHandle instanceHandle = currentBackend.AddInstance(
                    _modelAsset,
                    _paletteAsset,
                    transform.localToWorldMatrix);
                _registeredBackend = currentBackend;
                _instanceHandle = instanceHandle;
                _registeredModelGuid = modelGuid;
                _registeredPaletteGuid = paletteGuid;
                ClearRegistrationFailure();
                LogStateOnce(
                    $"registered:{_registeredModelGuid}:{_registeredPaletteGuid}:{_instanceHandle.Value}",
                    $"Registered voxel instance {_instanceHandle.Value} with model '{_registeredModelGuid}' and palette '{_registeredPaletteGuid}'.");
            }
            catch (Exception exception)
            {
                RecordRegistrationFailure(currentBackend, modelGuid, paletteGuid);
                LogStateOnce(
                    $"registration-failed:{modelGuid}:{paletteGuid}",
                    $"Failed to register voxel instance for model '{modelGuid}' and palette '{paletteGuid}'.");
                Debug.LogException(exception, this);
            }
        }

        private void ReleaseRegistration()
        {
            VoxelEngineRenderInstanceHandle releasedHandle = _instanceHandle;
            string releasedModelGuid = _registeredModelGuid;
            string releasedPaletteGuid = _registeredPaletteGuid;
            bool hadRegistration = _registeredBackend != null && _instanceHandle.IsValid;

            if (_registeredBackend != null && _instanceHandle.IsValid)
            {
                try
                {
                    _registeredBackend.RemoveInstance(_instanceHandle);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (KeyNotFoundException)
                {
                }
            }

            _registeredBackend = null;
            _instanceHandle = default;
            _registeredModelGuid = string.Empty;
            _registeredPaletteGuid = string.Empty;

            if (hadRegistration)
            {
                LogStateOnce(
                    $"released:{releasedHandle.Value}:{releasedModelGuid}:{releasedPaletteGuid}",
                    $"Released voxel instance {releasedHandle.Value} for model '{releasedModelGuid}' and palette '{releasedPaletteGuid}'.");
            }
        }

        private bool HasValidAssetReferences()
        {
            return _modelAsset != null
                && _paletteAsset != null
                && _modelAsset.RuntimeKeyIsValid()
                && _paletteAsset.RuntimeKeyIsValid();
        }

        private bool IsRegisteredWith(string modelGuid, string paletteGuid)
        {
            return _instanceHandle.IsValid
                && StringComparer.Ordinal.Equals(_registeredModelGuid, modelGuid)
                && StringComparer.Ordinal.Equals(_registeredPaletteGuid, paletteGuid);
        }

        private bool IsSameFailure(VoxelEngineRenderBackend backend, string modelGuid, string paletteGuid)
        {
            return _hasRegistrationFailure
                && ReferenceEquals(_lastFailedBackend, backend)
                && StringComparer.Ordinal.Equals(_lastFailedModelGuid, modelGuid)
                && StringComparer.Ordinal.Equals(_lastFailedPaletteGuid, paletteGuid);
        }

        private void RecordRegistrationFailure(VoxelEngineRenderBackend backend, string modelGuid, string paletteGuid)
        {
            _lastFailedBackend = backend;
            _lastFailedModelGuid = modelGuid;
            _lastFailedPaletteGuid = paletteGuid;
            _hasRegistrationFailure = true;
        }

        private void ClearRegistrationFailure()
        {
            _lastFailedBackend = null;
            _lastFailedModelGuid = string.Empty;
            _lastFailedPaletteGuid = string.Empty;
            _hasRegistrationFailure = false;
        }

        private void LogStateOnce(string stateKey, string message)
        {
            if (StringComparer.Ordinal.Equals(_lastLoggedState, stateKey))
            {
                return;
            }

            _lastLoggedState = stateKey;
            Debug.Log($"[{nameof(SimpleVoxelDrawer)}] {message}", this);
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

        private static string GetAssetGuid(UnityEngine.AddressableAssets.AssetReference assetReference)
        {
            return assetReference?.AssetGUID ?? string.Empty;
        }
    }
}
