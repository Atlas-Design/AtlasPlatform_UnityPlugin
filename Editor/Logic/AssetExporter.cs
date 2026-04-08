// In Packages/com.atlas.workflow/Editor/Logic/AssetExporter.cs

using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Export;
using UnityEditor;
using UnityEngine;

public static class AssetExporter
{
    /// <summary>
    /// Default max age for temp files before cleanup (7 days).
    /// </summary>
    private static readonly TimeSpan DefaultTempFileMaxAge = TimeSpan.FromDays(7);

    #region Texture Operations

    /// <summary>
    /// Blits a texture to a temporary RenderTexture to uncompress it, 
    /// reads the pixels, encodes to PNG, and saves to a temp file.
    /// </summary>
    public static async Task<string> ExportTextureAsPng(Texture2D texture)
    {
        if (texture == null)
        {
            AtlasLogger.LogError("ExportTextureAsPng failed: Texture is null.");
            return null;
        }

        AtlasLogger.LogFile($"Exporting texture '{texture.name}' as PNG...");

        RenderTexture tmp = null;
        Texture2D readableTexture = null;
        RenderTexture previous = RenderTexture.active;

        try
        {
            tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);

            Graphics.Blit(texture, tmp);

            RenderTexture.active = tmp;

            readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableTexture.Apply();

            byte[] pngData = readableTexture.EncodeToPNG();

            string tempFilePath = Path.Combine(GetTempDirectory(), $"{texture.name}_{System.Guid.NewGuid()}.png");
            await File.WriteAllBytesAsync(tempFilePath, pngData);

            AtlasLogger.LogFile($"Exported texture to: {tempFilePath} ({pngData.Length} bytes)");
            return tempFilePath;
        }
        finally
        {
            RenderTexture.active = previous;
            if (tmp != null)
                RenderTexture.ReleaseTemporary(tmp);
            if (readableTexture != null)
                UnityEngine.Object.DestroyImmediate(readableTexture);
        }
    }

    #endregion

    #region Model Operations

    /// <summary>
    /// Exports the provided GameObject to a binary .glb file using GLTFast.
    /// Used for uploading mesh inputs to the Atlas API.
    /// </summary>
    public static async Task<string> ExportGameObjectAsGlb(GameObject go)
    {
        if (go == null)
        {
            AtlasLogger.LogError("ExportGameObjectAsGlb failed: GameObject is null.");
            return null;
        }

        AtlasLogger.LogFile($"Exporting GameObject '{go.name}' as GLB...");

        string tempFilePath = Path.Combine(
            GetTempDirectory(),
            $"{go.name}_{System.Guid.NewGuid()}.glb"
        );
        
        var exportSettings = new ExportSettings
        {
            Format = GltfFormat.Binary,
            FileConflictResolution = FileConflictResolution.Overwrite,
            ImageDestination = ImageDestination.Automatic
        };

        var goExportSettings = new GameObjectExportSettings
        {
            OnlyActiveInHierarchy = false
        };
        
        var export = new GameObjectExport(exportSettings, goExportSettings);
        export.AddScene(new[] { go });

        bool success = await export.SaveToFileAndDispose(tempFilePath);

        if (success)
        {
            AtlasLogger.LogFile($"Exported GLB to: {tempFilePath}");
            return tempFilePath;
        }

        AtlasLogger.LogError($"Failed to export '{go.name}' to GLB.");
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

    /// <summary>
    /// Deletes all files in the temp directory older than the specified age.
    /// Uses the default max age if not specified.
    /// </summary>
    /// <param name="maxAge">Maximum age of files to keep. Files older than this will be deleted.</param>
    /// <returns>The number of files deleted.</returns>
    public static int CleanupTempFiles(TimeSpan? maxAge = null)
    {
        var age = maxAge ?? DefaultTempFileMaxAge;
        int deletedCount = 0;

        try
        {
            string tempDir = GetTempDirectory();
            if (!Directory.Exists(tempDir))
                return 0;

            var cutoffTime = DateTime.UtcNow - age;
            var files = Directory.GetFiles(tempDir);

            foreach (var filePath in files)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.CreationTimeUtc < cutoffTime)
                    {
                        fileInfo.Delete();
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue - don't let one file failure stop the cleanup
                    AtlasLogger.LogWarning($"Failed to delete temp file '{Path.GetFileName(filePath)}': {ex.Message}");
                }
            }

            if (deletedCount > 0)
            {
                AtlasLogger.LogFile($"Cleaned up {deletedCount} temp file(s) older than {age.TotalDays:F0} days.");
            }
        }
        catch (Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to cleanup temp files");
        }

        return deletedCount;
    }

    /// <summary>
    /// Deletes the entire temp directory and all its contents.
    /// Use with caution - this will remove ALL temp files regardless of age.
    /// </summary>
    /// <returns>True if the directory was successfully deleted or didn't exist.</returns>
    public static bool ClearAllTempFiles()
    {
        try
        {
            string tempDir = GetTempDirectory();
            if (!Directory.Exists(tempDir))
                return true;

            // Count files for logging
            int fileCount = Directory.GetFiles(tempDir).Length;

            Directory.Delete(tempDir, recursive: true);

            AtlasLogger.LogFile($"Cleared all temp files ({fileCount} file(s)).");
            return true;
        }
        catch (Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to clear temp directory");
            return false;
        }
    }

    /// <summary>
    /// Gets information about the current temp directory usage.
    /// </summary>
    /// <returns>A tuple containing (file count, total size in bytes).</returns>
    public static (int FileCount, long TotalBytes) GetTempDirectoryInfo()
    {
        try
        {
            string tempDir = GetTempDirectory();
            if (!Directory.Exists(tempDir))
                return (0, 0);

            var files = Directory.GetFiles(tempDir);
            long totalBytes = 0;

            foreach (var filePath in files)
            {
                try
                {
                    totalBytes += new FileInfo(filePath).Length;
                }
                catch
                {
                    // Ignore individual file errors
                }
            }

            return (files.Length, totalBytes);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Formats a byte count as a human-readable string.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F1} {suffixes[suffixIndex]}";
    }

    #endregion
}