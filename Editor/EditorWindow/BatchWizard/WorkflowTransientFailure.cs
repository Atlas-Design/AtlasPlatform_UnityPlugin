using System;
using System.IO;
using System.Net.Http;
using System.Threading;

/// <summary>
/// Classifies failures for batch transient retries (network/IO/timeout only — not API-reported workflow failure).
/// </summary>
public static class WorkflowTransientFailure
{
    public static bool IsTransientException(Exception ex, CancellationToken userCancellation = default)
    {
        if (ex == null)
            return false;

        if (ex is OperationCanceledException)
            return !userCancellation.IsCancellationRequested;

        if (ex is HttpRequestException)
            return true;

        if (ex is IOException)
            return true;

        if (ex is TimeoutException)
            return true;

        if (ex is System.Net.Sockets.SocketException)
            return true;

        return ex.InnerException != null && IsTransientException(ex.InnerException, userCancellation);
    }

    /// <summary>
    /// After <see cref="AtlasAPIController.RunWorkflowWithPollingAsync"/> returns null, indicates hard workflow/API failure (no retry).
    /// </summary>
    public static bool IsApiReportedWorkflowFailure(AtlasWorkflowJobState job)
    {
        if (job == null)
            return false;

        if (job.ExecutionStatus == ExecutionStatus.Failed)
            return true;

        if (!string.IsNullOrEmpty(job.ErrorMessage) && !string.IsNullOrEmpty(job.ExecutionId))
            return true;

        return false;
    }

    public static void ResetJobForRetryAttempt(AtlasWorkflowJobState job)
    {
        if (job == null)
            return;

        job.ExecutionId = null;
        job.ExecutionStatus = ExecutionStatus.None;
        job.ErrorMessage = null;
        job.ErrorNodeName = null;
        job.ErrorNodeType = null;
        job.ErrorNodeId = null;
        job.Progress01 = 0f;
        job.Status = JobStatus.Running;
        job.CompletedAtUtc = null;
        WorkflowManager.SaveJobToDisk(job);
    }

    public static int ComputeBackoffMilliseconds(int attemptIndex, Random rng)
    {
        int cap = 30_000;
        int baseMs = Math.Min(cap, 500 * (1 << attemptIndex));
        int jitter = rng != null ? rng.Next(0, 400) : 0;
        return baseMs + jitter;
    }
}
