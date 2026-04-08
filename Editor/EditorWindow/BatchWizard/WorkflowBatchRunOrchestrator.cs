using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Progress update while a batch run is in flight.
/// </summary>
public readonly struct WorkflowBatchProgress
{
    public WorkflowBatchProgress(int completedCount, int totalCount, string message)
    {
        CompletedCount = completedCount;
        TotalCount = totalCount;
        Message = message ?? string.Empty;
    }

    public int CompletedCount { get; }
    public int TotalCount { get; }
    public string Message { get; }
}

/// <summary>
/// Runs validated batch rows: Unity-serialized prep/create, limited concurrent API polling, transient retries per row.
/// Uses a work queue so <see cref="CancellationToken"/> cancellation does not start rows that were not yet dequeued (Phase 7).
/// </summary>
public static class WorkflowBatchRunOrchestrator
{
    /// <summary>
    /// Executes all rows in <paramref name="batchDefinition"/>. Caller must validate first.
    /// </summary>
    public static async Task RunAsync(
        AtlasWorkflowState workflowTemplate,
        WorkflowBatchDefinition batchDefinition,
        int maxConcurrentApiRuns,
        int maxTransientRetriesPerInstance,
        CancellationToken cancellationToken,
        IProgress<WorkflowBatchProgress> progress = null)
    {
        if (workflowTemplate == null || batchDefinition?.Rows == null || batchDefinition.Rows.Count == 0)
            return;

        maxConcurrentApiRuns = Math.Max(1, maxConcurrentApiRuns);
        int total = batchDefinition.Rows.Count;
        string batchId = Guid.NewGuid().ToString();
        string batchLabel = $"{workflowTemplate.ActiveName} {DateTime.Now:yyyy-MM-dd HH:mm}";

        string manifestPath = WorkflowBatchPersistence.RunManifestWriter.CreateManifestFilePath(batchId);
        var manifestWriter = new WorkflowBatchPersistence.RunManifestWriter(manifestPath);
        manifestWriter.WriteInitial(
            batchId,
            batchLabel,
            batchDefinition,
            maxConcurrentApiRuns,
            maxTransientRetriesPerInstance,
            batchDefinition.Rows);

        var unityPrepLock = new SemaphoreSlim(1, 1);
        var apiSemaphore = new SemaphoreSlim(maxConcurrentApiRuns, maxConcurrentApiRuns);
        var rng = new Random();
        var completedBox = new int[1];

        void Report(int done, string msg)
        {
            progress?.Report(new WorkflowBatchProgress(done, total, msg));
        }

        Report(0, "Starting batch…");

        var queue = new ConcurrentQueue<int>();
        for (var i = 0; i < total; i++)
            queue.Enqueue(i);

        int workerCount = Math.Min(maxConcurrentApiRuns, total);
        var workers = new List<Task>(workerCount);

        for (var w = 0; w < workerCount; w++)
        {
            workers.Add(RunWorkerAsync(
                workflowTemplate,
                batchDefinition,
                queue,
                batchId,
                batchLabel,
                maxTransientRetriesPerInstance,
                unityPrepLock,
                apiSemaphore,
                rng,
                cancellationToken,
                manifestWriter,
                () =>
                {
                    int done = Interlocked.Increment(ref completedBox[0]);
                    Report(done, $"Finished {done}/{total} instance(s)");
                },
                msg => Report(completedBox[0], msg)));
        }

        try
        {
            await Task.WhenAll(workers);
        }
        finally
        {
            manifestWriter.Finalize(cancellationToken.IsCancellationRequested);
        }
    }

    private static async Task RunWorkerAsync(
        AtlasWorkflowState workflowTemplate,
        WorkflowBatchDefinition batchDefinition,
        ConcurrentQueue<int> queue,
        string batchId,
        string batchLabel,
        int maxTransientRetriesPerInstance,
        SemaphoreSlim unityPrepLock,
        SemaphoreSlim apiSemaphore,
        Random rng,
        CancellationToken cancellationToken,
        WorkflowBatchPersistence.RunManifestWriter manifestWriter,
        Action onRowFullyDone,
        Action<string> reportRow)
    {
        while (queue.TryDequeue(out int rowIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RunSingleRowAsync(
                workflowTemplate,
                batchDefinition.Rows[rowIndex],
                rowIndex,
                batchId,
                batchLabel,
                maxTransientRetriesPerInstance,
                unityPrepLock,
                apiSemaphore,
                rng,
                cancellationToken,
                manifestWriter,
                onRowFullyDone,
                reportRow);
        }
    }

    private static async Task RunSingleRowAsync(
        AtlasWorkflowState workflowTemplate,
        WorkflowBatchRow row,
        int rowIndex,
        string batchId,
        string batchLabel,
        int maxTransientRetriesPerInstance,
        SemaphoreSlim unityPrepLock,
        SemaphoreSlim apiSemaphore,
        Random rng,
        CancellationToken cancellationToken,
        WorkflowBatchPersistence.RunManifestWriter manifestWriter,
        Action onRowFullyDone,
        Action<string> reportRow)
    {
        AtlasWorkflowState jobState = null;
        AtlasWorkflowJobState job = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await unityPrepLock.WaitAsync(cancellationToken);
            try
            {
                jobState = WorkflowManager.CloneStateForJobRun(workflowTemplate);
                if (jobState == null)
                    return;

                WorkflowJobRunHelper.ApplyBatchRowToJobState(row, jobState);
                job = WorkflowManager.CreateJobFromState(jobState);
                job.BatchId = batchId;
                job.BatchIndex = rowIndex;
                job.BatchName = batchLabel;
                WorkflowManager.SaveJobToDisk(job);
                WorkflowManager.NotifyJobsMutated();
                manifestWriter?.RecordJobCreated(rowIndex, job.JobId);
            }
            finally
            {
                unityPrepLock.Release();
            }

            reportRow($"Starting instance {rowIndex + 1}");

            int maxRounds = maxTransientRetriesPerInstance + 1;

            for (var round = 0; round < maxRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (round > 0)
                {
                    WorkflowTransientFailure.ResetJobForRetryAttempt(job);
                    reportRow($"Retry {round}/{maxTransientRetriesPerInstance} for instance {rowIndex + 1}");
                    int delay = WorkflowTransientFailure.ComputeBackoffMilliseconds(round - 1, rng);
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }

                Dictionary<string, string> inputFiles;
                await unityPrepLock.WaitAsync(cancellationToken);
                try
                {
                    inputFiles = await WorkflowJobRunHelper.PrepareInputFilesForJobAsync(job, jobState);
                }
                finally
                {
                    unityPrepLock.Release();
                }

                Dictionary<string, object> outputResults = null;
                await apiSemaphore.WaitAsync(cancellationToken);
                try
                {
                    try
                    {
                        outputResults = await AtlasAPIController.RunWorkflowWithPollingAsync(
                            jobState,
                            job,
                            inputFiles,
                            cancellationToken: cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (WorkflowTransientFailure.IsTransientException(ex, cancellationToken))
                    {
                        if (round >= maxRounds - 1)
                        {
                            WorkflowManager.MarkJobFailed(job,
                                $"Transient error (no retries left): {ex.Message}",
                                notifyUser: false);
                            return;
                        }

                        continue;
                    }
                    catch (Exception ex)
                    {
                        WorkflowManager.MarkJobFailed(job, ex.Message, notifyUser: false);
                        return;
                    }
                }
                finally
                {
                    apiSemaphore.Release();
                }

                if (outputResults != null)
                {
                    await unityPrepLock.WaitAsync(cancellationToken);
                    try
                    {
                        WorkflowJobRunHelper.MapOutputResultsToState(jobState, outputResults);
                        WorkflowJobRunHelper.CopyOutputFilesToJobFolder(job, jobState);
                        WorkflowManager.UpdateJobInputsFromState(job, jobState);
                        WorkflowManager.UpdateJobOutputsFromState(job, jobState);
                        WorkflowManager.MarkJobSucceeded(job, notifyUser: false);
                    }
                    finally
                    {
                        unityPrepLock.Release();
                    }

                    return;
                }

                if (job.Status == JobStatus.Cancelled)
                    return;

                if (WorkflowTransientFailure.IsApiReportedWorkflowFailure(job))
                {
                    WorkflowManager.MarkJobFailed(job,
                        string.IsNullOrEmpty(job.ErrorMessage)
                            ? "Workflow execution failed."
                            : job.ErrorMessage,
                        notifyUser: false);
                    return;
                }

                if (round >= maxRounds - 1)
                {
                    WorkflowManager.MarkJobFailed(job,
                        "Workflow execution returned no result (transient retries exhausted).",
                        notifyUser: false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (job != null && job.Status == JobStatus.Running)
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAtUtc = DateTime.UtcNow;
                job.Progress01 = 1f;
                job.ErrorMessage = "Batch cancelled.";
                WorkflowManager.SaveJobToDisk(job);
            }
        }
        finally
        {
            if (jobState != null)
                UnityEngine.Object.DestroyImmediate(jobState);

            if (job != null)
                onRowFullyDone?.Invoke();
        }
    }
}
