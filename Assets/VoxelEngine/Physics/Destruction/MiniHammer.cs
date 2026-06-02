using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VoxelEngine.Physics.Destruction
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Physics/Mini Hammer")]
    public sealed class MiniHammer : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private LayerMask _layerMask = UnityEngine.Physics.DefaultRaycastLayers;
        [SerializeField] private QueryTriggerInteraction _queryTriggerInteraction = QueryTriggerInteraction.Ignore;
        [SerializeField] private float _maxRayDistance = 200.0f;
        [SerializeField] private float _radiusInVoxels = 2.0f;
        [SerializeField] private float _maxDdaDistanceInVoxels = 512.0f;
        [SerializeField] private int _maxDdaSteps = 4096;
        [SerializeField] private bool _useCameraCenter = true;
        [SerializeField] private bool _syncImmediately;

        private void Awake()
        {
            ResolveCamera();
            SanitizeSerializedState();
        }

        private void Update()
        {
            if (!Application.isPlaying ||
                Mouse.current == null ||
                !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Strike();
        }

        private void OnValidate()
        {
            SanitizeSerializedState();
        }

        public bool Strike()
        {
            Camera rayCamera = ResolveCamera();
            if (rayCamera == null || Mouse.current == null)
            {
                return false;
            }

            Ray ray = _useCameraCenter
                ? rayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f))
                : rayCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            return Strike(ray);
        }

        public bool Strike(Ray ray)
        {
            RaycastHit[] hits = UnityEngine.Physics.RaycastAll(
                ray,
                _maxRayDistance,
                _layerMask,
                _queryTriggerInteraction);
            if (hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, CompareRaycastHits);
            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                if (TryStrikeHit(ray, hits[hitIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryStrikeHit(Ray ray, RaycastHit hit)
        {
            BoxCollider boxCollider = hit.collider as BoxCollider;
            if (boxCollider == null)
            {
                return false;
            }

            DestructionDebugObjectBinding binding = boxCollider.GetComponent<DestructionDebugObjectBinding>();
            if (binding == null)
            {
                binding = boxCollider.GetComponentInParent<DestructionDebugObjectBinding>();
            }

            if (binding == null ||
                !binding.TryResolve(out DestructionDebugRenderer renderer, out DestructionDebugObject debugObject))
            {
                return false;
            }

            if (!TryFindVoxelHit(renderer, debugObject, ray, hit, out int3 hitVoxel))
            {
                return false;
            }

            float3 removalCenter = new float3(hitVoxel.x + 0.5f, hitVoxel.y + 0.5f, hitVoxel.z + 0.5f);
            int removedCount = renderer.RemoveVoxelsInRadius(debugObject, removalCenter, _radiusInVoxels);
            if (removedCount <= 0)
            {
                return false;
            }

            if (_syncImmediately)
            {
                renderer.SyncRenderBackend();
            }

            return true;
        }

        private bool TryFindVoxelHit(
            DestructionDebugRenderer renderer,
            DestructionDebugObject debugObject,
            Ray ray,
            RaycastHit colliderHit,
            out int3 hitVoxel)
        {
            float voxelSize = Mathf.Max(0.000001f, renderer.VoxelSize);
            Vector3 rayDirection = ray.direction.normalized;
            Vector3 startWorld = colliderHit.point + (rayDirection * voxelSize * 0.001f);
            float3 origin = renderer.WorldToVoxelGridPoint(startWorld);
            float3 direction = renderer.WorldToVoxelGridDirection(rayDirection);
            if (math.lengthsq(direction) <= 0.000000000001f)
            {
                hitVoxel = default;
                return false;
            }

            return TryDdaFirstOccupiedVoxel(
                debugObject,
                origin,
                math.normalize(direction),
                _maxDdaSteps,
                _maxDdaDistanceInVoxels,
                out hitVoxel);
        }

        private static bool TryDdaFirstOccupiedVoxel(
            DestructionDebugObject debugObject,
            float3 origin,
            float3 direction,
            int maxSteps,
            float maxDistanceInVoxels,
            out int3 hitVoxel)
        {
            int3 voxel = FloorToInt3(origin);
            int3 step = new int3(
                DirectionStep(direction.x),
                DirectionStep(direction.y),
                DirectionStep(direction.z));

            float3 tMax = new float3(
                InitialBoundaryDistance(origin.x, voxel.x, direction.x),
                InitialBoundaryDistance(origin.y, voxel.y, direction.y),
                InitialBoundaryDistance(origin.z, voxel.z, direction.z));
            float3 tDelta = new float3(
                StepDistance(direction.x),
                StepDistance(direction.y),
                StepDistance(direction.z));

            float travelledDistance = 0.0f;
            int sanitizedMaxSteps = math.max(1, maxSteps);
            float sanitizedMaxDistance = math.max(0.0f, maxDistanceInVoxels);

            for (int stepIndex = 0; stepIndex < sanitizedMaxSteps && travelledDistance <= sanitizedMaxDistance; stepIndex++)
            {
                if (debugObject.TryGetVoxel(voxel, out _))
                {
                    hitVoxel = voxel;
                    return true;
                }

                if (tMax.x <= tMax.y && tMax.x <= tMax.z)
                {
                    voxel.x += step.x;
                    travelledDistance = tMax.x;
                    tMax.x += tDelta.x;
                }
                else if (tMax.y <= tMax.z)
                {
                    voxel.y += step.y;
                    travelledDistance = tMax.y;
                    tMax.y += tDelta.y;
                }
                else
                {
                    voxel.z += step.z;
                    travelledDistance = tMax.z;
                    tMax.z += tDelta.z;
                }
            }

            hitVoxel = default;
            return false;
        }

        private Camera ResolveCamera()
        {
            if (_camera != null)
            {
                return _camera;
            }

            _camera = GetComponent<Camera>();
            if (_camera != null)
            {
                return _camera;
            }

            _camera = Camera.main;
            return _camera;
        }

        private void SanitizeSerializedState()
        {
            _maxRayDistance = Mathf.Max(0.0f, float.IsFinite(_maxRayDistance) ? _maxRayDistance : 200.0f);
            _radiusInVoxels = Mathf.Max(0.0f, float.IsFinite(_radiusInVoxels) ? _radiusInVoxels : 2.0f);
            _maxDdaDistanceInVoxels = Mathf.Max(
                0.0f,
                float.IsFinite(_maxDdaDistanceInVoxels) ? _maxDdaDistanceInVoxels : 512.0f);
            _maxDdaSteps = Mathf.Max(1, _maxDdaSteps);
        }

        private static int3 FloorToInt3(float3 value)
        {
            return new int3(
                (int)math.floor(value.x),
                (int)math.floor(value.y),
                (int)math.floor(value.z));
        }

        private static int DirectionStep(float value)
        {
            if (value > 0.0f)
            {
                return 1;
            }

            return value < 0.0f ? -1 : 0;
        }

        private static float InitialBoundaryDistance(float origin, int voxel, float direction)
        {
            if (direction > 0.0f)
            {
                return ((voxel + 1.0f) - origin) / direction;
            }

            if (direction < 0.0f)
            {
                return (origin - voxel) / -direction;
            }

            return float.PositiveInfinity;
        }

        private static float StepDistance(float direction)
        {
            return direction == 0.0f ? float.PositiveInfinity : math.abs(1.0f / direction);
        }

        private static int CompareRaycastHits(RaycastHit left, RaycastHit right)
        {
            return left.distance.CompareTo(right.distance);
        }
    }
}
