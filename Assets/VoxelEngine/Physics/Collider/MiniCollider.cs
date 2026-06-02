using UnityEngine;
using VoxelEngine.Physics.Body;
using VoxelEngine.Physics.World;

namespace VoxelEngine.Physics.Collider
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Physics/Mini Collider")]
    public sealed class MiniCollider : MonoBehaviour
    {
        [SerializeField] private MiniColliderData _data = new MiniColliderData();

        private MiniColliderHandle _handle;
        private MiniRigidBody _attachedBody;

        public MiniColliderData Data => _data ??= new MiniColliderData();

        public MiniColliderHandle Handle => _handle;

        public bool IsRegistered => _handle.IsValid;

        public MiniRigidBody AttachedBody
        {
            get
            {
                if (_attachedBody == null)
                {
                    RefreshAttachedBody();
                }

                return _attachedBody;
            }
        }

        private void Awake()
        {
            Data.Normalize();
            RefreshAttachedBody();
            _handle = MiniPhysicsWorld.Default.RegisterCollider(this);
        }

        private void OnTransformParentChanged()
        {
            RefreshAttachedBody();
        }

        private void OnDestroy()
        {
            if (!_handle.IsValid)
            {
                return;
            }

            MiniPhysicsWorld.Default.UnregisterCollider(_handle);
            _handle = default;
        }

        private void OnValidate()
        {
            Data.Normalize();
            RefreshAttachedBody();
        }

        public void RefreshAttachedBody()
        {
            _attachedBody = GetComponentInParent<MiniRigidBody>();
        }
    }
}
