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


    // Snapshots (weùll use these more in later phases)
    public List<AtlasWorkflowParamState> InputsSnapshot = new List<AtlasWorkflowParamState>();
    public List<AtlasWorkflowParamState> OutputsSnapshot = new List<AtlasWorkflowParamState>();

    // Optional: where this jobùs files live (weùll wire this in phase 5)
    public string JobFolderPath;
}
