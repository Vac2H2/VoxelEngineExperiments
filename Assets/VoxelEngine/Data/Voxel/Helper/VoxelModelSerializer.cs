using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Data.Voxel
{
    public static class VoxelModelSerializer
    {
        public const string FileExtension = ".voxelmodel.bytes";

        public static byte[] SerializeToBytes(VoxelModel model)
        {
            using var stream = new MemoryStream();
            Serialize(stream, model);
            return stream.ToArray();
        }

        public static void Serialize(string path, VoxelModel model)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path must be a non-empty string.", nameof(path));
            }

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Serialize(stream, model);
        }

        public static void Serialize(Stream stream, VoxelModel model)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (!stream.CanWrite)
            {
                throw new ArgumentException("Stream must be writable.", nameof(stream));
            }

            ValidateModel(model);

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteVolume(writer, model.OpaqueVolume);
            WriteVolume(writer, model.TransparentVolume);
        }

        public static VoxelModel Deserialize(byte[] data, Allocator allocator)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using var stream = new MemoryStream(data, writable: false);
            return Deserialize(stream, allocator);
        }

        public static VoxelModel Deserialize(VoxelModelAsset modelAsset, Allocator allocator)
        {
            if (modelAsset == null)
            {
                throw new ArgumentNullException(nameof(modelAsset));
            }

            if (!modelAsset.HasSerializedData)
            {
                throw new ArgumentException("VoxelModelAsset contains no serialized data.", nameof(modelAsset));
            }

            return Deserialize(modelAsset.SerializedData, allocator);
        }

        public static VoxelModel Deserialize(TextAsset textAsset, Allocator allocator)
        {
            if (textAsset == null)
            {
                throw new ArgumentNullException(nameof(textAsset));
            }

            return Deserialize(textAsset.bytes, allocator);
        }

        public static VoxelModel Deserialize(string path, Allocator allocator)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path must be a non-empty string.", nameof(path));
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Deserialize(stream, allocator);
        }

        public static VoxelModel Deserialize(Stream stream, Allocator allocator)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (!stream.CanRead)
            {
                throw new ArgumentException("Stream must be readable.", nameof(stream));
            }

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            try
            {
                VoxelVolume opaqueVolume = null;
                VoxelVolume transparentVolume = null;

                try
                {
                    opaqueVolume = ReadVolume(reader, allocator);
                    transparentVolume = ReadVolume(reader, allocator);
                    return new VoxelModel(opaqueVolume, transparentVolume);
                }
                catch
                {
                    opaqueVolume?.Dispose();
                    transparentVolume?.Dispose();
                    throw;
                }
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("Unexpected end of VoxelModel stream.", ex);
            }
        }

        private static void WriteVolume(BinaryWriter writer, VoxelVolume volume)
        {
            ValidateVolume(volume);

            List<int> chunkIndices = GetAllocatedChunkIndices(volume);
            writer.Write(chunkIndices.Count);

            foreach (int chunkIndex in chunkIndices)
            {
                WriteInt3(writer, volume.ChunkCoordinates[chunkIndex]);

                NativeSlice<byte> voxelSlice = volume.GetChunkVoxelDataSlice(chunkIndex);
                for (int voxelIndex = 0; voxelIndex < VoxelVolume.VoxelsPerChunk; voxelIndex++)
                {
                    writer.Write(voxelSlice[voxelIndex]);
                }

                NativeSlice<VoxelChunkAabb> aabbSlice = volume.GetChunkAabbSlice(chunkIndex);
                for (int aabbIndex = 0; aabbIndex < VoxelVolume.MaxAabbsPerChunk; aabbIndex++)
                {
                    VoxelChunkAabb aabb = aabbSlice[aabbIndex];
                    writer.Write(aabb.IsActive);
                    WriteInt3(writer, aabb.Min);
                    WriteInt3(writer, aabb.Max);
                }
            }
        }

        private static VoxelVolume ReadVolume(BinaryReader reader, Allocator allocator)
        {
            int chunkCount = reader.ReadInt32();
            if (chunkCount < 0)
            {
                throw new InvalidDataException("VoxelVolume chunk count must be non-negative.");
            }

            VoxelVolume volume = new VoxelVolume(Math.Max(1, chunkCount), allocator);

            try
            {
                for (int storedChunkIndex = 0; storedChunkIndex < chunkCount; storedChunkIndex++)
                {
                    int3 chunkCoordinate = ReadInt3(reader);
                    if (!volume.TryAllocateChunk(chunkCoordinate, out int chunkIndex))
                    {
                        throw new InvalidDataException(
                            $"VoxelVolume contains duplicate chunk coordinate {chunkCoordinate}.");
                    }

                    NativeSlice<byte> voxelSlice = volume.GetChunkVoxelDataSlice(chunkIndex);
                    for (int voxelIndex = 0; voxelIndex < VoxelVolume.VoxelsPerChunk; voxelIndex++)
                    {
                        voxelSlice[voxelIndex] = reader.ReadByte();
                    }

                    NativeSlice<VoxelChunkAabb> aabbSlice = volume.GetChunkAabbSlice(chunkIndex);
                    for (int aabbIndex = 0; aabbIndex < VoxelVolume.MaxAabbsPerChunk; aabbIndex++)
                    {
                        byte isActive = reader.ReadByte();
                        int3 min = ReadInt3(reader);
                        int3 max = ReadInt3(reader);

                        if (isActive == 0)
                        {
                            aabbSlice[aabbIndex] = default;
                            continue;
                        }

                        if (isActive != 1)
                        {
                            throw new InvalidDataException(
                                $"VoxelChunkAabb IsActive flag must be 0 or 1, but was {isActive}.");
                        }

                        volume.SetAabb(chunkIndex, aabbIndex, min, max);
                    }
                }

                return volume;
            }
            catch
            {
                volume.Dispose();
                throw;
            }
        }

        private static List<int> GetAllocatedChunkIndices(VoxelVolume volume)
        {
            var chunkIndices = new List<int>(volume.ChunkCount);
            for (int chunkIndex = 0; chunkIndex < volume.ChunkCapacity; chunkIndex++)
            {
                if (volume.IsChunkAllocated(chunkIndex))
                {
                    chunkIndices.Add(chunkIndex);
                }
            }

            // Sort to make the binary output independent from runtime allocation history.
            chunkIndices.Sort((left, right) => Compare(volume.ChunkCoordinates[left], volume.ChunkCoordinates[right]));
            return chunkIndices;
        }

        private static int Compare(int3 left, int3 right)
        {
            int x = left.x.CompareTo(right.x);
            if (x != 0)
            {
                return x;
            }

            int y = left.y.CompareTo(right.y);
            if (y != 0)
            {
                return y;
            }

            return left.z.CompareTo(right.z);
        }

        private static void ValidateModel(VoxelModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            ValidateVolume(model.OpaqueVolume);
            ValidateVolume(model.TransparentVolume);
        }

        private static void ValidateVolume(VoxelVolume volume)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            if (!volume.IsCreated)
            {
                throw new ArgumentException("VoxelVolume must be created before serialization.", nameof(volume));
            }
        }

        private static void WriteInt3(BinaryWriter writer, int3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static int3 ReadInt3(BinaryReader reader)
        {
            return new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }
    }
}
