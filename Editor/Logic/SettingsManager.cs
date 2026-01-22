// In Packages/com.atlas.workflow/Editor/Logic/SettingsManager.cs

using UnityEditor;
using UnityEngine;
using System.IO;

public static class SettingsManager
{
    // EditorPrefs keys
    private const string SavePathKey = "AtlasWorkflow_SavePath";
    private const string VerboseLoggingKey = "AtlasWorkflow_VerboseLogging";
    private const string ApiTimeoutKey = "AtlasWorkflow_ApiTimeout";
    private const string NotifyOnJobCompleteKey = "AtlasWorkflow_NotifyOnJobComplete";
    private const string MaxTempStorageMBKey = "AtlasWorkflow_MaxTempStorageMB";
    private const string WarnOnTempExceededKey = "AtlasWorkflow_WarnOnTempExceeded";
    
    // Default values
    private const string DefaultSavePath = "Assets/AtlasOutputs";
    
    /// <summary>
    /// Default API timeout in minutes.
    /// </summary>
    public const int DefaultApiTimeoutMinutes = 10;
    
    /// <summary>
    /// Maximum configurable timeout in minutes (1 hour).
    /// </summary>
    public const int MaxApiTimeoutMinutes = 60;
    
    /// <summary>
    /// Special value indicating no timeout limit.
    /// </summary>
    public const int NoTimeoutValue = -1;
    
    /// <summary>
    /// Default max temp storage in MB.
    /// </summary>
    public const int DefaultMaxTempStorageMB = 500;

    #region Save Path

    /// <summary>
    /// Gets the currently configured save path.
    /// If no path is set, it returns the default and creates the folder.
    /// </summary>
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

    /// <summary>
    /// Sets a new save path. Validates that the path is inside the Assets folder.
    /// </summary>
    public static void SetSavePath(string newPath)
    {
        if (string.IsNullOrEmpty(newPath)) return;

        // Standardize the path format
        string validatedPath = newPath.Replace("\\", "/");

        if (!validatedPath.StartsWith("Assets/"))
        {
            AtlasLogger.LogError("Save path must be inside the project's Assets folder.");
            return;
        }

        EditorPrefs.SetString(SavePathKey, validatedPath);
        AtlasLogger.Log($"Save path set to: {validatedPath}");
    }

    #endregion

    #region Verbose Logging

    /// <summary>
    /// Gets whether verbose logging is enabled.
    /// </summary>
    public static bool GetVerboseLogging()
    {
        return EditorPrefs.GetBool(VerboseLoggingKey, false);
    }

    /// <summary>
    /// Sets whether verbose logging is enabled.
    /// </summary>
    public static void SetVerboseLogging(bool enabled)
    {
        EditorPrefs.SetBool(VerboseLoggingKey, enabled);
    }

    #endregion

    #region API Timeout

    /// <summary>
    /// Gets the API timeout in minutes.
    /// Returns NoTimeoutValue (-1) if no limit is set.
    /// </summary>
    public static int GetApiTimeoutMinutes()
    {
        return EditorPrefs.GetInt(ApiTimeoutKey, DefaultApiTimeoutMinutes);
    }

    /// <summary>
    /// Sets the API timeout in minutes.
    /// Use NoTimeoutValue (-1) for no limit.
    /// </summary>
    public static void SetApiTimeoutMinutes(int minutes)
    {
        // Validate: either NoTimeoutValue or within valid range
        if (minutes != NoTimeoutValue)
        {
            minutes = Mathf.Clamp(minutes, 1, MaxApiTimeoutMinutes);
        }
        EditorPrefs.SetInt(ApiTimeoutKey, minutes);
        AtlasLogger.Log($"API timeout set to: {FormatTimeoutDisplay(minutes)}");
    }

    /// <summary>
    /// Gets the API timeout as a TimeSpan.
    /// Returns null if no limit is set.
    /// </summary>
    public static System.TimeSpan? GetApiTimeout()
    {
        int minutes = GetApiTimeoutMinutes();
        if (minutes == NoTimeoutValue)
            return null;
        return System.TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// Formats the timeout value for display.
    /// </summary>
    public static string FormatTimeoutDisplay(int minutes)
    {
        if (minutes == NoTimeoutValue)
            return "No Limit";
        if (minutes == 1)
            return "1 minute";
        if (minutes < 60)
            return $"{minutes} minutes";
        if (minutes == 60)
            return "1 hour";
        return $"{minutes} minutes";
    }

    #endregion

    #region Notifications

    /// <summary>
    /// Gets whether to notify when a job completes.
    /// </summary>
    public static bool GetNotifyOnJobComplete()
    {
        return EditorPrefs.GetBool(NotifyOnJobCompleteKey, true);
    }

    /// <summary>
    /// Sets whether to notify when a job completes.
    /// </summary>
    public static void SetNotifyOnJobComplete(bool enabled)
    {
        EditorPrefs.SetBool(NotifyOnJobCompleteKey, enabled);
    }

    #endregion

    #region Storage Settings

    /// <summary>
    /// Gets the maximum temp storage size in MB.
    /// </summary>
    public static int GetMaxTempStorageMB()
    {
        return EditorPrefs.GetInt(MaxTempStorageMBKey, DefaultMaxTempStorageMB);
    }

    /// <summary>
    /// Sets the maximum temp storage size in MB.
    /// </summary>
    public static void SetMaxTempStorageMB(int megabytes)
    {
        megabytes = Mathf.Clamp(megabytes, 100, 5000); // 100MB to 5GB
        EditorPrefs.SetInt(MaxTempStorageMBKey, megabytes);
    }

    /// <summary>
    /// Gets whether to warn when temp storage exceeds the limit.
    /// </summary>
    public static bool GetWarnOnTempExceeded()
    {
        return EditorPrefs.GetBool(WarnOnTempExceededKey, true);
    }

    /// <summary>
    /// Sets whether to warn when temp storage exceeds the limit.
    /// </summary>
    public static void SetWarnOnTempExceeded(bool enabled)
    {
        EditorPrefs.SetBool(WarnOnTempExceededKey, enabled);
    }

    /// <summary>
    /// Checks if temp storage exceeds the configured limit and logs a warning if enabled.
    /// </summary>
    public static void CheckTempStorageLimit()
    {
        if (!GetWarnOnTempExceeded()) return;

        var (fileCount, totalBytes) = AssetExporter.GetTempDirectoryInfo();
        long maxBytes = GetMaxTempStorageMB() * 1024L * 1024L;

        if (totalBytes > maxBytes)
        {
            AtlasLogger.LogWarning(
                $"Temporary files ({AssetExporter.FormatBytes(totalBytes)}) exceed the configured limit " +
                $"({GetMaxTempStorageMB()} MB). Consider cleaning up in Project Settings > Atlas Workflow.");
        }
    }

    #endregion
}