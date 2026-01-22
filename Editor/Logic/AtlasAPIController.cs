// In Packages/com.atlas.workflow/Editor/Logic/AtlasAPIController.cs

using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public static class AtlasAPIController
{
    private static readonly HttpClient client = new HttpClient()
    {
        // Increase timeout to 10 minutes (or whatever is appropriate for your heaviest workflow)
        Timeout = System.TimeSpan.FromMinutes(10)
    };

    public static async Task<Dictionary<string, object>> RunWorkflowAsync(
        AtlasWorkflowState state,
        Dictionary<string, string> inputFilesOverride = null)
    {
        //Debug.Log("--- Starting REAL Workflow Run ---");

        if (state == null)
        {
            //Debug.LogError("[AtlasAPIController] State is null.");
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
                            //Debug.LogWarning($"[AtlasAPIController] No file path resolved for input '{input.ParamId}'.");
                        }
                        break;
                }
            }
        }

        // --- 2. UPLOAD FILES ---
        string uploadUrl = $"{baseUrl}/{state.Version}/upload/{state.ActiveApiId}";
        foreach (var (paramId, filePath) in filesToUpload)
        {
            //Debug.Log($"[AtlasAPIController] Uploading file for param '{paramId}' from path: {filePath}");

            using (var form = new MultipartFormDataContent())
            using (var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath)))
            {
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                HttpResponseMessage uploadResponse = await client.PostAsync(uploadUrl, form);
                if (!uploadResponse.IsSuccessStatusCode)
                {
                    //Debug.LogError($"[AtlasAPIController] Failed to upload file for '{paramId}'. Status: {uploadResponse.StatusCode}");
                    return null;
                }

                string jsonResponse = await uploadResponse.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonResponse);

                if (responseData == null || !responseData.TryGetValue("file_id", out var fileId))
                {
                    //Debug.LogError($"[AtlasAPIController] Upload response for '{paramId}' did not contain 'file_id'. Raw: {jsonResponse}");
                    return null;
                }

                payload[paramId] = fileId;
                Debug.Log($"[AtlasAPIController] Upload successful for '{paramId}'. File ID: {fileId}");
            }
        }

        // --- 3. EXECUTE WORKFLOW ---
        string executeUrl = $"{baseUrl}/{state.Version}/api_execute/{state.ActiveApiId}";
        string payloadJson = JsonConvert.SerializeObject(payload, Formatting.Indented);

        //Debug.Log($"[AtlasAPIController] Executing workflow with payload:\n{payloadJson}");

        using (var httpContent =
               new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json"))
        {
            HttpResponseMessage executeResponse = await client.PostAsync(executeUrl, httpContent);
            if (!executeResponse.IsSuccessStatusCode)
            {
                string body = await executeResponse.Content.ReadAsStringAsync();
                //Debug.LogError($"[AtlasAPIController] Workflow execution failed. Status: {executeResponse.StatusCode}\n{body}");
                return null;
            }

            string finalJsonResponse = await executeResponse.Content.ReadAsStringAsync();
            //Debug.Log($"[AtlasAPIController] Workflow executed successfully. Response:\n{finalJsonResponse}");

            var finalData = JsonConvert.DeserializeObject<Dictionary<string, object>>(finalJsonResponse);
            if (finalData == null || !finalData.TryGetValue("outputs", out var outputsRaw))
            {
                //Debug.LogError("[AtlasAPIController] Response does not contain 'outputs' field.");
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
    // --- NEW HELPER: Downloads a file and returns the temporary path ---
    private static async Task<string> DownloadFileAsync(string baseUrl, string version, string apiId, string fileId, AtlasWorkflowParamState outputParam)
    {
        string downloadUrl = $"{baseUrl}/{version}/download_binary_result/{apiId}/{fileId}";
        Debug.Log($"Downloading file for output '{outputParam.ParamId}'...");

        try
        {
            byte[] fileData = await client.GetByteArrayAsync(downloadUrl);
            if (fileData == null || fileData.Length == 0) return null;

            string extension = outputParam.ParamType == ParamType.image ? ".png" : ".glb";
            string tempFilePath = Path.Combine(AssetExporter.GetTempDirectory(), $"{outputParam.ParamId}_{fileId}{extension}");

            await File.WriteAllBytesAsync(tempFilePath, fileData);
            //Debug.Log($"Successfully downloaded file to: {tempFilePath}");
            return tempFilePath;
        }
        catch (HttpRequestException e)
        {
            Debug.LogError($"Failed to download file for '{outputParam.ParamId}'. Error: {e.Message}");
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