using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Shared job-run steps used by <see cref="AtlasWorkflowEditor"/> and batch execution.
/// </summary>
public static class WorkflowJobRunHelper
{
    /// <summary>
    /// Overwrites each input on <paramref name="jobState"/> from the batch row (matched by ParamId).
    /// </summary>
    public static void ApplyBatchRowToJobState(WorkflowBatchRow row, AtlasWorkflowState jobState)
    {
        if (row == null || jobState?.Inputs == null)
            return;

        foreach (var input in jobState.Inputs)
        {
            if (input == null || string.IsNullOrEmpty(input.ParamId))
                continue;
            if (!row.InputsByParamId.TryGetValue(input.ParamId, out var cell) || cell == null)
                continue;

            input.BoolValue = cell.BoolValue;
            input.NumberValue = cell.NumberValue;
            input.StringValue = cell.StringValue;
            input.ImageValue = cell.ImageValue;
            input.MeshValue = cell.MeshValue;
            input.FilePath = cell.FilePath;
            input.SourceType = cell.SourceType;
        }
    }

    /// <summary>
    /// Copies asset inputs into the job folder and returns ParamId → path for upload.
    /// </summary>
    /// <remarks>
    /// Only image and mesh project/file inputs are prepared. Audio inputs are out of scope for v1
    /// (see <c>AtlasAPIController</c> upload path).
    /// </remarks>
    public static async Task<Dictionary<string, string>> PrepareInputFilesForJobAsync(
        AtlasWorkflowJobState job,
        AtlasWorkflowState state)
    {
        var result = new Dictionary<string, string>();

        if (state.Inputs == null || string.IsNullOrEmpty(job.JobFolderPath))
            return result;

        try
        {
            var inputsFolder = Path.Combine(job.JobFolderPath, "inputs");
            if (!Directory.Exists(inputsFolder))
                Directory.CreateDirectory(inputsFolder);

            foreach (var input in state.Inputs)
            {
                if (input.ParamType != ParamType.image && input.ParamType != ParamType.mesh)
                    continue;

                string sourcePath = null;

                if (input.SourceType == InputSourceType.FilePath &&
                    !string.IsNullOrEmpty(input.FilePath) &&
                    File.Exists(input.FilePath))
                {
                    sourcePath = input.FilePath;
                }

                if (sourcePath == null && input.SourceType == InputSourceType.Project)
                {
                    if (input.ParamType == ParamType.image && input.ImageValue != null)
                        sourcePath = await AssetExporter.ExportTextureAsPng(input.ImageValue);
                    else if (input.ParamType == ParamType.mesh && input.MeshValue != null)
                        sourcePath = await AssetExporter.ExportGameObjectAsGlb(input.MeshValue);
                }

                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                    continue;

                var ext = Path.GetExtension(sourcePath);
                var safeParamId = string.IsNullOrEmpty(input.ParamId) ? "Param" : input.ParamId;

                foreach (var c in Path.GetInvalidFileNameChars())
                    safeParamId = safeParamId.Replace(c, '_');

                var destName = $"Input_{safeParamId}{ext}";
                var destPath = Path.Combine(inputsFolder, destName);

                File.Copy(sourcePath, destPath, true);
                input.FilePath = destPath;
                result[input.ParamId] = destPath;
            }
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to prepare input files for job");
        }

        return result;
    }

    /// <summary>
    /// Copies generated image, mesh, and audio file outputs into the job outputs folder and updates <paramref name="state"/> paths.
    /// </summary>
    public static void CopyOutputFilesToJobFolder(AtlasWorkflowJobState job, AtlasWorkflowState state)
    {
        if (state.Outputs == null || string.IsNullOrEmpty(job.JobFolderPath))
            return;

        try
        {
            var outputsFolder = Path.Combine(job.JobFolderPath, "outputs");
            if (!Directory.Exists(outputsFolder))
                Directory.CreateDirectory(outputsFolder);

            foreach (var outputState in state.Outputs)
            {
                if (outputState.ParamType != ParamType.image &&
                    outputState.ParamType != ParamType.mesh &&
                    outputState.ParamType != ParamType.audio)
                    continue;

                if (string.IsNullOrEmpty(outputState.FilePath) ||
                    !File.Exists(outputState.FilePath))
                    continue;

                var ext = Path.GetExtension(outputState.FilePath);
                var safeParamId = string.IsNullOrEmpty(outputState.ParamId)
                    ? "Output"
                    : outputState.ParamId;

                foreach (var c in Path.GetInvalidFileNameChars())
                    safeParamId = safeParamId.Replace(c, '_');

                var fileName = $"Output_{safeParamId}{ext}";
                var destPath = Path.Combine(outputsFolder, fileName);

                File.Copy(outputState.FilePath, destPath, true);
                outputState.FilePath = destPath;
            }
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to copy output files to job folder");
        }
    }

    /// <summary>
    /// Maps API output dictionary into workflow state outputs.
    /// </summary>
    public static void MapOutputResultsToState(
        AtlasWorkflowState state,
        Dictionary<string, object> outputResults)
    {
        if (state.Outputs == null || outputResults == null)
            return;

        foreach (var outputState in state.Outputs)
        {
            if (!outputResults.TryGetValue(outputState.ParamId, out var value) || value == null)
                continue;

            try
            {
                switch (outputState.ParamType)
                {
                    case ParamType.boolean:
                        outputState.BoolValue = System.Convert.ToBoolean(value);
                        break;

                    case ParamType.number:
                        outputState.NumberValue = System.Convert.ToSingle(value);
                        break;

                    case ParamType.@string:
                        outputState.StringValue = value.ToString();
                        break;

                    case ParamType.image:
                    case ParamType.mesh:
                    case ParamType.audio:
                        var path = value.ToString();
                        if (!string.IsNullOrEmpty(path))
                            outputState.FilePath = path;
                        break;
                }
            }
            catch (System.Exception ex)
            {
                AtlasLogger.LogException(ex, $"Failed to map output '{outputState.ParamId}'");
            }
        }
    }
}
