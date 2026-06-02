using UnityEngine;

namespace VoxelEngine.Physics.Destruction
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Physics/Destruction Debug Object Binding")]
    public sealed class DestructionDebugObjectBinding : MonoBehaviour
    {
        [SerializeField] private DestructionDebugRenderer _renderer;
        [SerializeField] private int _objectRuntimeId = 1;

        public DestructionDebugRenderer Renderer
        {
            get => _renderer;
            set => _renderer = value;
        }

        public int ObjectRuntimeId
        {
            get => _objectRuntimeId;
            set => _objectRuntimeId = value;
        }

        public void Bind(DestructionDebugRenderer renderer, DestructionDebugObject debugObject)
        {
            _renderer = renderer;
            _objectRuntimeId = debugObject != null ? debugObject.RuntimeId : 0;
        }

        public bool TryResolve(
            out DestructionDebugRenderer renderer,
            out DestructionDebugObject debugObject)
        {
            renderer = ResolveRenderer();
            if (renderer == null)
            {
                debugObject = null;
                return false;
            }

            if (_objectRuntimeId > 0 && renderer.TryGetObject(_objectRuntimeId, out debugObject))
            {
                return true;
            }

            if (renderer.Objects.Count == 1)
            {
                debugObject = renderer.Objects[0];
                if (debugObject != null)
                {
                    _objectRuntimeId = debugObject.RuntimeId;
                    return true;
                }
            }

            debugObject = null;
            return false;
        }

        private void Reset()
        {
            _renderer = GetComponentInParent<DestructionDebugRenderer>();
        }

        private void OnValidate()
        {
            if (_renderer == null)
            {
                _renderer = GetComponentInParent<DestructionDebugRenderer>();
            }
        }

        private DestructionDebugRenderer ResolveRenderer()
        {
            if (_renderer != null)
            {
                return _renderer;
            }

            _renderer = GetComponentInParent<DestructionDebugRenderer>();
            return _renderer;
        }
    }
}
