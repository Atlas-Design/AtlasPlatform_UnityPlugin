// In Packages/com.atlas.workflow/Editor/Logic/AtlasPlatformAuth.cs

using System;
using System.Net.Http;

/// <summary>
/// Workspace API key resolution and request authentication for Atlas Platform v0.2+.
/// </summary>
public static class AtlasPlatformAuth
{
    public const string ApiKeyEnvironmentVariableName = "ATLAS_API_KEY";
    public const string LegacyApiKeyEnvironmentVariableName = "API_KEY";

    public static bool HasConfiguredApiKey()
    {
        return !string.IsNullOrWhiteSpace(GetResolvedApiKey());
    }

    /// <summary>
    /// Project settings first, then environment variables when enabled.
    /// </summary>
    public static string GetResolvedApiKey()
    {
        string key = SettingsManager.GetWorkspaceApiKey();
        if (!string.IsNullOrWhiteSpace(key))
            return key.Trim();

        if (!SettingsManager.GetReadApiKeyFromEnvironment())
            return string.Empty;

        key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(key))
            return key.Trim();

        key = Environment.GetEnvironmentVariable(LegacyApiKeyEnvironmentVariableName);
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
    }

    /// <summary>
    /// v0.2+ endpoints require a workspace API key.
    /// </summary>
    public static bool RequiresApiKey(string version)
    {
        return AtlasApiVersion.UsesWorkspaceScopedFileApi(version);
    }

    public static bool TryValidateForRun(string version, out string errorMessage)
    {
        if (!RequiresApiKey(version) || HasConfiguredApiKey())
        {
            errorMessage = null;
            return true;
        }

        errorMessage = GetConfigureApiKeyMessage();
        return false;
    }

    public static void ApplyAuthHeaders(HttpRequestMessage request)
    {
        if (request == null)
            return;

        string apiKey = GetResolvedApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return;

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
    }

    public static string GetConfigureApiKeyMessage()
    {
        return "Atlas workspace API key is required for API v0.2+. Set it in Atlas → Atlas Workflow Settings " +
               "(Authentication), or set the ATLAS_API_KEY environment variable.";
    }

    public static string FormatHttpFailure(int statusCode, string fallback)
    {
        switch (statusCode)
        {
            case 401:
                return $"Authentication failed (401). {GetConfigureApiKeyMessage()}";
            case 403:
                return "Access denied (403). Verify your API key has access to this workspace.";
            default:
                return fallback;
        }
    }
}

/// <summary>
/// API version helpers and URL builders for legacy v0.1 vs workspace-scoped v0.2+ file endpoints.
/// </summary>
public static class AtlasApiVersion
{
    public static bool UsesWorkspaceScopedFileApi(string version)
    {
        if (!TryParseApiVersionString(version, out int major, out int minor))
            return false;

        return major > 0 || (major == 0 && minor >= 2);
    }

    public static string BuildUploadUrl(string baseUrl, string version, string apiId)
    {
        string cleanBase = NormalizeBaseUrl(baseUrl);
        if (UsesWorkspaceScopedFileApi(version))
            return $"{cleanBase}/{version}/upload";

        return $"{cleanBase}/{version}/upload/{apiId}";
    }

    public static string BuildDownloadUrl(string baseUrl, string version, string apiId, string fileId)
    {
        string cleanBase = NormalizeBaseUrl(baseUrl);
        if (UsesWorkspaceScopedFileApi(version))
            return $"{cleanBase}/{version}/download_binary_result/{fileId}";

        return $"{cleanBase}/{version}/download_binary_result/{apiId}/{fileId}";
    }

    public static string NormalizeBaseUrl(string baseUrl)
    {
        string url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = $"https://{url}";
        return url;
    }

    private static bool TryParseApiVersionString(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (string.IsNullOrWhiteSpace(version))
            return false;

        string trimmed = version.Trim();
        int dot = trimmed.IndexOf('.');
        string majorStr = dot >= 0 ? trimmed.Substring(0, dot) : trimmed;
        string minorStr = dot >= 0 ? trimmed.Substring(dot + 1) : "0";

        majorStr = majorStr.Trim();
        minorStr = minorStr.Trim();

        if (!int.TryParse(majorStr, out major))
            return false;

        minor = int.TryParse(minorStr, out int parsedMinor) ? parsedMinor : 0;
        return true;
    }
}
