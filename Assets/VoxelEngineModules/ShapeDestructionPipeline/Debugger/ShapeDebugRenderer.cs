using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngineModules.Shape.Debugger
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Shape/Shape Debug Renderer")]
    public sealed class ShapeDebugRenderer : MonoBehaviour
    {
        [SerializeField] private float _voxelSize = 1.0f;
        [SerializeField] private bool _syncInLateUpdate = true;

        private readonly Dictionary<int, Entry> _entriesByShapeHandle = new Dictionary<int, Entry>();
        private readonly HashSet<int> _dirtyShapeHandles = new HashSet<int>();
        private readonly ShapeRenderBackendBridge _renderBridge = new ShapeRenderBackendBridge();
        private ShapeDataStorage _storage;
        private object _registeredBackend;
        private string _lastWarningKey;

        public float VoxelSize
        {
            get => _voxelSize;
            set => _voxelSize = Mathf.Max(0.000001f, float.IsFinite(value) ? value : 1.0f);
        }

        public void Bind(ShapeDataStorage storage)
        {
            if (_storage == storage)
            {
                return;
            }

            ReleaseAllRegistrations();
            _storage = storage;
            MarkAllDirty();
        }

        public void RegisterOrRefreshShape(int shapeHandle, Color32 color)
        {
            if (!_entriesByShapeHandle.TryGetValue(shapeHandle, out Entry entry))
            {
                entry = new Entry(color, hasCustomTransform: false, Matrix4x4.identity);
                _entriesByShapeHandle.Add(shapeHandle, entry);
            }

            entry.Color = color;
            entry.HasCustomTransform = false;
            _dirtyShapeHandles.Add(shapeHandle);
        }

        public void RegisterOrRefreshShape(
            int shapeHandle,
            Color32 color,
            Matrix4x4 shapeLocalToWorld)
        {
            if (!_entriesByShapeHandle.TryGetValue(shapeHandle, out Entry entry))
            {
                entry = new Entry(color, hasCustomTransform: true, shapeLocalToWorld);
                _entriesByShapeHandle.Add(shapeHandle, entry);
            }

            entry.Color = color;
            entry.HasCustomTransform = true;
            entry.ShapeLocalToWorld = shapeLocalToWorld;
            _dirtyShapeHandles.Add(shapeHandle);
        }

        public void UpdateShapeTransform(int shapeHandle, Matrix4x4 shapeLocalToWorld)
        {
            if (!_entriesByShapeHandle.TryGetValue(shapeHandle, out Entry entry))
            {
                return;
            }

            entry.HasCustomTransform = true;
            entry.ShapeLocalToWorld = shapeLocalToWorld;
        }

        public void UnregisterShape(int shapeHandle)
        {
            if (!_entriesByShapeHandle.TryGetValue(shapeHandle, out Entry entry))
            {
                return;
            }

            RemoveEntryRegistration(entry);
            _entriesByShapeHandle.Remove(shapeHandle);
            _dirtyShapeHandles.Remove(shapeHandle);
        }

        public void Clear()
        {
            ReleaseAllRegistrations();
            _entriesByShapeHandle.Clear();
            _dirtyShapeHandles.Clear();
        }

        public void SyncRenderBackend()
        {
            if (_storage == null || !_storage.IsCreated)
            {
                return;
            }

            if (!_renderBridge.TryGetBackend(out object backend, out string error))
            {
                LogWarningOnce(error);
                return;
            }

            if (!ReferenceEquals(_registeredBackend, backend))
            {
                ReleaseAllRegistrations();
                _registeredBackend = backend;
                MarkAllDirty();
            }

            if (_dirtyShapeHandles.Count == 0)
            {
                UpdateRegisteredTransforms();
                return;
            }

            int[] dirtyShapeHandles = new int[_dirtyShapeHandles.Count];
            _dirtyShapeHandles.CopyTo(dirtyShapeHandles);
            _dirtyShapeHandles.Clear();

            for (int i = 0; i < dirtyShapeHandles.Length; i++)
            {
                int shapeHandle = dirtyShapeHandles[i];
                if (!_entriesByShapeHandle.TryGetValue(shapeHandle, out Entry entry))
                {
                    continue;
                }

                RemoveEntryRegistration(entry);
                entry.Version++;

                Matrix4x4 localToWorld = BuildRenderLocalToWorld(entry);
                string modelKey = $"ShapeDebugModel:{GetInstanceID()}:{shapeHandle}:{entry.Version}";
                string paletteKey = $"ShapeDebugPalette:{GetInstanceID()}:{shapeHandle}:{entry.Version}";
                if (!_renderBridge.TryAddShapeInstance(
                        backend,
                        _storage,
                        shapeHandle,
                        entry.Color,
                        localToWorld,
                        modelKey,
                        paletteKey,
                        out object renderHandle,
                        out string addError))
                {
                    entry.RenderHandle = null;
                    LogWarningOnce(addError);
                    continue;
                }

                entry.RenderHandle = renderHandle;
                entry.RegisteredLocalToWorld = localToWorld;
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !_syncInLateUpdate)
            {
                return;
            }

            SyncRenderBackend();
        }

        private void OnDisable()
        {
            ReleaseAllRegistrations();
        }

        private void OnDestroy()
        {
            ReleaseAllRegistrations();
        }

        private void OnValidate()
        {
            VoxelSize = _voxelSize;
        }


        #region Helpers

        private Matrix4x4 BuildRenderLocalToWorld(Entry entry)
        {
            Matrix4x4 shapeLocalToWorld = entry.HasCustomTransform
                ? entry.ShapeLocalToWorld
                : transform.localToWorldMatrix;
            return shapeLocalToWorld * Matrix4x4.Scale(Vector3.one * VoxelSize);
        }

        private void UpdateRegisteredTransforms()
        {
            if (_registeredBackend == null)
            {
                return;
            }

            foreach (Entry entry in _entriesByShapeHandle.Values)
            {
                if (entry.RenderHandle == null)
                {
                    continue;
                }

                Matrix4x4 localToWorld = BuildRenderLocalToWorld(entry);
                if (MatrixEquals(entry.RegisteredLocalToWorld, localToWorld))
                {
                    continue;
                }

                _renderBridge.UpdateTransform(_registeredBackend, entry.RenderHandle, localToWorld);
                entry.RegisteredLocalToWorld = localToWorld;
            }
        }

        private void ReleaseAllRegistrations()
        {
            foreach (Entry entry in _entriesByShapeHandle.Values)
            {
                RemoveEntryRegistration(entry);
            }

            _registeredBackend = null;
        }

        private void RemoveEntryRegistration(Entry entry)
        {
            if (_registeredBackend == null || entry.RenderHandle == null)
            {
                entry.RenderHandle = null;
                return;
            }

            _renderBridge.RemoveInstance(_registeredBackend, entry.RenderHandle);
            entry.RenderHandle = null;
        }

        private void MarkAllDirty()
        {
            foreach (int shapeHandle in _entriesByShapeHandle.Keys)
            {
                _dirtyShapeHandles.Add(shapeHandle);
            }
        }

        private void LogWarningOnce(string message)
        {
            if (string.IsNullOrEmpty(message) || _lastWarningKey == message)
            {
                return;
            }

            _lastWarningKey = message;
            Debug.LogWarning(message, this);
        }

        private static bool MatrixEquals(Matrix4x4 left, Matrix4x4 right)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!Mathf.Approximately(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion


        private sealed class Entry
        {
            public Entry(
                Color32 color,
                bool hasCustomTransform,
                Matrix4x4 shapeLocalToWorld)
            {
                Color = color;
                HasCustomTransform = hasCustomTransform;
                ShapeLocalToWorld = shapeLocalToWorld;
            }

            public Color32 Color;
            public object RenderHandle;
            public int Version;
            public bool HasCustomTransform;
            public Matrix4x4 ShapeLocalToWorld;
            public Matrix4x4 RegisteredLocalToWorld;
        }
    }
}
