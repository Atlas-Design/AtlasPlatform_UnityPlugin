using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Re-runs a failed or cancelled job from saved inputs (Job History → Retry). Creates a new job id;
/// batch metadata is preserved and the run manifest row mapping is updated when applicable.
/// </summary>
public static class WorkflowJobRetry
{
    /// <summary>
    /// Starts a retry on the editor main thread (async; safe to call from UI click handlers).
    /// </summary>
    public static async void RunRetryAsync(AtlasWorkflowJobState failedJob)
    {
        if (failedJob == null)
            return;

        if (failedJob.Status != JobStatus.Failed && failedJob.Status != JobStatus.Cancelled)
        {
            EditorUtility.DisplayDialog(
                "Retry job",
                "Only failed or cancelled jobs can be retried.",
                "OK");
            return;
        }

        if (!string.IsNullOrEmpty(WorkflowEditorRunSession.ActiveRunningJobId))
        {
            EditorUtility.DisplayDialog(
                "Retry job",
                "Another workflow run is in progress. Cancel it from Running Jobs, then retry.",
                "OK");
            return;
        }

        if (!WorkflowManager.TryFindLibraryWorkflowPathForApiId(failedJob.WorkflowId, out string libraryPath))
        {
            EditorUtility.DisplayDialog(
                "Retry job",
                $"No workflow in the library matches API id '{failedJob.WorkflowId}'. Import the workflow JSON again, then retry.",
                "OK");
            return;
        }

        var tempState = ScriptableObject.CreateInstance<AtlasWorkflowState>();
        var controller = new WorkflowStateController(tempState);
        try
        {
            controller.LoadWorkflowFromFile(libraryPath);
        }
        catch (Exception ex)
        {
            UnityEngine.Object.DestroyImmediate(tempState);
            EditorUtility.DisplayDialog("Retry job", $"Failed to load workflow:\n{ex.Message}", "OK");
            return;
        }

        if (!AtlasPlatformAuth.TryValidateForRun(tempState.Version, out string authError))
        {
            UnityEngine.Object.DestroyImmediate(tempState);
            EditorUtility.DisplayDialog("Retry job", authError, "OK");
            return;
        }

        WorkflowManager.ApplyInputsSnapshotToState(tempState, failedJob.InputsSnapshot);
        var jobState = WorkflowManager.CloneStateForJobRun(tempState);
        UnityEngine.Object.DestroyImmediate(tempState);

        var newJob = WorkflowManager.CreateJobFromState(jobState);
        newJob.BatchId = failedJob.BatchId;
        newJob.BatchIndex = failedJob.BatchIndex;
        newJob.BatchName = failedJob.BatchName;
        newJob.RetryOfJobId = failedJob.JobId;
        WorkflowManager.SaveJobToDisk(newJob);
        WorkflowManager.NotifyJobsMutated();

        if (!string.IsNullOrEmpty(failedJob.BatchId) && failedJob.BatchIndex.HasValue)
        {
            WorkflowBatchPersistence.TryUpdateManifestJobIdForBatchRow(
                failedJob.BatchId,
                failedJob.BatchIndex.Value,
                newJob.JobId);
        }

        using (var cts = new CancellationTokenSource())
        {
            WorkflowEditorRunSession.BeginActiveRun(newJob, cts);
            try
            {
                var inputFiles = await WorkflowJobRunHelper.PrepareInputFilesForJobAsync(newJob, jobState);

                var outputResults = await AtlasAPIController.RunWorkflowWithPollingAsync(
                    jobState,
                    newJob,
                    inputFiles,
                    cancellationToken: cts.Token);

                if (outputResults != null)
                {
                    WorkflowJobRunHelper.MapOutputResultsToState(jobState, outputResults);
                    WorkflowJobRunHelper.CopyOutputFilesToJobFolder(newJob, jobState);
                    WorkflowManager.UpdateJobInputsFromState(newJob, jobState);
                    WorkflowManager.UpdateJobOutputsFromState(newJob, jobState);
                    WorkflowManager.MarkJobSucceeded(newJob, notifyUser: true);
                    WorkflowEditorRunSession.NotifyJobSelectedOnNextEditorUpdate(newJob);
                }
                else
                {
                    if (newJob.Status == JobStatus.Cancelled)
                        return;

                    string msg = string.IsNullOrEmpty(newJob.ErrorMessage)
                        ? "Workflow execution returned no result (see logs)."
                        : newJob.ErrorMessage;
                    WorkflowManager.MarkJobFailed(newJob, msg, notifyUser: true);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled via Running Jobs; job state already updated by API layer when applicable
            }
            catch (Exception ex)
            {
                AtlasLogger.LogException(ex, "Retry job failed");
                WorkflowManager.MarkJobFailed(newJob, ex.Message, notifyUser: true);
            }
            finally
            {
                WorkflowEditorRunSession.EndActiveRun();
                if (jobState != null)
                    UnityEngine.Object.DestroyImmediate(jobState);
            }
        }
    }
}
