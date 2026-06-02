using System;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngineModules.Shape.Debugger
{
    internal enum ShapePhysicsBodyKind
    {
        Static,
        Dynamic,
        Kinematic,
    }

    internal struct ShapePhysicsBodyState
    {
        public ShapePhysicsBodyKind BodyKind;
        public float Mass;
        public Vector3 LocalCenterOfMass;
        public Vector3 InertiaSize;
        public bool UseGravity;
        public float GravityScale;
        public float LinearDamping;
        public float AngularDamping;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
    }

    internal readonly struct ShapePhysicsComponents
    {
        public ShapePhysicsComponents(GameObject gameObject, object body, object collider)
        {
            GameObject = gameObject;
            Body = body;
            Collider = collider;
        }

        public GameObject GameObject { get; }
        public object Body { get; }
        public object Collider { get; }
    }

    internal sealed class ShapePhysicsBridge
    {
        private const string AssemblyCSharpName = "Assembly-CSharp";
        private const int ChunkSize = ShapeDataContainer.ChunkSize;
        private const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        private const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;

        private Type _voxelEngineSettingsType;
        private Type _miniRigidBodyType;
        private Type _miniRigidBodyTypeEnum;
        private Type _miniColliderType;
        private Type _miniColliderDataType;
        private Type _miniColliderChunkDataType;
        private Type _miniPhysicsRunnerType;

        private PropertyInfo _globalVoxelSizeProperty;
        private PropertyInfo _bodyTypeProperty;
        private PropertyInfo _massProperty;
        private PropertyInfo _localCenterOfMassProperty;
        private PropertyInfo _useGravityProperty;
        private PropertyInfo _gravityScaleProperty;
        private PropertyInfo _linearDampingProperty;
        private PropertyInfo _angularDampingProperty;
        private PropertyInfo _linearVelocityProperty;
        private PropertyInfo _angularVelocityProperty;
        private PropertyInfo _worldCenterOfMassProperty;
        private PropertyInfo _isDynamicProperty;
        private PropertyInfo _isKinematicProperty;
        private PropertyInfo _handleProperty;
        private PropertyInfo _dataProperty;

        private MethodInfo _setSolidBoxInertiaTensorMethod;
        private MethodInfo _movePositionMethod;
        private MethodInfo _setChunksMethod;
        private MethodInfo _refreshAttachedBodyMethod;
        private MethodInfo _runnerTickMethod;

        private ConstructorInfo _fullChunkConstructor;
        private ConstructorInfo _explicitChunkConstructor;
        private FieldInfo _runnerTickInFixedUpdateField;

        private object _staticBodyType;
        private object _dynamicBodyType;
        private object _kinematicBodyType;

        private bool _resolved;
        private string _resolveError;

        public float ReadGlobalVoxelSize()
        {
            if (!ResolveTypes(out _))
            {
                return 1.0f;
            }

            object value = _globalVoxelSizeProperty.GetValue(null);
            return value is float voxelSize && voxelSize > 0.0f && float.IsFinite(voxelSize)
                ? voxelSize
                : 1.0f;
        }

        public bool TryCreateRunner(GameObject gameObject, out object runner, out string error)
        {
            runner = null;
            if (!ResolveTypes(out error))
            {
                return false;
            }

            runner = gameObject.GetComponent(_miniPhysicsRunnerType);
            if (runner == null)
            {
                runner = gameObject.AddComponent(_miniPhysicsRunnerType);
            }

            _runnerTickInFixedUpdateField?.SetValue(runner, false);
            return true;
        }

        public bool TryCreatePhysicsComponents(
            GameObject gameObject,
            out ShapePhysicsComponents components,
            out string error)
        {
            components = default;
            if (!ResolveTypes(out error))
            {
                return false;
            }

            object body = gameObject.GetComponent(_miniRigidBodyType);
            if (body == null)
            {
                body = gameObject.AddComponent(_miniRigidBodyType);
            }

            object collider = gameObject.GetComponent(_miniColliderType);
            if (collider == null)
            {
                collider = gameObject.AddComponent(_miniColliderType);
            }

            components = new ShapePhysicsComponents(gameObject, body, collider);
            return true;
        }

        public void TickRunner(object runner, float deltaTime)
        {
            if (runner == null || !ResolveTypes(out _))
            {
                return;
            }

            _runnerTickMethod.Invoke(runner, new object[] { Mathf.Max(0.0f, deltaTime) });
        }

        public ShapePhysicsBodyState CaptureBodyState(object body)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return DefaultDynamicState();
            }

            return new ShapePhysicsBodyState
            {
                BodyKind = ReadBodyKind(body),
                Mass = ReadFloat(_massProperty, body, 1.0f),
                LocalCenterOfMass = ReadVector3(_localCenterOfMassProperty, body, Vector3.zero),
                InertiaSize = Vector3.one,
                UseGravity = ReadBool(_useGravityProperty, body, true),
                GravityScale = ReadFloat(_gravityScaleProperty, body, 1.0f),
                LinearDamping = ReadFloat(_linearDampingProperty, body, 0.0f),
                AngularDamping = ReadFloat(_angularDampingProperty, body, 0.05f),
                LinearVelocity = ReadVector3(_linearVelocityProperty, body, Vector3.zero),
                AngularVelocity = ReadVector3(_angularVelocityProperty, body, Vector3.zero)
            };
        }

        public int ReadBodyHandleValue(object body, int fallback)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return fallback;
            }

            object handle = _handleProperty.GetValue(body);
            PropertyInfo valueProperty = handle?.GetType().GetProperty(
                "Value",
                BindingFlags.Public | BindingFlags.Instance);
            object value = valueProperty?.GetValue(handle);
            return value is int handleValue && handleValue != 0 ? handleValue : fallback;
        }

        public Vector3 ReadWorldCenterOfMass(object body, Transform fallbackTransform)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return fallbackTransform != null ? fallbackTransform.position : Vector3.zero;
            }

            object value = _worldCenterOfMassProperty.GetValue(body);
            return value is Vector3 vector
                ? vector
                : fallbackTransform != null
                    ? fallbackTransform.position
                    : Vector3.zero;
        }

        public void ConfigureBody(object body, ShapePhysicsBodyState state)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return;
            }

            _bodyTypeProperty.SetValue(body, BodyKindToEnum(state.BodyKind));
            _massProperty.SetValue(body, Mathf.Max(0.000001f, state.Mass));
            _localCenterOfMassProperty.SetValue(body, SanitizeVector(state.LocalCenterOfMass));
            _setSolidBoxInertiaTensorMethod.Invoke(
                body,
                new object[] { SanitizePositiveVector(state.InertiaSize, 0.000001f) });
            _useGravityProperty.SetValue(body, state.BodyKind == ShapePhysicsBodyKind.Dynamic && state.UseGravity);
            _gravityScaleProperty.SetValue(body, SanitizeFinite(state.GravityScale));
            _linearDampingProperty.SetValue(body, Mathf.Max(0.0f, SanitizeFinite(state.LinearDamping)));
            _angularDampingProperty.SetValue(body, Mathf.Max(0.0f, SanitizeFinite(state.AngularDamping)));
            _linearVelocityProperty.SetValue(body, SanitizeVector(state.LinearVelocity));
            _angularVelocityProperty.SetValue(body, SanitizeVector(state.AngularVelocity));
        }

        public void SetBodyKind(object body, ShapePhysicsBodyKind bodyKind)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return;
            }

            _bodyTypeProperty.SetValue(body, BodyKindToEnum(bodyKind));
        }

        public void SetUseGravity(object body, bool useGravity)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return;
            }

            _useGravityProperty.SetValue(body, useGravity);
        }

        public void SetLinearVelocity(object body, Vector3 velocity)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return;
            }

            _linearVelocityProperty.SetValue(body, SanitizeVector(velocity));
        }

        public void SetAngularVelocity(object body, Vector3 velocity)
        {
            if (body == null || !ResolveTypes(out _))
            {
                return;
            }

            _angularVelocityProperty.SetValue(body, SanitizeVector(velocity));
        }

        public void MoveBodyPosition(object body, Transform transform, Vector3 worldPosition)
        {
            if (body != null && ResolveTypes(out _))
            {
                _movePositionMethod.Invoke(body, new object[] { SanitizeVector(worldPosition) });
                return;
            }

            if (transform != null)
            {
                transform.position = SanitizeVector(worldPosition);
            }
        }

        public bool RefreshColliderFromShape(
            object collider,
            ShapeDataStorage storage,
            int shapeHandle,
            out string error)
        {
            if (!ResolveTypes(out error))
            {
                return false;
            }

            if (collider == null)
            {
                error = "MiniCollider is null.";
                return false;
            }

            object data = _dataProperty.GetValue(collider);
            if (data == null)
            {
                error = "MiniCollider.Data is null.";
                return false;
            }

            Array chunks = BuildColliderChunks(storage, shapeHandle);
            _setChunksMethod.Invoke(data, new object[] { chunks });
            _refreshAttachedBodyMethod.Invoke(collider, Array.Empty<object>());
            error = null;
            return true;
        }

        public static ShapePhysicsBodyState DefaultDynamicState()
        {
            return new ShapePhysicsBodyState
            {
                BodyKind = ShapePhysicsBodyKind.Dynamic,
                Mass = 48.0f,
                LocalCenterOfMass = Vector3.zero,
                InertiaSize = Vector3.one,
                UseGravity = true,
                GravityScale = 1.0f,
                LinearDamping = 0.0f,
                AngularDamping = 0.05f,
                LinearVelocity = Vector3.zero,
                AngularVelocity = Vector3.zero
            };
        }


        #region Shape Conversion

        private Array BuildColliderChunks(ShapeDataStorage storage, int shapeHandle)
        {
            ShapeDataView dataView = storage.GetShapeDataView(shapeHandle);
            object[] chunks = new object[ChunksPerShape];
            int chunkCount = 0;
            int chunkBase = shapeHandle * ChunksPerShape;

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = chunkBase + chunkSlot;
                if (dataView.ChunkUsed[chunkMetadataIndex] == 0 ||
                    !TryBuildOccupiedVoxelIndices(
                        dataView.IsOccupied,
                        chunkMetadataIndex,
                        out int[] occupiedVoxelIndices,
                        out bool isFullChunk))
                {
                    continue;
                }

                chunks[chunkCount++] = isFullChunk
                    ? _fullChunkConstructor.Invoke(new object[] { dataView.ChunkPositions[chunkMetadataIndex] })
                    : _explicitChunkConstructor.Invoke(new object[] { dataView.ChunkPositions[chunkMetadataIndex], occupiedVoxelIndices });
            }

            Array typedChunks = Array.CreateInstance(_miniColliderChunkDataType, chunkCount);
            for (int i = 0; i < chunkCount; i++)
            {
                typedChunks.SetValue(chunks[i], i);
            }

            return typedChunks;
        }

        private static bool TryBuildOccupiedVoxelIndices(
            NativeArray<byte> isOccupied,
            int chunkMetadataIndex,
            out int[] occupiedVoxelIndices,
            out bool isFullChunk)
        {
            int sourceBase = chunkMetadataIndex * BitPlaneBytesPerChunk;
            int occupiedCount = 0;
            isFullChunk = true;

            for (int row = 0; row < BitPlaneBytesPerChunk; row++)
            {
                byte value = isOccupied[sourceBase + row];
                if (value != 0xFF)
                {
                    isFullChunk = false;
                }

                occupiedCount += math.countbits((uint)value);
            }

            if (occupiedCount == 0)
            {
                occupiedVoxelIndices = Array.Empty<int>();
                return false;
            }

            if (isFullChunk)
            {
                occupiedVoxelIndices = Array.Empty<int>();
                return true;
            }

            occupiedVoxelIndices = new int[occupiedCount];
            int writeIndex = 0;
            for (int z = 0; z < ChunkSize; z++)
            {
                for (int y = 0; y < ChunkSize; y++)
                {
                    byte row = isOccupied[sourceBase + RowIndex(y, z)];
                    while (row != 0)
                    {
                        int x = math.tzcnt((uint)row);
                        occupiedVoxelIndices[writeIndex++] = x + ChunkSize * y + ChunkSize * ChunkSize * z;
                        row = (byte)(row & ~(1 << x));
                    }
                }
            }

            return true;
        }

        private static int RowIndex(int y, int z)
        {
            return y + ChunkSize * z;
        }

        #endregion


        #region Reflection

        private bool ResolveTypes(out string error)
        {
            if (_resolved)
            {
                error = null;
                return true;
            }

            if (!string.IsNullOrEmpty(_resolveError))
            {
                error = _resolveError;
                return false;
            }

            try
            {
                _voxelEngineSettingsType = RequiredType("VoxelEngine.VoxelEngineSettings");
                _miniRigidBodyType = RequiredType("VoxelEngine.Physics.Body.MiniRigidBody");
                _miniRigidBodyTypeEnum = RequiredType("VoxelEngine.Physics.Body.MiniRigidBodyType");
                _miniColliderType = RequiredType("VoxelEngine.Physics.Collider.MiniCollider");
                _miniColliderDataType = RequiredType("VoxelEngine.Physics.Collider.MiniColliderData");
                _miniColliderChunkDataType = RequiredType("VoxelEngine.Physics.Collider.MiniColliderChunkData");
                _miniPhysicsRunnerType = RequiredType("VoxelEngine.Physics.Simulation.MiniPhysicsBroadphaseDebugRunner");

                _globalVoxelSizeProperty = RequiredProperty(
                    _voxelEngineSettingsType,
                    "GlobalVoxelSize",
                    BindingFlags.Public | BindingFlags.Static);
                _bodyTypeProperty = RequiredProperty(_miniRigidBodyType, "BodyType");
                _massProperty = RequiredProperty(_miniRigidBodyType, "Mass");
                _localCenterOfMassProperty = RequiredProperty(_miniRigidBodyType, "LocalCenterOfMass");
                _useGravityProperty = RequiredProperty(_miniRigidBodyType, "UseGravity");
                _gravityScaleProperty = RequiredProperty(_miniRigidBodyType, "GravityScale");
                _linearDampingProperty = RequiredProperty(_miniRigidBodyType, "LinearDamping");
                _angularDampingProperty = RequiredProperty(_miniRigidBodyType, "AngularDamping");
                _linearVelocityProperty = RequiredProperty(_miniRigidBodyType, "LinearVelocity");
                _angularVelocityProperty = RequiredProperty(_miniRigidBodyType, "AngularVelocity");
                _worldCenterOfMassProperty = RequiredProperty(_miniRigidBodyType, "WorldCenterOfMass");
                _isDynamicProperty = RequiredProperty(_miniRigidBodyType, "IsDynamic");
                _isKinematicProperty = RequiredProperty(_miniRigidBodyType, "IsKinematic");
                _handleProperty = RequiredProperty(_miniRigidBodyType, "Handle");
                _dataProperty = RequiredProperty(_miniColliderType, "Data");

                _setSolidBoxInertiaTensorMethod = RequiredMethod(
                    _miniRigidBodyType,
                    "SetSolidBoxInertiaTensor",
                    typeof(Vector3));
                _movePositionMethod = RequiredMethod(_miniRigidBodyType, "MovePosition", typeof(Vector3));
                _setChunksMethod = RequiredMethod(
                    _miniColliderDataType,
                    "SetChunks",
                    _miniColliderChunkDataType.MakeArrayType());
                _refreshAttachedBodyMethod = RequiredMethod(_miniColliderType, "RefreshAttachedBody");
                _runnerTickMethod = RequiredMethod(_miniPhysicsRunnerType, "Tick", typeof(float));

                _fullChunkConstructor = RequiredConstructor(_miniColliderChunkDataType, typeof(int3));
                _explicitChunkConstructor = RequiredConstructor(
                    _miniColliderChunkDataType,
                    typeof(int3),
                    typeof(int[]));
                _runnerTickInFixedUpdateField = _miniPhysicsRunnerType.GetField(
                    "_tickInFixedUpdate",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                _staticBodyType = Enum.Parse(_miniRigidBodyTypeEnum, "Static");
                _dynamicBodyType = Enum.Parse(_miniRigidBodyTypeEnum, "Dynamic");
                _kinematicBodyType = Enum.Parse(_miniRigidBodyTypeEnum, "Kinematic");

                _resolved = true;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                _resolveError = exception.Message;
                error = _resolveError;
                return false;
            }
        }

        private object BodyKindToEnum(ShapePhysicsBodyKind bodyKind)
        {
            switch (bodyKind)
            {
                case ShapePhysicsBodyKind.Static:
                    return _staticBodyType;
                case ShapePhysicsBodyKind.Kinematic:
                    return _kinematicBodyType;
                default:
                    return _dynamicBodyType;
            }
        }

        private ShapePhysicsBodyKind ReadBodyKind(object body)
        {
            if (ReadBool(_isDynamicProperty, body, false))
            {
                return ShapePhysicsBodyKind.Dynamic;
            }

            return ReadBool(_isKinematicProperty, body, false)
                ? ShapePhysicsBodyKind.Kinematic
                : ShapePhysicsBodyKind.Static;
        }

        private static Type RequiredType(string fullName)
        {
            Type type = Type.GetType($"{fullName}, {AssemblyCSharpName}");
            if (type != null)
            {
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            throw new TypeLoadException($"Could not find type '{fullName}'.");
        }

        private static PropertyInfo RequiredProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (property == null)
            {
                throw new MissingMemberException(type.FullName, name);
            }

            return property;
        }

        private static PropertyInfo RequiredProperty(
            Type type,
            string name,
            BindingFlags bindingFlags)
        {
            PropertyInfo property = type.GetProperty(name, bindingFlags);
            if (property == null)
            {
                throw new MissingMemberException(type.FullName, name);
            }

            return property;
        }

        private static MethodInfo RequiredMethod(Type type, string name, params Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static,
                null,
                parameterTypes,
                null);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }

            return method;
        }

        private static ConstructorInfo RequiredConstructor(Type type, params Type[] parameterTypes)
        {
            ConstructorInfo constructor = type.GetConstructor(parameterTypes);
            if (constructor == null)
            {
                throw new MissingMethodException(type.FullName, ".ctor");
            }

            return constructor;
        }

        #endregion


        #region Value Helpers

        private static bool ReadBool(PropertyInfo property, object instance, bool fallback)
        {
            object value = property.GetValue(instance);
            return value is bool boolValue ? boolValue : fallback;
        }

        private static float ReadFloat(PropertyInfo property, object instance, float fallback)
        {
            object value = property.GetValue(instance);
            return value is float floatValue && float.IsFinite(floatValue) ? floatValue : fallback;
        }

        private static Vector3 ReadVector3(PropertyInfo property, object instance, Vector3 fallback)
        {
            object value = property.GetValue(instance);
            return value is Vector3 vector ? SanitizeVector(vector) : fallback;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsFinite(value) ? value : 0.0f;
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                SanitizeFinite(value.x),
                SanitizeFinite(value.y),
                SanitizeFinite(value.z));
        }

        private static Vector3 SanitizePositiveVector(Vector3 value, float minimum)
        {
            value = SanitizeVector(value);
            return new Vector3(
                Mathf.Max(minimum, value.x),
                Mathf.Max(minimum, value.y),
                Mathf.Max(minimum, value.z));
        }

        #endregion
    }
}
