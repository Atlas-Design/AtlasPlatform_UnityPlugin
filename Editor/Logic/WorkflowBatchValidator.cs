using System.Collections.Generic;
using System.IO;

/// <summary>
/// Validates <see cref="WorkflowBatchRow"/> entries against a loaded <see cref="AtlasWorkflowState"/> input schema.
/// </summary>
public static class WorkflowBatchValidator
{
    /// <summary>
    /// Validates rows against <paramref name="workflow"/>.Inputs. Every workflow input is required per row.
    /// </summary>
    public static BatchValidationResult Validate(
        AtlasWorkflowState workflow,
        IReadOnlyList<WorkflowBatchRow> rows,
        WorkflowBatchDefinition fingerprint = null)
    {
        var result = new BatchValidationResult();

        if (workflow == null)
        {
            result.AddBatchLevel("Workflow state is null.");
            return result;
        }

        if (workflow.Inputs == null || workflow.Inputs.Count == 0)
        {
            result.AddBatchLevel("Workflow has no inputs to batch.");
            return result;
        }

        if (rows == null || rows.Count == 0)
        {
            result.AddBatchLevel("Batch has no rows.");
            return result;
        }

        if (fingerprint != null)
        {
            if (!string.IsNullOrEmpty(fingerprint.WorkflowActiveApiId) &&
                fingerprint.WorkflowActiveApiId != workflow.ActiveApiId)
            {
                result.AddBatchLevel(
                    $"Batch was built for API id '{fingerprint.WorkflowActiveApiId}' but current workflow has '{workflow.ActiveApiId}'.");
            }

            if (!string.IsNullOrEmpty(fingerprint.WorkflowVersion) &&
                fingerprint.WorkflowVersion != workflow.Version)
            {
                result.AddBatchLevel(
                    $"Batch was built for version '{fingerprint.WorkflowVersion}' but current workflow has '{workflow.Version}'.");
            }
        }

        for (var i = 0; i < rows.Count; i++)
            ValidateRow(workflow.Inputs, rows[i], i, result);

        return result;
    }

    /// <summary>
    /// Fills <paramref name="definition"/> fingerprint fields from <paramref name="workflow"/> (convenience for wizard).
    /// </summary>
    public static void CaptureWorkflowFingerprint(AtlasWorkflowState workflow, WorkflowBatchDefinition definition)
    {
        if (workflow == null || definition == null)
            return;

        definition.WorkflowActiveApiId = workflow.ActiveApiId;
        definition.WorkflowActiveName = workflow.ActiveName;
        definition.WorkflowVersion = workflow.Version;
        definition.WorkflowBaseUrl = workflow.BaseUrl;
    }

    private static void ValidateRow(
        IReadOnlyList<AtlasWorkflowParamState> schemaInputs,
        WorkflowBatchRow row,
        int rowIndex,
        BatchValidationResult result)
    {
        if (row == null)
        {
            result.Add(rowIndex, null, "Row is null.");
            return;
        }

        foreach (var schema in schemaInputs)
        {
            if (schema == null || string.IsNullOrEmpty(schema.ParamId))
                continue;

            if (!row.InputsByParamId.TryGetValue(schema.ParamId, out var cell) || cell == null)
            {
                result.Add(rowIndex, schema.ParamId, "Missing value for required input.");
                continue;
            }

            if (cell.ParamType != schema.ParamType)
            {
                result.Add(rowIndex, schema.ParamId,
                    $"Type mismatch: workflow expects {schema.ParamType}, row has {cell.ParamType}.");
                continue;
            }

            switch (schema.ParamType)
            {
                case ParamType.number:
                    if (float.IsNaN(cell.NumberValue) || float.IsInfinity(cell.NumberValue))
                        result.Add(rowIndex, schema.ParamId, "Number must be finite.");
                    break;

                case ParamType.@string:
                    if (cell.StringValue == null)
                        result.Add(rowIndex, schema.ParamId, "String value is null.");
                    break;

                case ParamType.image:
                    if (!IsImageProvided(cell))
                        result.Add(rowIndex, schema.ParamId,
                            "Image input needs a project texture or an existing file path.");
                    break;

                case ParamType.mesh:
                    if (!IsMeshProvided(cell))
                        result.Add(rowIndex, schema.ParamId,
                            "Mesh input needs a project GameObject or an existing file path.");
                    break;

                case ParamType.audio:
                    result.Add(rowIndex, schema.ParamId,
                        "Audio input is not supported in this plugin version.");
                    break;
                    // boolean: any value allowed
            }
        }
    }

    private static bool IsImageProvided(AtlasWorkflowParamState cell)
    {
        if (cell.SourceType == InputSourceType.FilePath)
            return !string.IsNullOrEmpty(cell.FilePath) && File.Exists(cell.FilePath);
        return cell.ImageValue != null;
    }

    private static bool IsMeshProvided(AtlasWorkflowParamState cell)
    {
        if (cell.SourceType == InputSourceType.FilePath)
            return !string.IsNullOrEmpty(cell.FilePath) && File.Exists(cell.FilePath);
        return cell.MeshValue != null;
    }
}
