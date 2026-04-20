using System;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace VoxelEngine.Data.Voxel
{
    public static class VoxelPaletteSerializer
    {
        public const string FileExtension = ".voxelpalette.bytes";

        public static byte[] SerializeToBytes(VoxelPalette palette)
        {
            using var stream = new MemoryStream();
            Serialize(stream, palette);
            return stream.ToArray();
        }

        public static void Serialize(string path, VoxelPalette palette)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path must be a non-empty string.", nameof(path));
            }

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Serialize(stream, palette);
        }

        public static void Serialize(Stream stream, VoxelPalette palette)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (!stream.CanWrite)
            {
                throw new ArgumentException("Stream must be writable.", nameof(stream));
            }

            ValidatePalette(palette);

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            for (int i = 0; i < VoxelPalette.ColorCount; i++)
            {
                VoxelColor color = palette[i];
                writer.Write(color.R);
                writer.Write(color.G);
                writer.Write(color.B);
                writer.Write(color.A);
            }
        }

        public static VoxelPalette Deserialize(byte[] data, Allocator allocator)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using var stream = new MemoryStream(data, writable: false);
            return Deserialize(stream, allocator);
        }

        public static VoxelPalette Deserialize(VoxelPaletteAsset paletteAsset, Allocator allocator)
        {
            if (paletteAsset == null)
            {
                throw new ArgumentNullException(nameof(paletteAsset));
            }

            if (!paletteAsset.HasSerializedData)
            {
                throw new ArgumentException("VoxelPaletteAsset contains no serialized data.", nameof(paletteAsset));
            }

            return Deserialize(paletteAsset.SerializedData, allocator);
        }

        public static VoxelPalette Deserialize(TextAsset textAsset, Allocator allocator)
        {
            if (textAsset == null)
            {
                throw new ArgumentNullException(nameof(textAsset));
            }

            return Deserialize(textAsset.bytes, allocator);
        }

        public static VoxelPalette Deserialize(string path, Allocator allocator)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path must be a non-empty string.", nameof(path));
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Deserialize(stream, allocator);
        }

        public static VoxelPalette Deserialize(Stream stream, Allocator allocator)
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
                VoxelPalette palette = new VoxelPalette(allocator);

                try
                {
                    for (int i = 0; i < VoxelPalette.ColorCount; i++)
                    {
                        palette[i] = new VoxelColor(
                            reader.ReadByte(),
                            reader.ReadByte(),
                            reader.ReadByte(),
                            reader.ReadByte());
                    }

                    return palette;
                }
                catch
                {
                    palette.Dispose();
                    throw;
                }
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("Unexpected end of VoxelPalette stream.", ex);
            }
        }

        private static void ValidatePalette(VoxelPalette palette)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (!palette.IsCreated)
            {
                throw new ArgumentException("VoxelPalette must be created.", nameof(palette));
            }
        }
    }
}
