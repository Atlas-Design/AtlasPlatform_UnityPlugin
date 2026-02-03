// GLBImporter.cs - Custom GLB/GLTF importer for Atlas Workflow
// Parses GLB files directly without external dependencies (no GLTFast, no UnityGLTF)
// This avoids shader compilation errors and gives full control over the import process

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Atlas.Workflow
{
    /// <summary>
    /// Result of a GLB import operation
    /// </summary>
    public class GLBImportResult
    {
        public bool Success;
        public string ErrorMessage;
        
        public GameObject Prefab;
        public Mesh Mesh;
        public Material Material;
        public Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        
        public string PrefabPath;
        public string MeshPath;
        public string MaterialPath;
    }

    /// <summary>
    /// Custom GLB importer that parses GLB files directly
    /// </summary>
    public static class GLBImporter
    {
        #region Public API

        /// <summary>
        /// Imports a GLB file and creates Unity assets (mesh, textures, material, prefab)
        /// </summary>
        /// <param name="glbPath">Path to the GLB file (can be outside Assets folder)</param>
        /// <param name="outputFolder">Unity Assets folder to save imported assets (e.g., "Assets/Imports")</param>
        /// <param name="assetName">Optional name for the imported assets (defaults to filename)</param>
        /// <returns>Import result with references to created assets</returns>
        public static GLBImportResult Import(string glbPath, string outputFolder, string assetName = null)
        {
            var result = new GLBImportResult();

            try
            {
                if (!File.Exists(glbPath))
                {
                    result.ErrorMessage = $"GLB file not found: {glbPath}";
                    return result;
                }

                // Ensure output folder exists
                EnsureFolderExists(outputFolder);

                // Determine asset name
                if (string.IsNullOrEmpty(assetName))
                    assetName = Path.GetFileNameWithoutExtension(glbPath);

                // Sanitize asset name
                assetName = SanitizeAssetName(assetName);

                AtlasLogger.LogFile($"[GLBImporter] Importing: {glbPath}");

                // Parse the GLB file
                var glbData = ParseGLB(glbPath);

                // Extract and save textures
                result.Textures = ExtractAndSaveTextures(glbData, outputFolder, assetName);

                // Configure texture import settings (normal maps, etc.)
                ConfigureTextureSettings(glbData, result.Textures);

                // Create material
                result.Material = CreateMaterial(glbData, result.Textures, outputFolder, assetName);
                result.MaterialPath = AssetDatabase.GetAssetPath(result.Material);

                // Extract mesh
                result.Mesh = ExtractMesh(glbData, outputFolder, assetName);
                result.MeshPath = AssetDatabase.GetAssetPath(result.Mesh);

                // Create prefab
                result.Prefab = CreatePrefab(result.Mesh, result.Material, outputFolder, assetName);
                result.PrefabPath = AssetDatabase.GetAssetPath(result.Prefab);

                result.Success = true;
                AtlasLogger.LogFile($"[GLBImporter] Import complete: {result.PrefabPath}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                AtlasLogger.LogError($"[GLBImporter] Import failed: {ex.Message}");
                AtlasLogger.LogException(ex, "GLB Import");
            }

            return result;
        }

        #endregion

        #region GLB Parsing

        private class GLBData
        {
            public JObject Json;
            public byte[] BinaryChunk;
        }

        private static GLBData ParseGLB(string glbPath)
        {
            byte[] fileData = File.ReadAllBytes(glbPath);

            // Validate GLB header
            if (fileData.Length < 12)
                throw new Exception("File too small to be a valid GLB");

            uint magic = BitConverter.ToUInt32(fileData, 0);
            if (magic != 0x46546C67) // "glTF" in little-endian
                throw new Exception("Invalid GLB file (magic number mismatch)");

            uint version = BitConverter.ToUInt32(fileData, 4);
            if (version != 2)
                AtlasLogger.LogWarning($"[GLBImporter] GLB version {version} (expected 2)");

            var result = new GLBData();

            // Parse chunks
            int offset = 12;
            while (offset < fileData.Length)
            {
                if (offset + 8 > fileData.Length)
                    break;

                uint chunkLength = BitConverter.ToUInt32(fileData, offset);
                uint chunkType = BitConverter.ToUInt32(fileData, offset + 4);

                if (offset + 8 + chunkLength > fileData.Length)
                    throw new Exception("Chunk extends beyond file boundary");

                if (chunkType == 0x4E4F534A) // "JSON"
                {
                    string jsonStr = Encoding.UTF8.GetString(fileData, offset + 8, (int)chunkLength);
                    result.Json = JObject.Parse(jsonStr);
                }
                else if (chunkType == 0x004E4942) // "BIN\0"
                {
                    result.BinaryChunk = new byte[chunkLength];
                    Array.Copy(fileData, offset + 8, result.BinaryChunk, 0, chunkLength);
                }

                offset += 8 + (int)chunkLength;
                
                // Align to 4-byte boundary
                int padding = (4 - (offset % 4)) % 4;
                offset += padding;
            }

            if (result.Json == null)
                throw new Exception("No JSON chunk found in GLB file");

            return result;
        }

        #endregion

        #region Texture Extraction

        private static Dictionary<string, Texture2D> ExtractAndSaveTextures(GLBData glb, string outputFolder, string assetName)
        {
            var textures = new Dictionary<string, Texture2D>();
            
            var images = glb.Json["images"] as JArray;
            var bufferViews = glb.Json["bufferViews"] as JArray;

            if (images == null || bufferViews == null)
                return textures;

            for (int i = 0; i < images.Count; i++)
            {
                var image = images[i];
                int? bvIndex = image["bufferView"]?.Value<int>();
                
                if (bvIndex == null)
                {
                    // External URI reference - not supported for now
                    AtlasLogger.LogWarning($"[GLBImporter] Image {i} uses external URI (not supported)");
                    continue;
                }

                if (glb.BinaryChunk == null)
                {
                    AtlasLogger.LogWarning("[GLBImporter] No binary chunk to extract textures from");
                    continue;
                }

                var bv = bufferViews[bvIndex.Value];
                int byteOffset = bv["byteOffset"]?.Value<int>() ?? 0;
                int byteLength = bv["byteLength"].Value<int>();

                // Extract image bytes
                byte[] imageBytes = new byte[byteLength];
                Array.Copy(glb.BinaryChunk, byteOffset, imageBytes, 0, byteLength);

                // Create texture
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!tex.LoadImage(imageBytes))
                {
                    AtlasLogger.LogWarning($"[GLBImporter] Failed to decode image {i}");
                    UnityEngine.Object.DestroyImmediate(tex);
                    continue;
                }

                // Determine texture name
                string texName = image["name"]?.ToString();
                if (string.IsNullOrEmpty(texName))
                    texName = $"{assetName}_Tex{i}";
                else
                    texName = SanitizeAssetName(texName);

                // Save as PNG
                string texPath = $"{outputFolder}/{texName}.png";
                texPath = AssetDatabase.GenerateUniqueAssetPath(texPath);
                
                byte[] pngData = tex.EncodeToPNG();
                File.WriteAllBytes(texPath, pngData);
                
                UnityEngine.Object.DestroyImmediate(tex);

                // Import and load as asset
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
                
                Texture2D loadedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (loadedTex != null)
                {
                    textures[$"image_{i}"] = loadedTex;
                    AtlasLogger.LogFile($"[GLBImporter] Extracted texture: {texPath} ({loadedTex.width}x{loadedTex.height})");
                }
            }

            return textures;
        }

        private static void ConfigureTextureSettings(GLBData glb, Dictionary<string, Texture2D> textures)
        {
            var materials = glb.Json["materials"] as JArray;
            var gltfTextures = glb.Json["textures"] as JArray;

            if (materials == null || gltfTextures == null || materials.Count == 0)
                return;

            // Check first material for normal map
            var mat = materials[0];
            var normalTex = mat["normalTexture"];
            
            if (normalTex == null)
                return;

            int texIndex = normalTex["index"]?.Value<int>() ?? -1;
            if (texIndex < 0 || texIndex >= gltfTextures.Count)
                return;

            int imageIndex = gltfTextures[texIndex]["source"]?.Value<int>() ?? -1;
            string key = $"image_{imageIndex}";
            
            if (!textures.ContainsKey(key))
                return;

            Texture2D normalMapTex = textures[key];
            string texPath = AssetDatabase.GetAssetPath(normalMapTex);

            TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
                AtlasLogger.LogFile($"[GLBImporter] Configured as Normal Map: {texPath}");
            }
        }

        #endregion

        #region Material Creation

        private static Material CreateMaterial(GLBData glb, Dictionary<string, Texture2D> textures, string outputFolder, string assetName)
        {
            // Find appropriate shader
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                throw new Exception("No suitable shader found (URP/Lit or Standard)");

            Material mat = new Material(shader);
            mat.name = $"{assetName}_Material";

            var materials = glb.Json["materials"] as JArray;
            var gltfTextures = glb.Json["textures"] as JArray;

            if (materials != null && materials.Count > 0 && gltfTextures != null)
            {
                var gltfMat = materials[0];
                var pbr = gltfMat["pbrMetallicRoughness"];

                if (pbr != null)
                {
                    // Base color texture
                    AssignTexture(mat, pbr["baseColorTexture"], gltfTextures, textures,
                        "_BaseMap", "_MainTex", null);

                    // Metallic/Roughness texture
                    AssignTexture(mat, pbr["metallicRoughnessTexture"], gltfTextures, textures,
                        "_MetallicGlossMap", null, "_METALLICGLOSSMAP");

                    // Base color factor
                    var baseColorFactor = pbr["baseColorFactor"] as JArray;
                    if (baseColorFactor != null && baseColorFactor.Count >= 4)
                    {
                        Color color = new Color(
                            baseColorFactor[0].Value<float>(),
                            baseColorFactor[1].Value<float>(),
                            baseColorFactor[2].Value<float>(),
                            baseColorFactor[3].Value<float>()
                        );
                        mat.SetColor("_BaseColor", color);
                        mat.SetColor("_Color", color);
                    }

                    // Metallic/Roughness factors
                    float metallic = pbr["metallicFactor"]?.Value<float>() ?? 1.0f;
                    float roughness = pbr["roughnessFactor"]?.Value<float>() ?? 1.0f;
                    mat.SetFloat("_Metallic", metallic);
                    mat.SetFloat("_Smoothness", 1.0f - roughness); // Unity uses smoothness, not roughness
                }

                // Normal texture
                AssignTexture(mat, gltfMat["normalTexture"], gltfTextures, textures,
                    "_BumpMap", null, "_NORMALMAP");

                // Occlusion texture
                AssignTexture(mat, gltfMat["occlusionTexture"], gltfTextures, textures,
                    "_OcclusionMap", null, null);

                // Emissive texture
                AssignTexture(mat, gltfMat["emissiveTexture"], gltfTextures, textures,
                    "_EmissionMap", null, "_EMISSION");

                // Emissive factor
                var emissiveFactor = gltfMat["emissiveFactor"] as JArray;
                if (emissiveFactor != null && emissiveFactor.Count >= 3)
                {
                    Color emissive = new Color(
                        emissiveFactor[0].Value<float>(),
                        emissiveFactor[1].Value<float>(),
                        emissiveFactor[2].Value<float>()
                    );
                    mat.SetColor("_EmissionColor", emissive);
                }
            }

            // Save material
            string matPath = $"{outputFolder}/{assetName}_Material.mat";
            matPath = AssetDatabase.GenerateUniqueAssetPath(matPath);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        private static void AssignTexture(Material mat, JToken textureInfo, JArray gltfTextures, 
            Dictionary<string, Texture2D> textures, string propertyName, string fallbackProperty, string keyword)
        {
            if (textureInfo == null)
                return;

            int texIndex = textureInfo["index"]?.Value<int>() ?? -1;
            if (texIndex < 0 || texIndex >= gltfTextures.Count)
                return;

            int imageIndex = gltfTextures[texIndex]["source"]?.Value<int>() ?? -1;
            string key = $"image_{imageIndex}";

            if (!textures.ContainsKey(key))
                return;

            Texture2D tex = textures[key];
            mat.SetTexture(propertyName, tex);
            
            if (!string.IsNullOrEmpty(fallbackProperty))
                mat.SetTexture(fallbackProperty, tex);

            if (!string.IsNullOrEmpty(keyword))
                mat.EnableKeyword(keyword);
        }

        #endregion

        #region Mesh Extraction

        private static Mesh ExtractMesh(GLBData glb, string outputFolder, string assetName)
        {
            var meshes = glb.Json["meshes"] as JArray;
            if (meshes == null || meshes.Count == 0)
                throw new Exception("No meshes found in GLB file");

            var gltfMesh = meshes[0];
            var primitives = gltfMesh["primitives"] as JArray;
            if (primitives == null || primitives.Count == 0)
                throw new Exception("No mesh primitives found");

            var primitive = primitives[0];
            var attributes = primitive["attributes"];

            var accessors = glb.Json["accessors"] as JArray;
            var bufferViews = glb.Json["bufferViews"] as JArray;

            if (accessors == null || bufferViews == null)
                throw new Exception("Missing accessors or bufferViews");

            // Read vertex data
            int posAccessor = attributes["POSITION"].Value<int>();
            Vector3[] positions = ReadVec3Accessor(glb, accessors, bufferViews, posAccessor);

            Vector3[] normals = null;
            if (attributes["NORMAL"] != null)
            {
                normals = ReadVec3Accessor(glb, accessors, bufferViews, attributes["NORMAL"].Value<int>());
            }

            Vector2[] uvs = null;
            if (attributes["TEXCOORD_0"] != null)
            {
                uvs = ReadVec2Accessor(glb, accessors, bufferViews, attributes["TEXCOORD_0"].Value<int>());
            }

            int[] indices = null;
            if (primitive["indices"] != null)
            {
                indices = ReadIndicesAccessor(glb, accessors, bufferViews, primitive["indices"].Value<int>());
            }
            else
            {
                // Generate sequential indices if not provided
                indices = new int[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                    indices[i] = i;
            }

            // Create Unity Mesh
            Mesh mesh = new Mesh();
            mesh.name = gltfMesh["name"]?.ToString() ?? $"{assetName}_Mesh";

            // Use 32-bit indices for large meshes
            if (positions.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = positions;
            
            if (normals != null)
                mesh.normals = normals;
            
            if (uvs != null)
                mesh.uv = uvs;

            mesh.triangles = indices;

            // Recalculate missing data
            if (normals == null)
                mesh.RecalculateNormals();

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            AtlasLogger.LogFile($"[GLBImporter] Mesh: {positions.Length} vertices, {indices.Length / 3} triangles");

            // Save mesh asset
            string meshPath = $"{outputFolder}/{assetName}_Mesh.asset";
            meshPath = AssetDatabase.GenerateUniqueAssetPath(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        }

        private static Vector3[] ReadVec3Accessor(GLBData glb, JArray accessors, JArray bufferViews, int accessorIndex)
        {
            var accessor = accessors[accessorIndex];
            int count = accessor["count"].Value<int>();
            int bvIndex = accessor["bufferView"].Value<int>();
            int accOffset = accessor["byteOffset"]?.Value<int>() ?? 0;

            var bv = bufferViews[bvIndex];
            int bvOffset = bv["byteOffset"]?.Value<int>() ?? 0;
            int stride = bv["byteStride"]?.Value<int>() ?? 12; // Default for Vec3 float

            Vector3[] result = new Vector3[count];
            int dataOffset = bvOffset + accOffset;

            for (int i = 0; i < count; i++)
            {
                int offset = dataOffset + i * stride;
                float x = BitConverter.ToSingle(glb.BinaryChunk, offset);
                float y = BitConverter.ToSingle(glb.BinaryChunk, offset + 4);
                float z = BitConverter.ToSingle(glb.BinaryChunk, offset + 8);

                // Convert from GLTF (right-handed) to Unity (left-handed)
                result[i] = new Vector3(x, y, -z);
            }

            return result;
        }

        private static Vector2[] ReadVec2Accessor(GLBData glb, JArray accessors, JArray bufferViews, int accessorIndex)
        {
            var accessor = accessors[accessorIndex];
            int count = accessor["count"].Value<int>();
            int bvIndex = accessor["bufferView"].Value<int>();
            int accOffset = accessor["byteOffset"]?.Value<int>() ?? 0;

            var bv = bufferViews[bvIndex];
            int bvOffset = bv["byteOffset"]?.Value<int>() ?? 0;
            int stride = bv["byteStride"]?.Value<int>() ?? 8; // Default for Vec2 float

            Vector2[] result = new Vector2[count];
            int dataOffset = bvOffset + accOffset;

            for (int i = 0; i < count; i++)
            {
                int offset = dataOffset + i * stride;
                float u = BitConverter.ToSingle(glb.BinaryChunk, offset);
                float v = BitConverter.ToSingle(glb.BinaryChunk, offset + 4);

                // Flip V coordinate (GLTF origin at top-left, Unity at bottom-left)
                result[i] = new Vector2(u, 1f - v);
            }

            return result;
        }

        private static int[] ReadIndicesAccessor(GLBData glb, JArray accessors, JArray bufferViews, int accessorIndex)
        {
            var accessor = accessors[accessorIndex];
            int count = accessor["count"].Value<int>();
            int componentType = accessor["componentType"].Value<int>();
            int bvIndex = accessor["bufferView"].Value<int>();
            int accOffset = accessor["byteOffset"]?.Value<int>() ?? 0;

            var bv = bufferViews[bvIndex];
            int bvOffset = bv["byteOffset"]?.Value<int>() ?? 0;

            int[] result = new int[count];
            int dataOffset = bvOffset + accOffset;

            // Read indices based on component type
            for (int i = 0; i < count; i++)
            {
                switch (componentType)
                {
                    case 5121: // UNSIGNED_BYTE
                        result[i] = glb.BinaryChunk[dataOffset + i];
                        break;
                    case 5123: // UNSIGNED_SHORT
                        result[i] = BitConverter.ToUInt16(glb.BinaryChunk, dataOffset + i * 2);
                        break;
                    case 5125: // UNSIGNED_INT
                        result[i] = (int)BitConverter.ToUInt32(glb.BinaryChunk, dataOffset + i * 4);
                        break;
                    default:
                        throw new Exception($"Unsupported index component type: {componentType}");
                }
            }

            // Reverse winding order (GLTF CCW -> Unity CW)
            for (int i = 0; i < result.Length; i += 3)
            {
                int temp = result[i];
                result[i] = result[i + 2];
                result[i + 2] = temp;
            }

            return result;
        }

        #endregion

        #region Prefab Creation

        private static GameObject CreatePrefab(Mesh mesh, Material material, string outputFolder, string assetName)
        {
            // Create temporary GameObject
            GameObject go = new GameObject(assetName);
            
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();

            mf.sharedMesh = mesh;
            mr.sharedMaterial = material;

            // Save as prefab
            string prefabPath = $"{outputFolder}/{assetName}.prefab";
            prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
            
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            
            // Clean up temporary object
            UnityEngine.Object.DestroyImmediate(go);

            return prefab;
        }

        #endregion

        #region Utilities

        private static void EnsureFolderExists(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string folderName = Path.GetFileName(path);

            if (string.IsNullOrEmpty(parent))
                parent = "Assets";

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolderExists(parent);

            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static string SanitizeAssetName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Unnamed";

            // Remove invalid characters
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            // Remove other problematic characters
            name = name.Replace(' ', '_');

            return name;
        }

        #endregion
    }
}
