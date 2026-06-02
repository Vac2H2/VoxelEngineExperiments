using System;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngineModules.Shape.Debugger
{
    internal sealed class ShapeRenderBackendBridge
    {
        private const string AssemblyCSharpName = "Assembly-CSharp";
        private const int ChunkSize = ShapeDataContainer.ChunkSize;
        private const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        private const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;

        private Type _renderPipelineType;
        private Type _renderBackendType;
        private Type _renderInstanceHandleType;
        private Type _voxelModelType;
        private Type _voxelModelKeyType;
        private Type _voxelVolumeType;
        private Type _voxelPaletteType;
        private Type _voxelPaletteKeyType;
        private Type _voxelColorType;

        private PropertyInfo _activePipelineProperty;
        private PropertyInfo _renderBackendProperty;
        private PropertyInfo _opaqueVolumeProperty;
        private PropertyInfo _paletteItemProperty;

        private MethodInfo _addInstanceMethod;
        private MethodInfo _removeInstanceMethod;
        private MethodInfo _updateTransformMethod;
        private MethodInfo _createModelMethod;
        private MethodInfo _tryAllocateChunkMethod;
        private MethodInfo _getChunkVoxelDataSliceMethod;
        private MethodInfo _tryAllocateAabbSlotMethod;
        private MethodInfo _setAabbMethod;

        private bool _resolved;
        private string _resolveError;

        public bool TryGetBackend(out object backend, out string error)
        {
            backend = null;
            if (!ResolveTypes(out error))
            {
                return false;
            }

            RenderPipeline currentPipeline = RenderPipelineManager.currentPipeline;
            object pipeline = currentPipeline != null &&
                              _renderPipelineType.IsInstanceOfType(currentPipeline)
                ? currentPipeline
                : _activePipelineProperty.GetValue(null);

            if (pipeline == null)
            {
                error = "Waiting for an active VoxelEngineRenderPipeline.";
                return false;
            }

            backend = _renderBackendProperty.GetValue(pipeline);
            if (backend == null)
            {
                error = "Active VoxelEngineRenderPipeline has no render backend.";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryAddShapeInstance(
            object backend,
            ShapeDataStorage storage,
            int shapeHandle,
            Color32 color,
            Matrix4x4 localToWorld,
            string modelKey,
            string paletteKey,
            out object renderHandle,
            out string error)
        {
            renderHandle = null;
            if (!ResolveTypes(out error))
            {
                return false;
            }

            if (backend == null)
            {
                error = "Render backend is null.";
                return false;
            }

            int nonEmptyChunkCount = CountNonEmptyChunks(storage, shapeHandle);
            if (nonEmptyChunkCount == 0)
            {
                error = "Shape has no occupied chunks to render.";
                return false;
            }

            object model = null;
            object palette = null;

            try
            {
                model = _createModelMethod.Invoke(
                    null,
                    new object[] { math.max(1, nonEmptyChunkCount), 1, Allocator.Temp });
                palette = Activator.CreateInstance(_voxelPaletteType, Allocator.Temp);

                FillModel(model, storage, shapeHandle);
                FillPalette(palette, color);

                object modelKeyValue = Activator.CreateInstance(_voxelModelKeyType, modelKey);
                object paletteKeyValue = Activator.CreateInstance(_voxelPaletteKeyType, paletteKey);

                renderHandle = _addInstanceMethod.Invoke(
                    backend,
                    new[] { modelKeyValue, paletteKeyValue, model, palette, localToWorld });

                error = null;
                return true;
            }
            catch (TargetInvocationException exception)
            {
                error = exception.InnerException != null
                    ? exception.InnerException.Message
                    : exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                DisposeReflected(model);
                DisposeReflected(palette);
            }
        }

        public void RemoveInstance(object backend, object renderHandle)
        {
            if (backend == null ||
                renderHandle == null ||
                !ResolveTypes(out _))
            {
                return;
            }

            try
            {
                _removeInstanceMethod.Invoke(backend, new[] { renderHandle });
            }
            catch (TargetInvocationException)
            {
            }
        }

        public void UpdateTransform(object backend, object renderHandle, Matrix4x4 localToWorld)
        {
            if (backend == null ||
                renderHandle == null ||
                !ResolveTypes(out _))
            {
                return;
            }

            try
            {
                _updateTransformMethod.Invoke(backend, new[] { renderHandle, localToWorld });
            }
            catch (TargetInvocationException)
            {
            }
        }


        #region Model Build

        private int CountNonEmptyChunks(ShapeDataStorage storage, int shapeHandle)
        {
            ShapeDataView dataView = storage.GetShapeDataView(shapeHandle);
            int chunkBase = shapeHandle * ChunksPerShape;
            int nonEmptyChunkCount = 0;

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = chunkBase + chunkSlot;
                if (dataView.ChunkUsed[chunkMetadataIndex] == 0 ||
                    !HasOccupiedVoxel(dataView.IsOccupied, chunkMetadataIndex))
                {
                    continue;
                }

                nonEmptyChunkCount++;
            }

            return nonEmptyChunkCount;
        }

        private void FillModel(object model, ShapeDataStorage storage, int shapeHandle)
        {
            ShapeDataView dataView = storage.GetShapeDataView(shapeHandle);
            object volume = _opaqueVolumeProperty.GetValue(model);
            int chunkBase = shapeHandle * ChunksPerShape;

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = chunkBase + chunkSlot;
                if (dataView.ChunkUsed[chunkMetadataIndex] == 0 ||
                    !TryGetOccupiedLocalVoxelMinMax(
                        dataView.IsOccupied,
                        chunkMetadataIndex,
                        out int3 min,
                        out int3 max))
                {
                    continue;
                }

                object[] allocateChunkArguments = { dataView.ChunkPositions[chunkMetadataIndex], -1 };
                bool allocated = (bool)_tryAllocateChunkMethod.Invoke(volume, allocateChunkArguments);
                if (!allocated)
                {
                    continue;
                }

                int volumeChunkIndex = (int)allocateChunkArguments[1];
                NativeSlice<byte> destination = (NativeSlice<byte>)_getChunkVoxelDataSliceMethod.Invoke(
                    volume,
                    new object[] { volumeChunkIndex });

                CopyBitPlaneChunkToVoxelVolumeChunk(
                    dataView.IsOccupied,
                    chunkMetadataIndex,
                    destination);

                object[] allocateAabbArguments = { volumeChunkIndex, -1 };
                bool aabbAllocated = (bool)_tryAllocateAabbSlotMethod.Invoke(volume, allocateAabbArguments);
                if (!aabbAllocated)
                {
                    throw new InvalidOperationException("Shape debug chunk has no free AABB slot.");
                }

                int aabbIndex = (int)allocateAabbArguments[1];
                _setAabbMethod.Invoke(
                    volume,
                    new object[] { volumeChunkIndex, aabbIndex, min, max + new int3(1, 1, 1) });
            }
        }

        private void FillPalette(object palette, Color32 color)
        {
            object voxelColor = Activator.CreateInstance(
                _voxelColorType,
                color.r,
                color.g,
                color.b,
                color.a);
            _paletteItemProperty.SetValue(palette, voxelColor, new object[] { 1 });
        }

        private static bool HasOccupiedVoxel(NativeArray<byte> isOccupied, int chunkMetadataIndex)
        {
            int sourceBase = chunkMetadataIndex * BitPlaneBytesPerChunk;
            for (int row = 0; row < BitPlaneBytesPerChunk; row++)
            {
                if (isOccupied[sourceBase + row] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetOccupiedLocalVoxelMinMax(
            NativeArray<byte> isOccupied,
            int chunkMetadataIndex,
            out int3 min,
            out int3 max)
        {
            min = new int3(ChunkSize, ChunkSize, ChunkSize);
            max = new int3(-1, -1, -1);
            int sourceBase = chunkMetadataIndex * BitPlaneBytesPerChunk;

            for (int z = 0; z < ChunkSize; z++)
            {
                for (int y = 0; y < ChunkSize; y++)
                {
                    byte row = isOccupied[sourceBase + RowIndex(y, z)];
                    while (row != 0)
                    {
                        int x = math.tzcnt((uint)row);
                        int3 voxel = new int3(x, y, z);
                        min = math.min(min, voxel);
                        max = math.max(max, voxel);
                        row = (byte)(row & ~(1 << x));
                    }
                }
            }

            return max.x >= 0;
        }

        private static void CopyBitPlaneChunkToVoxelVolumeChunk(
            NativeArray<byte> isOccupied,
            int chunkMetadataIndex,
            NativeSlice<byte> destination)
        {
            int sourceBase = chunkMetadataIndex * BitPlaneBytesPerChunk;

            for (int z = 0; z < ChunkSize; z++)
            {
                for (int y = 0; y < ChunkSize; y++)
                {
                    byte row = isOccupied[sourceBase + RowIndex(y, z)];
                    while (row != 0)
                    {
                        int x = math.tzcnt((uint)row);
                        destination[FlattenVoxelIndex(x, y, z)] = 1;
                        row = (byte)(row & ~(1 << x));
                    }
                }
            }
        }

        private static int RowIndex(int y, int z)
        {
            return y + ChunkSize * z;
        }

        private static int FlattenVoxelIndex(int x, int y, int z)
        {
            return x + ChunkSize * y + ChunkSize * ChunkSize * z;
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
                _renderPipelineType = RequiredType("VoxelEngine.Render.RenderPipeline.VoxelEngineRenderPipeline");
                _renderBackendType = RequiredType("VoxelEngine.Render.RenderBackend.VoxelEngineRenderBackend");
                _renderInstanceHandleType = RequiredType("VoxelEngine.Render.RenderBackend.VoxelEngineRenderInstanceHandle");
                _voxelModelType = RequiredType("VoxelEngine.Data.Voxel.VoxelModel");
                _voxelModelKeyType = RequiredType("VoxelEngine.LifeCycle.Manager.VoxelModelKey");
                _voxelVolumeType = RequiredType("VoxelEngine.Data.Voxel.VoxelVolume");
                _voxelPaletteType = RequiredType("VoxelEngine.Data.Voxel.VoxelPalette");
                _voxelPaletteKeyType = RequiredType("VoxelEngine.LifeCycle.Manager.VoxelPaletteKey");
                _voxelColorType = RequiredType("VoxelEngine.Data.Voxel.VoxelColor");

                _activePipelineProperty = RequiredProperty(
                    _renderPipelineType,
                    "ActivePipeline",
                    BindingFlags.Public | BindingFlags.Static);
                _renderBackendProperty = RequiredProperty(
                    _renderPipelineType,
                    "RenderBackend",
                    BindingFlags.Public | BindingFlags.Instance);
                _opaqueVolumeProperty = RequiredProperty(
                    _voxelModelType,
                    "OpaqueVolume",
                    BindingFlags.Public | BindingFlags.Instance);
                _paletteItemProperty = RequiredProperty(
                    _voxelPaletteType,
                    "Item",
                    BindingFlags.Public | BindingFlags.Instance);

                _createModelMethod = RequiredMethod(
                    _voxelModelType,
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    typeof(int),
                    typeof(int),
                    typeof(Allocator));
                _tryAllocateChunkMethod = RequiredMethod(
                    _voxelVolumeType,
                    "TryAllocateChunk",
                    BindingFlags.Public | BindingFlags.Instance,
                    typeof(int3),
                    typeof(int).MakeByRefType());
                _getChunkVoxelDataSliceMethod = RequiredMethod(
                    _voxelVolumeType,
                    "GetChunkVoxelDataSlice",
                    BindingFlags.Public | BindingFlags.Instance,
                    typeof(int));
                _tryAllocateAabbSlotMethod = RequiredMethod(
                    _voxelVolumeType,
                    "TryAllocateAabbSlot",
                    BindingFlags.Public | BindingFlags.Instance,
                    typeof(int),
                    typeof(int).MakeByRefType());
                _setAabbMethod = RequiredMethod(
                    _voxelVolumeType,
                    "SetAabb",
                    BindingFlags.Public | BindingFlags.Instance,
                    typeof(int),
                    typeof(int),
                    typeof(int3),
                    typeof(int3));
                _addInstanceMethod = RequiredMethod(
                    _renderBackendType,
                    "AddInstance",
                    BindingFlags.Public | BindingFlags.Instance,
                    _voxelModelKeyType,
                    _voxelPaletteKeyType,
                    _voxelModelType,
                    _voxelPaletteType,
                    typeof(Matrix4x4));
                _removeInstanceMethod = RequiredMethod(
                    _renderBackendType,
                    "RemoveInstance",
                    BindingFlags.Public | BindingFlags.Instance,
                    _renderInstanceHandleType);
                _updateTransformMethod = RequiredMethod(
                    _renderBackendType,
                    "UpdateInstanceTransform",
                    BindingFlags.Public | BindingFlags.Instance,
                    _renderInstanceHandleType,
                    typeof(Matrix4x4));

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

        private static MethodInfo RequiredMethod(
            Type type,
            string name,
            BindingFlags bindingFlags,
            params Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(name, bindingFlags, null, parameterTypes, null);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }

            return method;
        }

        private static void DisposeReflected(object instance)
        {
            if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        #endregion
    }
}
