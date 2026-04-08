using System;
using UnityEngine;

/// <summary>
/// Holds all state for the batch editor window. The window owns lifecycle and calls <see cref="Dispose"/>.
/// </summary>
public sealed class WorkflowBatchEditorSession : IDisposable
{
    public AtlasWorkflowState WorkflowState { get; }
    public WorkflowStateController StateController { get; }
    public WorkflowBatchDefinition BatchDefinition { get; } = new WorkflowBatchDefinition();

    public int MaxConcurrentRuns { get; set; } = 2;
    public int MaxTransientRetriesPerInstance { get; set; } = 2;

    public WorkflowBatchEditorSession()
    {
        WorkflowState = ScriptableObject.CreateInstance<AtlasWorkflowState>();
        StateController = new WorkflowStateController(WorkflowState);
    }

    public void ResetBatchAfterWorkflowChanged()
    {
        BatchDefinition.Rows.Clear();
        BatchDefinition.WorkflowLibraryFileName = null;
        WorkflowBatchValidator.CaptureWorkflowFingerprint(WorkflowState, BatchDefinition);
    }

    public bool HasWorkflowLoaded =>
        WorkflowState != null && !string.IsNullOrEmpty(WorkflowState.ActiveName);

    public void Dispose()
    {
        if (WorkflowState != null)
            UnityEngine.Object.DestroyImmediate(WorkflowState);
    }
}
