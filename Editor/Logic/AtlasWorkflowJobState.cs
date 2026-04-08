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

// Server-side execution status returned by api_status endpoint
public enum ExecutionStatus
{
    None,       // Not yet submitted
    Pending,    // Submitted, waiting to start
    Running,    // Currently executing
    Completed,  // Finished successfully
    Failed      // Finished with error
}

[Serializable]
public class AtlasWorkflowJobState
{
    // Identity
    public string JobId;          // GUID
    public string WorkflowId;     // state.ActiveApiId
    public string WorkflowName;   // state.ActiveName
    public string WorkflowVersion;// state.Version

    // Batch (optional; null / omitted in JSON for standalone jobs from before batch support)
    public string BatchId;       // Shared GUID for all instances in one batch; null = not part of a batch
    public int? BatchIndex;      // 0..N-1 within the batch; null when not batched
    public string BatchName;     // Optional user-facing label for the batch

    /// <summary>If set, this run was started as a retry of a previous failed/cancelled job (same inputs lineage).</summary>
    public string RetryOfJobId;

    // Timing
    public DateTime CreatedAtUtc;
    public DateTime? CompletedAtUtc;

    // Status
    public JobStatus Status;
    public ExecutionStatus ExecutionStatus;  // Server-side status from polling
    public string ExecutionId;               // Returned by api_execute_async

    // Error info (enhanced in new API)
    public string ErrorMessage;
    public string ErrorNodeName;             // Node that caused the error
    public string ErrorNodeType;             // Type of the failing node
    public string ErrorNodeId;               // ID of the failing node

    // 0..1 progress value
    public float Progress01;

    // Snapshots (we'll use these more in later phases)
    public List<AtlasWorkflowParamState> InputsSnapshot = new List<AtlasWorkflowParamState>();
    public List<AtlasWorkflowParamState> OutputsSnapshot = new List<AtlasWorkflowParamState>();

    // Optional: where this job's files live (we'll wire this in phase 5)
    public string JobFolderPath;
}
