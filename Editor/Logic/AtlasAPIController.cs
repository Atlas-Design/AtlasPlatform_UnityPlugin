// In Packages/com.atlas.workflow/Editor/Logic/AtlasAPIController.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public static class AtlasAPIController
{
    // HttpClient with no timeout - we'll use CancellationToken for per-request timeouts
    private static readonly HttpClient client = new HttpClient()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    /// <summary>
    /// Creates a CancellationTokenSource with the configured timeout.
    /// Returns null if no timeout is configured (infinite wait).
    /// </summary>
    private static CancellationTokenSource CreateTimeoutCancellation()
    {
        var timeout = SettingsManager.GetApiTimeout();
        if (timeout == null)
        {
            AtlasLogger.LogAPI("Using no timeout limit for API request.");
            return null;
        }

        AtlasLogger.LogAPI($"Using timeout of {SettingsManager.FormatTimeoutDisplay(SettingsManager.GetApiTimeoutMinutes())} for API request.");
        return new CancellationTokenSource(timeout.Value);
    }

    /// <summary>
    /// Sends a POST request with the configured timeout.
    /// </summary>
    private static async Task<HttpResponseMessage> PostWithTimeoutAsync(string url, HttpContent content)
    {
        using var cts = CreateTimeoutCancellation();
        var token = cts?.Token ?? CancellationToken.None;

        try
        {
            return await client.PostAsync(url, content, token);
        }
        catch (OperationCanceledException) when (cts != null && cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Request to {url} timed out after {SettingsManager.FormatTimeoutDisplay(SettingsManager.GetApiTimeoutMinutes())}.");
        }
    }

    /// <summary>
    /// Downloads bytes with the configured timeout.
    /// </summary>
    private static async Task<byte[]> GetBytesWithTimeoutAsync(string url)
    {
        using var cts = CreateTimeoutCancellation();
        var token = cts?.Token ?? CancellationToken.None;

        try
        {
            var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (OperationCanceledException) when (cts != null && cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Download from {url} timed out after {SettingsManager.FormatTimeoutDisplay(SettingsManager.GetApiTimeoutMinutes())}.");
        }
    }

    /// <summary>
    /// Sends a GET request and returns the response body as a string.
    /// </summary>
    private static async Task<string> GetStringWithTimeoutAsync(string url)
    {
        using var cts = CreateTimeoutCancellation();
        var token = cts?.Token ?? CancellationToken.None;

        try
        {
            var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (OperationCanceledException) when (cts != null && cts.IsCancellationRequested)
        {
            throw new TimeoutException($"GET request to {url} timed out after {SettingsManager.FormatTimeoutDisplay(SettingsManager.GetApiTimeoutMinutes())}.");
        }
    }

    #region Async Polling API (New)

    /// <summary>
    /// Holds the parsed response from the api_status endpoint.
    /// </summary>
    public class StatusResponse
    {
        public ExecutionStatus Status;
        public Dictionary<string, object> Outputs;  // Only populated when Status == Completed
        
        // Error details (only populated when Status == Failed)
        public string ErrorMessage;
        public string ErrorNodeName;
        public string ErrorNodeType;
        public string ErrorNodeId;
    }

    /// <summary>
    /// Polls the api_status endpoint once and returns the parsed status.
    /// </summary>
    /// <param name="baseUrl">The base API URL (e.g., https://api.prod.atlas.design)</param>
    /// <param name="version">API version (e.g., 0.1)</param>
    /// <param name="executionId">The execution ID returned by api_execute_async</param>
    /// <returns>Parsed status response</returns>
    public static async Task<StatusResponse> PollStatusAsync(string baseUrl, string version, string executionId)
    {
        string statusUrl = $"{baseUrl}/{version}/api_status/{executionId}";
        AtlasLogger.LogAPI($"Polling status: {statusUrl}");

        try
        {
            string jsonResponse = await GetStringWithTimeoutAsync(statusUrl);
            AtlasLogger.LogAPI($"Status response: {jsonResponse}");

            var responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
            if (responseData == null)
            {
                throw new Exception("Failed to parse status response as JSON.");
            }

            var result = new StatusResponse();

            // Parse status string to enum
            string statusStr = responseData.TryGetValue("status", out var statusObj) ? statusObj?.ToString() : null;
            result.Status = ParseExecutionStatus(statusStr);

            // If completed, extract outputs from result.outputs
            if (result.Status == ExecutionStatus.Completed && responseData.TryGetValue("result", out var resultObj))
            {
                var resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultObj.ToString());
                if (resultDict != null && resultDict.TryGetValue("outputs", out var outputsObj))
                {
                    result.Outputs = JsonConvert.DeserializeObject<Dictionary<string, object>>(outputsObj.ToString());
                }
            }

            // If failed, extract error details
            if (result.Status == ExecutionStatus.Failed && responseData.TryGetValue("error", out var errorObj))
            {
                var errorDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(errorObj.ToString());
                if (errorDict != null)
                {
                    result.ErrorMessage = errorDict.TryGetValue("error", out var errMsg) ? errMsg?.ToString() : "Unknown error";
                    result.ErrorNodeName = errorDict.TryGetValue("node_name", out var nodeName) ? nodeName?.ToString() : null;
                    result.ErrorNodeType = errorDict.TryGetValue("node_type", out var nodeType) ? nodeType?.ToString() : null;
                    result.ErrorNodeId = errorDict.TryGetValue("node_id", out var nodeId) ? nodeId?.ToString() : null;
                }
            }

            return result;
        }
        catch (HttpRequestException e)
        {
            AtlasLogger.LogError($"Failed to poll status. Error: {e.Message}");
            throw;
        }
        catch (TimeoutException e)
        {
            AtlasLogger.LogError($"Status poll timed out. {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// Parses the status string from the API into our ExecutionStatus enum.
    /// </summary>
    private static ExecutionStatus ParseExecutionStatus(string status)
    {
        return status?.ToLowerInvariant() switch
        {
            "pending" => ExecutionStatus.Pending,
            "running" => ExecutionStatus.Running,
            "completed" => ExecutionStatus.Completed,
            "failed" => ExecutionStatus.Failed,
            _ => ExecutionStatus.None
        };
    }

    /// <summary>
    /// Holds the result of a workflow submission (before polling).
    /// </summary>
    public class SubmitResult
    {
        public bool Success;
        public string ExecutionId;
        public string ErrorMessage;
        
        // Store these so we can poll without re-reading state
        public string BaseUrl;
        public string Version;
        public string ApiId;
    }

    /// <summary>
    /// Uploads files and submits the workflow to api_execute_async.
    /// Returns immediately with an execution_id (does not wait for completion).
    /// </summary>
    public static async Task<SubmitResult> SubmitWorkflowAsync(
        AtlasWorkflowState state,
        Dictionary<string, string> inputFilesOverride = null)
    {
        AtlasLogger.LogAPI("Submitting workflow (async)...");

        if (state == null)
        {
            AtlasLogger.LogError("SubmitWorkflowAsync: State is null.");
            return new SubmitResult { Success = false, ErrorMessage = "State is null." };
        }

        string baseUrl = (state.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http"))
        {
            baseUrl = $"https://{baseUrl}";
        }

        var payload = new Dictionary<string, object>();
        var filesToUpload = new List<(string ParamId, string FilePath)>();

        // --- 1. BUILD PAYLOAD & COLLECT FILES ---
        if (state.Inputs != null)
        {
            foreach (var input in state.Inputs)
            {
                switch (input.ParamType)
                {
                    case ParamType.boolean:
                        payload[input.ParamId] = input.BoolValue;
                        break;

                    case ParamType.number:
                        payload[input.ParamId] = input.NumberValue;
                        break;

                    case ParamType.@string:
                        payload[input.ParamId] = input.StringValue;
                        break;

                    case ParamType.image:
                    case ParamType.mesh:
                        string filePath = null;

                        // Prefer job-local copy if provided
                        if (inputFilesOverride != null &&
                            inputFilesOverride.TryGetValue(input.ParamId, out var overridePath) &&
                            !string.IsNullOrEmpty(overridePath) &&
                            File.Exists(overridePath))
                        {
                            filePath = overridePath;
                        }
                        else
                        {
                            filePath = await GetPathForUpload(input);
                        }

                        if (!string.IsNullOrEmpty(filePath))
                        {
                            filesToUpload.Add((input.ParamId, filePath));
                        }
                        else
                        {
                            AtlasLogger.LogWarning($"No file path resolved for input '{input.ParamId}'.");
                        }
                        break;
                }
            }
        }

        // --- 2. UPLOAD FILES ---
        string uploadUrl = $"{baseUrl}/{state.Version}/upload/{state.ActiveApiId}";
        foreach (var (paramId, filePath) in filesToUpload)
        {
            AtlasLogger.LogAPI($"Uploading file for param '{paramId}' from path: {filePath}");

            using (var form = new MultipartFormDataContent())
            using (var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath)))
            {
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                HttpResponseMessage uploadResponse = await PostWithTimeoutAsync(uploadUrl, form);
                if (!uploadResponse.IsSuccessStatusCode)
                {
                    string err = $"Failed to upload file for '{paramId}'. Status: {uploadResponse.StatusCode}";
                    AtlasLogger.LogError(err);
                    return new SubmitResult { Success = false, ErrorMessage = err };
                }

                string jsonResponse = await uploadResponse.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonResponse);

                if (responseData == null || !responseData.TryGetValue("file_id", out var fileId))
                {
                    string err = $"Upload response for '{paramId}' did not contain 'file_id'. Raw: {jsonResponse}";
                    AtlasLogger.LogError(err);
                    return new SubmitResult { Success = false, ErrorMessage = err };
                }

                payload[paramId] = fileId;
                AtlasLogger.LogAPI($"Upload successful for '{paramId}'. File ID: {fileId}");
            }
        }

        // --- 3. SUBMIT TO ASYNC ENDPOINT ---
        string executeUrl = $"{baseUrl}/{state.Version}/api_execute_async/{state.ActiveApiId}";
        string payloadJson = JsonConvert.SerializeObject(payload, Formatting.Indented);

        AtlasLogger.LogAPI($"Submitting workflow at: {executeUrl}");
        AtlasLogger.LogAPI($"Payload:\n{payloadJson}");

        using (var httpContent = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json"))
        {
            HttpResponseMessage submitResponse = await PostWithTimeoutAsync(executeUrl, httpContent);
            if (!submitResponse.IsSuccessStatusCode)
            {
                string body = await submitResponse.Content.ReadAsStringAsync();
                string err = $"Workflow submission failed. Status: {submitResponse.StatusCode}\n{body}";
                AtlasLogger.LogError(err);
                return new SubmitResult { Success = false, ErrorMessage = err };
            }

            string responseJson = await submitResponse.Content.ReadAsStringAsync();
            AtlasLogger.LogAPI($"Workflow submitted successfully. Response:\n{responseJson}");

            var responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);
            if (responseData == null || !responseData.TryGetValue("execution_id", out var execIdObj))
            {
                string err = $"Response does not contain 'execution_id' field. Raw: {responseJson}";
                AtlasLogger.LogError(err);
                return new SubmitResult { Success = false, ErrorMessage = err };
            }

            return new SubmitResult
            {
                Success = true,
                ExecutionId = execIdObj.ToString(),
                BaseUrl = baseUrl,
                Version = state.Version,
                ApiId = state.ActiveApiId
            };
        }
    }

    /// <summary>
    /// Default poll interval in seconds.
    /// </summary>
    private const float DefaultPollIntervalSeconds = 2.0f;

    /// <summary>
    /// Runs the complete workflow using the new async polling API:
    /// 1. Submit workflow (upload files + api_execute_async)
    /// 2. Poll api_status until completed/failed
    /// 3. Download output files
    /// 4. Return outputs
    /// </summary>
    /// <param name="state">The workflow state with inputs configured</param>
    /// <param name="job">Optional job state to update during execution</param>
    /// <param name="inputFilesOverride">Optional file path overrides for inputs</param>
    /// <param name="pollIntervalSeconds">How often to poll for status (default 2s)</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>Dictionary of output param IDs to values (file paths for assets)</returns>
    public static async Task<Dictionary<string, object>> RunWorkflowWithPollingAsync(
        AtlasWorkflowState state,
        AtlasWorkflowJobState job = null,
        Dictionary<string, string> inputFilesOverride = null,
        float pollIntervalSeconds = DefaultPollIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        AtlasLogger.LogAPI("Starting workflow run with polling...");

        // --- 1. SUBMIT WORKFLOW ---
        var submitResult = await SubmitWorkflowAsync(state, inputFilesOverride);
        if (!submitResult.Success)
        {
            AtlasLogger.LogError($"Workflow submission failed: {submitResult.ErrorMessage}");
            return null;
        }

        AtlasLogger.LogAPI($"Workflow submitted. Execution ID: {submitResult.ExecutionId}");

        // Update job state if provided
        if (job != null)
        {
            job.ExecutionId = submitResult.ExecutionId;
            job.ExecutionStatus = ExecutionStatus.Pending;
        }

        // --- 2. POLL FOR COMPLETION ---
        StatusResponse statusResponse = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), cancellationToken);

            statusResponse = await PollStatusAsync(submitResult.BaseUrl, submitResult.Version, submitResult.ExecutionId);

            // Update job state if provided
            if (job != null)
            {
                job.ExecutionStatus = statusResponse.Status;
            }

            AtlasLogger.LogAPI($"Execution status: {statusResponse.Status}");

            if (statusResponse.Status == ExecutionStatus.Completed)
            {
                break;
            }

            if (statusResponse.Status == ExecutionStatus.Failed)
            {
                // Build detailed error message
                string errorMsg = statusResponse.ErrorMessage ?? "Unknown error";
                if (!string.IsNullOrEmpty(statusResponse.ErrorNodeName) || !string.IsNullOrEmpty(statusResponse.ErrorNodeType))
                {
                    errorMsg += $" (node: {statusResponse.ErrorNodeName ?? statusResponse.ErrorNodeType})";
                }
                if (!string.IsNullOrEmpty(statusResponse.ErrorNodeId))
                {
                    string shortId = statusResponse.ErrorNodeId.Length > 8 
                        ? statusResponse.ErrorNodeId.Substring(0, 8) + "..." 
                        : statusResponse.ErrorNodeId;
                    errorMsg += $" [id: {shortId}]";
                }

                // Update job with error details
                if (job != null)
                {
                    job.ErrorMessage = errorMsg;
                    job.ErrorNodeName = statusResponse.ErrorNodeName;
                    job.ErrorNodeType = statusResponse.ErrorNodeType;
                    job.ErrorNodeId = statusResponse.ErrorNodeId;
                }

                AtlasLogger.LogError($"Workflow execution failed: {errorMsg}");
                return null;
            }

            // For pending/running, continue polling
            if (statusResponse.Status != ExecutionStatus.Pending && statusResponse.Status != ExecutionStatus.Running)
            {
                AtlasLogger.LogError($"Unknown execution status: {statusResponse.Status}");
                return null;
            }
        }

        // Check if cancelled
        if (cancellationToken.IsCancellationRequested)
        {
            AtlasLogger.LogWarning("Workflow polling was cancelled.");
            return null;
        }

        if (statusResponse?.Outputs == null)
        {
            AtlasLogger.LogError("Completed status did not contain outputs.");
            return null;
        }

        var outputResults = statusResponse.Outputs;

        // --- 3. DOWNLOAD RESULTING FILES FOR ASSET OUTPUTS ---
        if (state.Outputs != null && outputResults != null)
        {
            foreach (var outputParam in state.Outputs)
            {
                if ((outputParam.ParamType == ParamType.image || outputParam.ParamType == ParamType.mesh) &&
                    outputResults.TryGetValue(outputParam.ParamId, out var fileIdObj))
                {
                    string fileId = fileIdObj?.ToString();
                    if (string.IsNullOrEmpty(fileId))
                        continue;

                    string downloadedPath = await DownloadFileAsync(
                        submitResult.BaseUrl, 
                        submitResult.Version, 
                        submitResult.ApiId, 
                        fileId, 
                        outputParam);

                    if (!string.IsNullOrEmpty(downloadedPath))
                    {
                        outputResults[outputParam.ParamId] = downloadedPath;
                    }
                }
            }
        }

        AtlasLogger.LogAPI("Workflow completed successfully with polling.");
        return outputResults;
    }

    #endregion

    public static async Task<Dictionary<string, object>> RunWorkflowAsync(
        AtlasWorkflowState state,
        Dictionary<string, string> inputFilesOverride = null)
    {
        AtlasLogger.LogAPI("Starting workflow run...");

        if (state == null)
        {
            AtlasLogger.LogError("RunWorkflowAsync: State is null.");
            return null;
        }

        string baseUrl = (state.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http"))
        {
            baseUrl = $"https://{baseUrl}";
        }

        var payload = new Dictionary<string, object>();
        var filesToUpload = new List<(string ParamId, string FilePath)>();

        // --- 1. BUILD PAYLOAD & COLLECT FILES ---
        if (state.Inputs != null)
        {
            foreach (var input in state.Inputs)
            {
                switch (input.ParamType)
                {
                    case ParamType.boolean:
                        payload[input.ParamId] = input.BoolValue;
                        break;

                    case ParamType.number:
                        payload[input.ParamId] = input.NumberValue;
                        break;

                    case ParamType.@string:
                        payload[input.ParamId] = input.StringValue;
                        break;

                    case ParamType.image:
                    case ParamType.mesh:
                        string filePath = null;

                        // Prefer job-local copy if provided
                        if (inputFilesOverride != null &&
                            inputFilesOverride.TryGetValue(input.ParamId, out var overridePath) &&
                            !string.IsNullOrEmpty(overridePath) &&
                            File.Exists(overridePath))
                        {
                            filePath = overridePath;
                        }
                        else
                        {
                            // Fallback to existing behaviour (temp export / direct path)
                            filePath = await GetPathForUpload(input);
                        }

                        if (!string.IsNullOrEmpty(filePath))
                        {
                            filesToUpload.Add((input.ParamId, filePath));
                        }
                        else
                        {
                            AtlasLogger.LogWarning($"No file path resolved for input '{input.ParamId}'.");
                        }
                        break;
                }
            }
        }

        // --- 2. UPLOAD FILES ---
        string uploadUrl = $"{baseUrl}/{state.Version}/upload/{state.ActiveApiId}";
        foreach (var (paramId, filePath) in filesToUpload)
        {
            AtlasLogger.LogAPI($"Uploading file for param '{paramId}' from path: {filePath}");

            using (var form = new MultipartFormDataContent())
            using (var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath)))
            {
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                HttpResponseMessage uploadResponse = await PostWithTimeoutAsync(uploadUrl, form);
                if (!uploadResponse.IsSuccessStatusCode)
                {
                    AtlasLogger.LogError($"Failed to upload file for '{paramId}'. Status: {uploadResponse.StatusCode}");
                    return null;
                }

                string jsonResponse = await uploadResponse.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonResponse);

                if (responseData == null || !responseData.TryGetValue("file_id", out var fileId))
                {
                    AtlasLogger.LogError($"Upload response for '{paramId}' did not contain 'file_id'. Raw: {jsonResponse}");
                    return null;
                }

                payload[paramId] = fileId;
                AtlasLogger.LogAPI($"Upload successful for '{paramId}'. File ID: {fileId}");
            }
        }

        // --- 3. EXECUTE WORKFLOW ---
        string executeUrl = $"{baseUrl}/{state.Version}/api_execute/{state.ActiveApiId}";
        string payloadJson = JsonConvert.SerializeObject(payload, Formatting.Indented);

        AtlasLogger.LogAPI($"Executing workflow at: {executeUrl}");
        AtlasLogger.LogAPI($"Payload:\n{payloadJson}");

        using (var httpContent =
               new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json"))
        {
            HttpResponseMessage executeResponse = await PostWithTimeoutAsync(executeUrl, httpContent);
            if (!executeResponse.IsSuccessStatusCode)
            {
                string body = await executeResponse.Content.ReadAsStringAsync();
                AtlasLogger.LogError($"Workflow execution failed. Status: {executeResponse.StatusCode}\n{body}");
                return null;
            }

            string finalJsonResponse = await executeResponse.Content.ReadAsStringAsync();
            AtlasLogger.LogAPI($"Workflow executed successfully. Response:\n{finalJsonResponse}");

            var finalData = JsonConvert.DeserializeObject<Dictionary<string, object>>(finalJsonResponse);
            if (finalData == null || !finalData.TryGetValue("outputs", out var outputsRaw))
            {
                AtlasLogger.LogError("Response does not contain 'outputs' field.");
                return null;
            }

            var outputResults =
                JsonConvert.DeserializeObject<Dictionary<string, object>>(outputsRaw.ToString());

            // --- 4. DOWNLOAD RESULTING FILES FOR ASSET OUTPUTS ---
            if (state.Outputs != null && outputResults != null)
            {
                foreach (var outputParam in state.Outputs)
                {
                    if ((outputParam.ParamType == ParamType.image || outputParam.ParamType == ParamType.mesh) &&
                        outputResults.TryGetValue(outputParam.ParamId, out var fileIdObj))
                    {
                        string fileId = fileIdObj?.ToString();
                        if (string.IsNullOrEmpty(fileId))
                            continue;

                        // NOTE: we assume DownloadFileAsync has signature:
                        // DownloadFileAsync(string baseUrl, string version, string apiId, string fileId, AtlasWorkflowParamState outputParam)
                        string downloadedPath =
                            await DownloadFileAsync(baseUrl, state.Version, state.ActiveApiId, fileId, outputParam);

                        if (!string.IsNullOrEmpty(downloadedPath))
                        {
                            // Replace the file_id with the local path
                            outputResults[outputParam.ParamId] = downloadedPath;
                        }
                    }
                }
            }

            return outputResults;
        }
    }
    /// <summary>
    /// Downloads a file from the API and returns the temporary path.
    /// </summary>
    private static async Task<string> DownloadFileAsync(string baseUrl, string version, string apiId, string fileId, AtlasWorkflowParamState outputParam)
    {
        string downloadUrl = $"{baseUrl}/{version}/download_binary_result/{apiId}/{fileId}";
        AtlasLogger.LogAPI($"Downloading file for output '{outputParam.ParamId}' from: {downloadUrl}");

        try
        {
            byte[] fileData = await GetBytesWithTimeoutAsync(downloadUrl);
            if (fileData == null || fileData.Length == 0)
            {
                AtlasLogger.LogWarning($"Downloaded empty file for '{outputParam.ParamId}'.");
                return null;
            }

            string extension = outputParam.ParamType == ParamType.image ? ".png" : ".glb";
            string tempFilePath = Path.Combine(AssetExporter.GetTempDirectory(), $"{outputParam.ParamId}_{fileId}{extension}");

            await File.WriteAllBytesAsync(tempFilePath, fileData);
            AtlasLogger.LogFile($"Successfully downloaded file to: {tempFilePath} ({fileData.Length} bytes)");
            return tempFilePath;
        }
        catch (HttpRequestException e)
        {
            AtlasLogger.LogError($"Failed to download file for '{outputParam.ParamId}'. Error: {e.Message}");
            return null;
        }
        catch (TimeoutException e)
        {
            AtlasLogger.LogError($"Download timed out for '{outputParam.ParamId}'. {e.Message}");
            return null;
        }
    }

    // --- NEW HELPER: Consolidates the asset export logic ---
    private static async Task<string> GetPathForUpload(AtlasWorkflowParamState input)
    {
        if (input.SourceType == InputSourceType.FilePath && File.Exists(input.FilePath))
        {
            return input.FilePath;
        }
        if (input.SourceType == InputSourceType.Project)
        {
            if (input.ParamType == ParamType.image && input.ImageValue != null)
                return await AssetExporter.ExportTextureAsPng(input.ImageValue);

            if (input.ParamType == ParamType.mesh && input.MeshValue != null)
                return await AssetExporter.ExportGameObjectAsGlb(input.MeshValue);
        }
        return null;
    }
}