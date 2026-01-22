// In Packages/com.atlas.workflow/Editor/Logic/SettingsManager.cs

using UnityEditor;
using UnityEngine;
using System.IO;

public static class SettingsManager
{
    private const string SavePathKey = "AtlasWorkflow_SavePath";
    private const string DefaultSavePath = "Assets/AtlasOutputs";

    // Gets the currently configured save path.
    // If no path is set, it returns the default and creates the folder.
    public static string GetSavePath()
    {
        string path = EditorPrefs.GetString(SavePathKey, DefaultSavePath);

        // Ensure the directory exists
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    // Sets a new save path. It validates that the path is inside the Assets folder.
    public static void SetSavePath(string newPath)
    {
        if (string.IsNullOrEmpty(newPath)) return;

        // Standardize the path format
        string validatedPath = newPath.Replace("\\", "/");

        if (!validatedPath.StartsWith("Assets/"))
        {
            //Debug.LogError("Save path must be inside the project's Assets folder.");
            return;
        }

        EditorPrefs.SetString(SavePathKey, validatedPath);
        //Debug.Log($"Atlas save path set to: {validatedPath}");
    }
}