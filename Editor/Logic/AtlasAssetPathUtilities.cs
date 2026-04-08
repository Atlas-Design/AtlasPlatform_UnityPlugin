using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Resolves Unity <c>Assets/...</c> paths to absolute disk paths and ensures directories exist
/// before <see cref="File.Copy"/> / <see cref="File.WriteAllBytes"/> (AssetDatabase alone is not enough).
/// </summary>
public static class AtlasAssetPathUtilities
{
    public static string NormalizeToAssetsRelative(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        path = path.Replace('\\', '/').TrimEnd('/');
        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return path;

        string data = Application.dataPath.Replace('\\', '/');
        if (path.StartsWith(data, StringComparison.OrdinalIgnoreCase))
        {
            string rest = path.Substring(data.Length).TrimStart('/');
            return string.IsNullOrEmpty(rest) ? "Assets" : "Assets/" + rest;
        }

        return path;
    }

    public static string AssetPathToAbsolute(string assetPath)
    {
        assetPath = NormalizeToAssetsRelative(assetPath);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Atlas: path must be under Assets (got: {assetPath})");

        string tail = assetPath.Substring("Assets/".Length);
        if (string.IsNullOrEmpty(tail))
            return Application.dataPath;

        return Path.GetFullPath(Path.Combine(Application.dataPath,
            tail.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Creates the parent folder of an asset file on disk (and all parents). Safe to call repeatedly.
    /// </summary>
    public static void EnsureParentDirectoryExistsOnDisk(string assetFilePath)
    {
        assetFilePath = NormalizeToAssetsRelative(assetFilePath.Replace('\\', '/'));
        int slash = assetFilePath.LastIndexOf('/');
        if (slash <= 0)
            return;

        string folderAsset = assetFilePath.Substring(0, slash);
        string absFolder = AssetPathToAbsolute(folderAsset);
        Directory.CreateDirectory(absFolder);
    }
}
