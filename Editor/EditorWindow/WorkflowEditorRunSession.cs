using System;
using System.Threading;

/// <summary>
/// Shared session state for single-run workflow execution from <see cref="AtlasWorkflowEditor"/> so
/// <see cref="AtlasWorkflowJobsWindow"/> can offer Cancel vs Dismiss without referencing the editor instance.
/// </summary>
public static class WorkflowEditorRunSession
{
    static string _activeRunningJobId;
    static CancellationTokenSource _activeRunCts;

    public static string ActiveRunningJobId => _activeRunningJobId;

    /// <summary>Raised when the user selects a job in Job History (updates main editor status line).</summary>
    public static event Action<AtlasWorkflowJobState> JobSelectedForStatus;

    public static void BeginActiveRun(AtlasWorkflowJobState job, CancellationTokenSource cts)
    {
        if (job == null || string.IsNullOrEmpty(job.JobId) || cts == null)
            return;

        EndActiveRun();
        _activeRunningJobId = job.JobId;
        _activeRunCts = cts;
    }

    /// <summary>Clears active-run tracking. Caller still owns disposing the <see cref="CancellationTokenSource"/>.</summary>
    public static void EndActiveRun()
    {
        _activeRunningJobId = null;
        _activeRunCts = null;
    }

    public static void NotifyJobSelected(AtlasWorkflowJobState job)
    {
        JobSelectedForStatus?.Invoke(job);
    }

    public static void NotifyJobSelectedOnNextEditorUpdate(AtlasWorkflowJobState job)
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            JobSelectedForStatus?.Invoke(job);
        };
    }

    /// <summary>
    /// Cancel when this session owns the run; otherwise dismiss (mark failed) for stale cards.
    /// </summary>
    public static void HandleRunningJobStop(AtlasWorkflowJobState job)
    {
        if (job == null)
            return;

        if (!string.IsNullOrEmpty(_activeRunningJobId) && job.JobId == _activeRunningJobId && _activeRunCts != null)
            _activeRunCts.Cancel();
        else
        {
            WorkflowManager.MarkJobFailed(job,
                "Dismissed from Running Jobs (this editor session was not executing it; likely stale after restart).",
                notifyUser: false);
        }
    }
}
