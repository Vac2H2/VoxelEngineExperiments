using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.Lighting
{
    public enum VoxelLocalLightShape
    {
        Point = 0,
        Spot = 1,
        Sphere = 2,
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelExperiments/Lighting/Voxel Local Light")]
    public sealed class VoxelLocalLight : MonoBehaviour
    {
        [SerializeField] private VoxelLocalLightShape _shape = VoxelLocalLightShape.Point;
        [SerializeField] private Color _color = Color.white;
        [SerializeField, Min(0.0f)] private float _intensity = 20.0f;
        [SerializeField, Min(0.0f)] private float _range = 12.0f;
        [SerializeField] private bool _castsShadows = true;
        [SerializeField, Range(0.0f, 179.0f)] private float _spotAngle = 45.0f;
        [SerializeField, Range(0.0f, 179.0f)] private float _innerSpotAngle = 30.0f;
        [SerializeField, Min(0.0f)] private float _sphereRadius = 0.5f;

        public VoxelLocalLightShape Shape => _shape;

        public Color Color => _color;

        public float Intensity => Mathf.Max(_intensity, 0.0f);

        public float Range => Mathf.Max(_range, 0.0f);

        public bool CastsShadows => _castsShadows;

        public float SpotAngle => Mathf.Clamp(_spotAngle, 0.0f, 179.0f);

        public float InnerSpotAngle => Mathf.Clamp(_innerSpotAngle, 0.0f, SpotAngle);

        public float SphereRadius => Mathf.Max(_sphereRadius, 0.0f);

        public Vector3 Position => transform.position;

        public Vector3 Forward => transform.forward;

        public Bounds GetInfluenceBounds()
        {
            float extent = Range;
            if (_shape == VoxelLocalLightShape.Sphere)
            {
                extent += SphereRadius;
            }

            return new Bounds(Position, Vector3.one * (extent * 2.0f));
        }

        private void Reset()
        {
            _shape = VoxelLocalLightShape.Point;
            _color = Color.white;
            _intensity = 20.0f;
            _range = 12.0f;
            _castsShadows = true;
            _spotAngle = 45.0f;
            _innerSpotAngle = 30.0f;
            _sphereRadius = 0.5f;
        }

        private void OnValidate()
        {
            _intensity = Mathf.Max(_intensity, 0.0f);
            _range = Mathf.Max(_range, 0.0f);
            _sphereRadius = Mathf.Max(_sphereRadius, 0.0f);
            _spotAngle = Mathf.Clamp(_spotAngle, 0.0f, 179.0f);
            _innerSpotAngle = Mathf.Clamp(_innerSpotAngle, 0.0f, _spotAngle);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.identity;

            Color gizmoColor = _color;
            gizmoColor.a = 0.8f;
            Gizmos.color = gizmoColor;

            Vector3 position = Position;
            switch (_shape)
            {
                case VoxelLocalLightShape.Sphere:
                    Gizmos.DrawWireSphere(position, SphereRadius);
                    Gizmos.DrawWireSphere(position, Range + SphereRadius);
                    break;

                default:
                    Gizmos.DrawWireSphere(position, Range);
                    break;
            }
        }
    }
}
