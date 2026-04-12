using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.VoxelRenderer
{
    internal static class MaterialPropertyBlockUtility
    {
        public static void CopyShaderProperties(
            Material material,
            MaterialPropertyBlock source,
            MaterialPropertyBlock destination)
        {
            if (destination == null)
            {
                throw new System.ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            AppendShaderProperties(material, source, destination);
        }

        public static void AppendShaderProperties(
            Material material,
            MaterialPropertyBlock source,
            MaterialPropertyBlock destination)
        {
            if (destination == null)
            {
                throw new System.ArgumentNullException(nameof(destination));
            }

            if (material == null || source == null || source.isEmpty)
            {
                return;
            }

            Shader shader = material.shader;
            if (shader == null)
            {
                return;
            }

            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                int propertyId = Shader.PropertyToID(shader.GetPropertyName(i));
                if (!source.HasProperty(propertyId))
                {
                    continue;
                }

                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                        destination.SetColor(propertyId, source.GetColor(propertyId));
                        break;

                    case ShaderPropertyType.Vector:
                        destination.SetVector(propertyId, source.GetVector(propertyId));
                        break;

                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        destination.SetFloat(propertyId, source.GetFloat(propertyId));
                        break;

                    case ShaderPropertyType.Int:
                        destination.SetInteger(propertyId, source.GetInteger(propertyId));
                        break;

                    case ShaderPropertyType.Texture:
                        destination.SetTexture(propertyId, source.GetTexture(propertyId));
                        break;
                }
            }
        }
    }
}
