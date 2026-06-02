using UnityEngine;
using UnityEngine.Serialization;
using VoxelEngine.Physics.World;

namespace VoxelEngine.Physics.Simulation
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Physics/Mini Physics Broadphase Debug Runner")]
    public sealed class MiniPhysicsBroadphaseDebugRunner : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("_tickInUpdate")] private bool _tickInFixedUpdate = true;
        [SerializeField] private bool _drawColliderBounds = true;
        [SerializeField, FormerlySerializedAs("_drawChunkPairs")] private bool _drawColliderPairs = true;
        [SerializeField] private bool _drawContactPoints = true;
        [SerializeField] private bool _drawContactNormals = true;
        [SerializeField] private bool _logPairCountChanges = true;
        [SerializeField] private Color _colliderBoundsColor = new Color(1.0f, 1.0f, 1.0f, 0.35f);
        [SerializeField, FormerlySerializedAs("_chunkPairAColor")] private Color _colliderPairAColor = new Color(1.0f, 0.85f, 0.1f, 1.0f);
        [SerializeField, FormerlySerializedAs("_chunkPairBColor")] private Color _colliderPairBColor = new Color(0.1f, 0.75f, 1.0f, 1.0f);
        [SerializeField] private Color _contactPointColor = new Color(1.0f, 0.12f, 0.08f, 1.0f);
        [SerializeField] private Color _contactNormalColor = new Color(0.2f, 1.0f, 0.35f, 1.0f);
        [SerializeField] private float _contactPointRadius = 0.045f;
        [SerializeField] private float _contactNormalLength = 0.35f;
        [SerializeField] private float _contactNormalArrowSize = 0.08f;
        [SerializeField] private int _solverIterations = 10;
        [SerializeField] private float _solverRestitution = 0.0f;
        [SerializeField] private float _solverFriction = 0.6f;
        [SerializeField] private float _solverBaumgarte = 0.08f;
        [SerializeField] private float _solverPenetrationSlop = 0.01f;
        [SerializeField] private float _solverMaxDepenetrationVelocity = 3.0f;
        [SerializeField] private float _solverRestitutionVelocityThreshold = 1.0f;

        private readonly MiniPhysicsEngine _engine = new MiniPhysicsEngine();
        private int _lastLoggedPairCount = -1;

        public MiniPhysicsEngine Engine => _engine;

        public int ColliderPairCount => _engine.FrameData.ColliderPairCount;

        private void FixedUpdate()
        {
            if (!_tickInFixedUpdate)
            {
                return;
            }

            Tick(Time.fixedDeltaTime);
        }

        public void Tick()
        {
            Tick(Time.fixedDeltaTime);
        }

        public void Tick(float deltaTime)
        {
            ApplySolverSettings();
            _engine.Tick(MiniPhysicsWorld.Default, deltaTime);

            if (!_logPairCountChanges || _lastLoggedPairCount == ColliderPairCount)
            {
                return;
            }

            _lastLoggedPairCount = ColliderPairCount;
            Debug.Log($"[{nameof(MiniPhysicsBroadphaseDebugRunner)}] Broadphase collider pairs: {ColliderPairCount}", this);
        }

        private void OnValidate()
        {
            _solverIterations = Mathf.Max(1, _solverIterations);
            _solverRestitution = Mathf.Max(0.0f, _solverRestitution);
            _solverFriction = Mathf.Max(0.0f, _solverFriction);
            _solverBaumgarte = Mathf.Max(0.0f, _solverBaumgarte);
            _solverPenetrationSlop = Mathf.Max(0.0f, _solverPenetrationSlop);
            _solverMaxDepenetrationVelocity = Mathf.Max(0.0f, _solverMaxDepenetrationVelocity);
            _solverRestitutionVelocityThreshold = Mathf.Max(0.0f, _solverRestitutionVelocityThreshold);
        }

        private void ApplySolverSettings()
        {
            _engine.Solver.IterationCount = Mathf.Max(1, _solverIterations);
            _engine.Solver.Restitution = Mathf.Max(0.0f, _solverRestitution);
            _engine.Solver.Friction = Mathf.Max(0.0f, _solverFriction);
            _engine.Solver.Baumgarte = Mathf.Max(0.0f, _solverBaumgarte);
            _engine.Solver.PenetrationSlop = Mathf.Max(0.0f, _solverPenetrationSlop);
            _engine.Solver.MaxDepenetrationVelocity = Mathf.Max(0.0f, _solverMaxDepenetrationVelocity);
            _engine.Solver.RestitutionVelocityThreshold = Mathf.Max(0.0f, _solverRestitutionVelocityThreshold);
        }

        private void OnDrawGizmos()
        {
            MiniPhysicsFrameData frameData = _engine.FrameData;

            if (_drawColliderBounds)
            {
                DrawColliderBounds(frameData);
            }

            if (_drawColliderPairs)
            {
                DrawColliderPairs(frameData);
            }

            if (_drawContactPoints || _drawContactNormals)
            {
                DrawContacts(frameData);
            }
        }

        private void DrawColliderBounds(MiniPhysicsFrameData frameData)
        {
            Gizmos.color = _colliderBoundsColor;
            var colliders = frameData.Colliders;
            for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++)
            {
                MiniColliderFrame collider = colliders[colliderIndex];
                if (!collider.HasWorldBounds)
                {
                    continue;
                }

                Gizmos.DrawWireCube(collider.WorldBounds.center, collider.WorldBounds.size);
            }
        }

        private void DrawColliderPairs(MiniPhysicsFrameData frameData)
        {
            var colliders = frameData.Colliders;
            var colliderPairs = frameData.ColliderPairs;
            for (int pairIndex = 0; pairIndex < colliderPairs.Count; pairIndex++)
            {
                var pair = colliderPairs[pairIndex];
                MiniColliderFrame colliderA = colliders[pair.ColliderAIndex];
                MiniColliderFrame colliderB = colliders[pair.ColliderBIndex];

                Gizmos.color = _colliderPairAColor;
                Gizmos.DrawWireCube(colliderA.WorldBounds.center, colliderA.WorldBounds.size);

                Gizmos.color = _colliderPairBColor;
                Gizmos.DrawWireCube(colliderB.WorldBounds.center, colliderB.WorldBounds.size);
            }
        }

        private void DrawContacts(MiniPhysicsFrameData frameData)
        {
            float pointRadius = Mathf.Max(0.0f, _contactPointRadius);
            float normalLength = Mathf.Max(0.0f, _contactNormalLength);
            float arrowSize = Mathf.Max(0.0f, _contactNormalArrowSize);
            var manifolds = frameData.ContactManifolds;

            for (int manifoldIndex = 0; manifoldIndex < manifolds.Count; manifoldIndex++)
            {
                MiniContactManifoldFrame manifold = manifolds[manifoldIndex];
                for (int pointIndex = 0; pointIndex < manifold.PointCount; pointIndex++)
                {
                    MiniContactPointFrame point = manifold.GetPoint(pointIndex);

                    if (_drawContactPoints && pointRadius > 0.0f)
                    {
                        Gizmos.color = _contactPointColor;
                        Gizmos.DrawSphere(point.Position, pointRadius);
                    }

                    if (_drawContactNormals && normalLength > 0.0f)
                    {
                        Gizmos.color = _contactNormalColor;
                        DrawNormal(point.Position, manifold.Normal, normalLength, arrowSize);
                    }
                }
            }
        }

        private static void DrawNormal(Vector3 start, Vector3 normal, float length, float arrowSize)
        {
            Vector3 direction = NormalizeOrFallback(normal, Vector3.up);
            Vector3 end = start + (direction * length);
            Gizmos.DrawLine(start, end);

            if (arrowSize <= 0.0f)
            {
                return;
            }

            Vector3 tangent = Vector3.Cross(direction, Vector3.up);
            if (tangent.sqrMagnitude < 0.000001f)
            {
                tangent = Vector3.Cross(direction, Vector3.right);
            }

            tangent.Normalize();
            Vector3 back = -direction * arrowSize;
            Vector3 side = tangent * (arrowSize * 0.45f);
            Gizmos.DrawLine(end, end + back + side);
            Gizmos.DrawLine(end, end + back - side);
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            float magnitude = value.magnitude;
            return magnitude > 0.0f ? value / magnitude : fallback;
        }
    }
}
