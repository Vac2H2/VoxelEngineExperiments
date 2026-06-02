using UnityEngine;

namespace VoxelEngine.Physics.DebugRenderer
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Physics/Mini Physics Global Voxel Size Position")]
    public sealed class MiniPhysicsGlobalVoxelSizePosition : MonoBehaviour
    {
        [SerializeField] private Vector3 _worldPositionAtVoxelSizeOne;

        private float _lastAppliedVoxelSize = -1.0f;

        public Vector3 WorldPositionAtVoxelSizeOne
        {
            get => _worldPositionAtVoxelSizeOne;
            set
            {
                _worldPositionAtVoxelSizeOne = value;
                Apply();
            }
        }

        private void Reset()
        {
            float voxelSize = VoxelEngineSettings.SanitizeVoxelSize(VoxelEngineSettings.GlobalVoxelSize);
            _worldPositionAtVoxelSizeOne = transform.position / voxelSize;
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                return;
            }

            float voxelSize = VoxelEngineSettings.SanitizeVoxelSize(VoxelEngineSettings.GlobalVoxelSize);
            if (!Mathf.Approximately(_lastAppliedVoxelSize, voxelSize))
            {
                Apply(voxelSize);
            }
        }

        public void Apply()
        {
            Apply(VoxelEngineSettings.GlobalVoxelSize);
        }

        private void Apply(float voxelSize)
        {
            float sanitizedVoxelSize = VoxelEngineSettings.SanitizeVoxelSize(voxelSize);
            transform.position = _worldPositionAtVoxelSizeOne * sanitizedVoxelSize;
            transform.localScale = Vector3.one;
            _lastAppliedVoxelSize = sanitizedVoxelSize;
        }
    }
}
