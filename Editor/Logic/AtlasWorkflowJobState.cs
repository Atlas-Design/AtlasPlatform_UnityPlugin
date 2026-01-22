using System;
using System.Collections.Generic;

// A single execution ("run") of a workflow.
public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

[Serializable]
public class AtlasWorkflowJobState
{
    // Identity
    public string JobId;          // GUID
    public string WorkflowId;     // state.ActiveApiId
    public string WorkflowName;   // state.ActiveName
    public string WorkflowVersion;// state.Version

    // Timing
    public DateTime CreatedAtUtc;
    public DateTime? CompletedAtUtc;

    // Status
    public JobStatus Status;
    public string ErrorMessage;

    // 0..1 progress value
    public float Progress01;


    // Snapshots (we’ll use these more in later phases)
    public List<AtlasWorkflowParamState> InputsSnapshot = new List<AtlasWorkflowParamState>();
    public List<AtlasWorkflowParamState> OutputsSnapshot = new List<AtlasWorkflowParamState>();

    // Optional: where this job’s files live (we’ll wire this in phase 5)
    public string JobFolderPath;
}
