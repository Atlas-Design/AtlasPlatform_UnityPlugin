// In Packages/com.atlas.workflow/Editor/Logic/AssetExporter.cs

using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Export;
using UnityEditor;
using UnityEngine;

public static class AssetExporter
{
    #region Texture Operations

    /// <summary>
    /// blits a texture to a temporary RenderTexture to uncompress it, 
    /// reads the pixels, encodes to PNG, and saves to a temp file.
    /// </summary>
    public static async Task<string> ExportTextureAsPng(Texture2D texture)
    {
        if (texture == null)
        {
            //Debug.LogError("Export failed: Texture is null.");
            return null;
        }

        // --- Step 1: Create a temporary RenderTexture ---
        // This acts as a temporary, uncompressed "canvas" in GPU memory.
        RenderTexture tmp = RenderTexture.GetTemporary(
            texture.width,
            texture.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear);

        // --- Step 2: Blit (copy) the texture to the RenderTexture ---
        // This uses the GPU to decompress the texture and draw it onto our canvas.
        Graphics.Blit(texture, tmp);

        // --- Step 3: Read the pixels back from the RenderTexture ---
        RenderTexture previous = RenderTexture.active; // Save the currently active render texture
        RenderTexture.active = tmp;                   // Set our temporary canvas as the active one

        // Create a new, readable Texture2D to receive the pixels
        Texture2D readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        readableTexture.Apply();

        RenderTexture.active = previous;              // Restore the original active render texture
        RenderTexture.ReleaseTemporary(tmp);          // Release the temporary canvas

        // --- Step 4: Encode the uncompressed texture to PNG ---
        byte[] pngData = readableTexture.EncodeToPNG();

        // Clean up the temporary Texture2D object we created
        Object.DestroyImmediate(readableTexture);

        // --- Step 5: Save the PNG file ---
        string tempFilePath = Path.Combine(GetTempDirectory(), $"{texture.name}_{System.Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(tempFilePath, pngData);

        return tempFilePath;
    }

    #endregion

    #region Model Operations

    /// <summary>
    /// Configures GLTFast settings and exports the provided GameObject to a binary .glb file.
    /// </summary>
    public static async Task<string> ExportGameObjectAsGlb(GameObject go)
    {
        if (go == null)
        {
            //Debug.LogError("Export failed: GameObject is null.");
            return null;
        }

        string tempFilePath = Path.Combine(
            GetTempDirectory(),
            $"{go.name}_{System.Guid.NewGuid()}.glb"
        );
        var exportSettings = new ExportSettings
        {
            Format = GltfFormat.Binary,
            FileConflictResolution = FileConflictResolution.Overwrite,
            ImageDestination = ImageDestination.Automatic

            // ComponentMask, LightIntensityFactor, etc. if you care
        };

        var goExportSettings = new GameObjectExportSettings
        {
            OnlyActiveInHierarchy = false
        };
        // GameObjectExport is the correct type from GLTFast.Export
        var export = new GameObjectExport(exportSettings, goExportSettings);

        // You can keep the AddScene call exactly like this
        export.AddScene(new[] { go });

        bool success = await export.SaveToFileAndDispose(tempFilePath);

        if (success)
        {
            return tempFilePath;
        }

        //Debug.LogError($"Failed to export {go.name} to GLB.");
        return null;
    }

    #endregion

    #region File System Utilities

    /// <summary>
    /// Gets the path to the temporary directory, creating it if it doesn't exist.
    /// </summary>
    public static string GetTempDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "UnityAtlasWorkflow");
        if (!Directory.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }
        return tempDir;
    }

    #endregion
}