using System.Collections.Generic;

/// <summary>
/// In-memory description of a batch: optional workflow fingerprint + ordered rows.
/// </summary>
public sealed class WorkflowBatchDefinition
{
    public string WorkflowActiveApiId;
    public string WorkflowActiveName;
    public string WorkflowVersion;
    public string WorkflowBaseUrl;

    /// <summary>Library filename only (e.g. workflow.json) under Atlas workflow library; used for save/load and run manifests.</summary>
    public string WorkflowLibraryFileName;

    public List<WorkflowBatchRow> Rows = new List<WorkflowBatchRow>();
}

/// <summary>
/// One batch instance: values for each workflow input, keyed by ParamId.
/// </summary>
public sealed class WorkflowBatchRow
{
    public Dictionary<string, AtlasWorkflowParamState> InputsByParamId =
        new Dictionary<string, AtlasWorkflowParamState>();

    /// <summary>
    /// Creates a row with a shallow copy of each workflow input (same ParamIds and types as <paramref name="inputs"/>).
    /// </summary>
    public static WorkflowBatchRow FromWorkflowInputs(IReadOnlyList<AtlasWorkflowParamState> inputs)
    {
        var row = new WorkflowBatchRow();
        if (inputs == null)
            return row;

        foreach (var p in inputs)
        {
            if (p == null || string.IsNullOrEmpty(p.ParamId))
                continue;

            row.InputsByParamId[p.ParamId] = CloneParamCell(p);
        }

        return row;
    }

    /// <summary>
    /// Deep-enough copy for editing a row independently of the workflow state asset.
    /// </summary>
    public static AtlasWorkflowParamState CloneParamCell(AtlasWorkflowParamState source)
    {
        if (source == null)
            return null;

        return new AtlasWorkflowParamState
        {
            ParamId = source.ParamId,
            Label = source.Label,
            ParamType = source.ParamType,
            SourceType = source.SourceType,
            BoolValue = source.BoolValue,
            NumberValue = source.NumberValue,
            StringValue = source.StringValue,
            ImageValue = source.ImageValue,
            MeshValue = source.MeshValue,
            FilePath = source.FilePath,
            Format = source.Format
        };
    }

    /// <summary>
    /// Duplicate this row (new dictionary, cloned param cells).
    /// </summary>
    public WorkflowBatchRow Clone()
    {
        var copy = new WorkflowBatchRow();
        foreach (var kv in InputsByParamId)
            copy.InputsByParamId[kv.Key] = CloneParamCell(kv.Value);
        return copy;
    }
}

public sealed class BatchValidationIssue
{
    public int RowIndex = -1;
    public string ParamId;
    public string Message;
}

public sealed class BatchValidationResult
{
    public List<BatchValidationIssue> Issues { get; } = new List<BatchValidationIssue>();

    public bool IsValid => Issues.Count == 0;

    public void Add(int rowIndex, string paramId, string message)
    {
        Issues.Add(new BatchValidationIssue
        {
            RowIndex = rowIndex,
            ParamId = paramId,
            Message = message
        });
    }

    public void AddBatchLevel(string message)
    {
        Issues.Add(new BatchValidationIssue { RowIndex = -1, Message = message });
    }
}
