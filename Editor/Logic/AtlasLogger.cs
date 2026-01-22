// In Packages/com.atlas.workflow/Editor/Logic/AtlasLogger.cs

using UnityEngine;

/// <summary>
/// Centralized logging utility for the Atlas Workflow plugin.
/// Verbose logs can be toggled via Project Settings > Atlas Workflow.
/// </summary>
public static class AtlasLogger
{
    private const string LogPrefix = "[Atlas]";

    /// <summary>
    /// Logs an informational message. Only outputs if verbose logging is enabled.
    /// </summary>
    public static void Log(string message)
    {
        if (SettingsManager.GetVerboseLogging())
            Debug.Log($"{LogPrefix} {message}");
    }

    /// <summary>
    /// Logs a warning message. Always outputs regardless of verbose setting.
    /// </summary>
    public static void LogWarning(string message)
    {
        Debug.LogWarning($"{LogPrefix} {message}");
    }

    /// <summary>
    /// Logs an error message. Always outputs regardless of verbose setting.
    /// </summary>
    public static void LogError(string message)
    {
        Debug.LogError($"{LogPrefix} {message}");
    }

    /// <summary>
    /// Logs an exception with context. Always outputs regardless of verbose setting.
    /// </summary>
    public static void LogException(System.Exception ex, string context = null)
    {
        if (string.IsNullOrEmpty(context))
            Debug.LogException(ex);
        else
            Debug.LogError($"{LogPrefix} {context}: {ex.Message}\n{ex.StackTrace}");
    }

    /// <summary>
    /// Logs a verbose message for API operations. Only outputs if verbose logging is enabled.
    /// </summary>
    public static void LogAPI(string message)
    {
        if (SettingsManager.GetVerboseLogging())
            Debug.Log($"{LogPrefix} [API] {message}");
    }

    /// <summary>
    /// Logs a verbose message for job operations. Only outputs if verbose logging is enabled.
    /// </summary>
    public static void LogJob(string message)
    {
        if (SettingsManager.GetVerboseLogging())
            Debug.Log($"{LogPrefix} [Job] {message}");
    }

    /// <summary>
    /// Logs a verbose message for file operations. Only outputs if verbose logging is enabled.
    /// </summary>
    public static void LogFile(string message)
    {
        if (SettingsManager.GetVerboseLogging())
            Debug.Log($"{LogPrefix} [File] {message}");
    }
}
