using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;

public static class WorkflowManager
{
    #region Constants & State

    private const string LibraryFolderName = "AtlasWorkflowLibrary";
    private const string JobsRootFolderName = "AtlasWorkflowJobs";
    private const string JobFileName = "job.json";

    /// <summary>
    /// Jobs still marked Running longer than this are treated as stale on load (crash/force-quit/no save).
    /// </summary>
    public const int StaleRunningJobMaxHours = 48;

    /// <summary>
    /// Runtime cache of all loaded job states.
    /// </summary>
    public static readonly List<AtlasWorkflowJobState> Jobs = new List<AtlasWorkflowJobState>();

    /// <summary>
    /// Raised after the in-memory jobs list or job status changes in a way UI should refresh (history, Running Jobs).
    /// Handlers should marshal to the main thread (e.g. <see cref="UnityEditor.EditorApplication.delayCall"/>).
    /// </summary>
    public static event Action JobsMutated;

    public static void NotifyJobsMutated()
    {
        JobsMutated?.Invoke();
    }

    #endregion

    #region Library Management (Workflow Files)

    /// <summary>
    /// Returns the full path to the workflow library directory (PersistentDataPath). 
    /// Creates the directory if it does not exist.
    /// </summary>
    public static string GetLibraryDirectory()
    {
        string path = Path.Combine(Application.persistentDataPath, LibraryFolderName);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            AtlasLogger.LogFile($"Created library directory at: {path}");
        }
        return path;
    }

    /// <summary>
    /// Scans the library directory and returns a list of full paths for all .json workflow files.
    /// </summary>
    public static List<string> GetSavedWorkflows()
    {
        string libraryDir = GetLibraryDirectory();
        return Directory.GetFiles(libraryDir, "*.json").ToList();
    }

    /// <summary>
    /// Copies an external workflow file into the internal library. Overwrites if the file exists.
    /// Returns the destination path on success, or null on failure.
    /// </summary>
    public static string SaveWorkflowToLibrary(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
        {
            AtlasLogger.LogError($"Source file not found: {sourceFilePath}");
            return null;
        }

        string libraryDir = GetLibraryDirectory();
        string fileName = Path.GetFileName(sourceFilePath);
        string destinationPath = Path.Combine(libraryDir, fileName);

        File.Copy(sourceFilePath, destinationPath, true); // true allows overwriting

        if (File.Exists(destinationPath))
        {
            AtlasLogger.LogFile($"Saved workflow to library: {destinationPath}");
            return destinationPath;
        }
        
        AtlasLogger.LogError($"Failed to save workflow to: {destinationPath}");
        return null;
    }

    /// <summary>
    /// Deletes a specific workflow file from the library by filename.
    /// Returns true if the file was successfully deleted (or didn't exist).
    /// </summary>
    public static bool DeleteWorkflowFromLibrary(string fileName)
    {
        string libraryDir = GetLibraryDirectory();
        string filePath = Path.Combine(libraryDir, fileName);

        if (!File.Exists(filePath))
        {
            AtlasLogger.LogWarning($"Cannot delete workflow, file not found: {filePath}");
            return false;
        }

        File.Delete(filePath);
        AtlasLogger.LogFile($"Deleted workflow from library: {filePath}");
        return !File.Exists(filePath);
    }

    #endregion

    #region Job Lifecycle (Creation & Status)

    /// <summary>
    /// Creates a new Job based on the current UI/Workflow State. 
    /// Snapshots inputs, creates the directory structure, and saves the initial job file.
    /// </summary>
    public static AtlasWorkflowJobState CreateJobFromState(AtlasWorkflowState state)
    {
        var job = new AtlasWorkflowJobState
        {
            JobId = Guid.NewGuid().ToString(),
            WorkflowId = state.ActiveApiId,
            WorkflowName = state.ActiveName,
            WorkflowVersion = state.Version,

            CreatedAtUtc = DateTime.UtcNow,
            Status = JobStatus.Running,

            Progress01 = 0f,

            InputsSnapshot = state.Inputs.ConvertAll(CloneParam)
        };

        job.JobFolderPath = GetJobFolderPath(job);

        Jobs.Add(job);
        SaveJobToDisk(job);

        AtlasLogger.LogJob($"Created job {job.JobId} for workflow '{job.WorkflowName}'");

        NotifyJobsMutated();

        return job;
    }

    /// <summary>
    /// Marks a running job as Succeeded, sets progress to 100%, and saves to disk.
    /// </summary>
    /// <param name="notifyUser">If false, skips the completion dialog even when settings ask for it (e.g. batch runs).</param>
    public static void MarkJobSucceeded(AtlasWorkflowJobState job, bool notifyUser = true)
    {
        job.Status = JobStatus.Succeeded;
        job.ExecutionStatus = ExecutionStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.Progress01 = 1f;
        SaveJobToDisk(job);

        SettingsManager.CheckTempStorageLimit();

        NotifyJobsMutated();

        if (notifyUser)
            NotifyJobComplete(job, true);
    }

    /// <summary>
    /// Marks a running job as Failed, records the error message, and saves to disk.
    /// Note: Enhanced error details (ErrorNodeName, etc.) should be set on the job 
    /// before calling this method if available from the API.
    /// </summary>
    public static void MarkJobFailed(AtlasWorkflowJobState job, string errorMessage, bool notifyUser = true)
    {
        job.Status = JobStatus.Failed;
        job.ExecutionStatus = ExecutionStatus.Failed;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.ErrorMessage = errorMessage;
        job.Progress01 = 1f;
        SaveJobToDisk(job);

        NotifyJobsMutated();

        if (notifyUser)
        {
            SettingsManager.CheckTempStorageLimit();
            NotifyJobComplete(job, false);
        }
    }

    /// <summary>
    /// Shows a notification dialog when a job completes (if enabled in settings).
    /// </summary>
    private static void NotifyJobComplete(AtlasWorkflowJobState job, bool succeeded)
    {
        if (!SettingsManager.GetNotifyOnJobComplete())
            return;

        string title = succeeded ? "Workflow Completed" : "Workflow Failed";
        string message = succeeded
            ? $"'{job.WorkflowName}' completed successfully."
            : $"'{job.WorkflowName}' failed.\n\n{job.ErrorMessage}";

        // Use EditorApplication.delayCall to ensure we're on the main thread
        UnityEditor.EditorApplication.delayCall += () =>
        {
            UnityEditor.EditorUtility.DisplayDialog(title, message, "OK");
        };
    }


    /// <summary>
    /// Snapshots the inputs from the active Workflow State into the Job object and saves the job.
    /// </summary>
    public static void UpdateJobInputsFromState(AtlasWorkflowJobState job, AtlasWorkflowState state)
    {
        if (state.Inputs == null)
        {
            job.InputsSnapshot = new List<AtlasWorkflowParamState>();
        }
        else
        {
            job.InputsSnapshot = state.Inputs.ConvertAll(CloneParam);
        }

        SaveJobToDisk(job);
    }

    /// <summary>
    /// Creates an in-memory copy of the workflow state for a single job run.
    /// This clone is not saved as an asset; it just isolates Inputs/Outputs
    /// so multiple jobs can run in parallel without sharing the same lists.
    /// </summary>
    public static AtlasWorkflowState CloneStateForJobRun(AtlasWorkflowState source)
    {
        if (source == null) return null;

        var clone = ScriptableObject.CreateInstance<AtlasWorkflowState>();

        // Metadata
        clone.ActiveApiId = source.ActiveApiId;
        clone.ActiveName = source.ActiveName;
        clone.BaseUrl = source.BaseUrl;
        clone.Version = source.Version;

        // Inputs
        if (source.Inputs != null)
        {
            foreach (var src in source.Inputs)
            {
                if (src == null) continue;
                var p = new AtlasWorkflowParamState
                {
                    ParamId = src.ParamId,
                    Label = src.Label,
                    ParamType = src.ParamType,
                    SourceType = src.SourceType,

                    BoolValue = src.BoolValue,
                    NumberValue = src.NumberValue,
                    StringValue = src.StringValue,

                    // We keep the Unity asset refs; they�re needed for export
                    ImageValue = src.ImageValue,
                    MeshValue = src.MeshValue,

                    FilePath = src.FilePath,
                    Format = src.Format
                };
                clone.Inputs.Add(p);
            }
        }

        // Outputs
        if (source.Outputs != null)
        {
            foreach (var src in source.Outputs)
            {
                if (src == null) continue;
                var p = new AtlasWorkflowParamState
                {
                    ParamId = src.ParamId,
                    Label = src.Label,
                    ParamType = src.ParamType,
                    SourceType = src.SourceType,

                    BoolValue = src.BoolValue,
                    NumberValue = src.NumberValue,
                    StringValue = src.StringValue,

                    ImageValue = src.ImageValue,
                    MeshValue = src.MeshValue,

                    FilePath = src.FilePath,
                    Format = src.Format
                };
                clone.Outputs.Add(p);
            }
        }

        return clone;
    }

    #endregion

    #region State & Data Mapping

    /// <summary>
    /// Copies saved input values from a job snapshot onto a loaded workflow state (matched by ParamId).
    /// Used when re-running a job from history.
    /// </summary>
    public static void ApplyInputsSnapshotToState(AtlasWorkflowState state, IList<AtlasWorkflowParamState> snapshot)
    {
        if (state?.Inputs == null || snapshot == null)
            return;

        Dictionary<string, AtlasWorkflowParamState> byId = new Dictionary<string, AtlasWorkflowParamState>();
        foreach (var p in snapshot)
        {
            if (p != null && !string.IsNullOrEmpty(p.ParamId) && !byId.ContainsKey(p.ParamId))
                byId[p.ParamId] = p;
        }

        foreach (var input in state.Inputs)
        {
            if (input == null || string.IsNullOrEmpty(input.ParamId))
                continue;
            if (!byId.TryGetValue(input.ParamId, out var snap) || snap == null)
                continue;

            input.BoolValue = snap.BoolValue;
            input.NumberValue = snap.NumberValue;
            input.StringValue = snap.StringValue ?? "";
            input.FilePath = snap.FilePath;
            input.SourceType = snap.SourceType;
            input.ImageValue = null;
            input.MeshValue = null;
        }
    }

    /// <summary>
    /// Finds a workflow library JSON whose <c>api_id</c> matches <paramref name="workflowApiId"/> (job <see cref="AtlasWorkflowJobState.WorkflowId"/>).
    /// </summary>
    public static bool TryFindLibraryWorkflowPathForApiId(string workflowApiId, out string libraryFilePath)
    {
        libraryFilePath = null;
        if (string.IsNullOrEmpty(workflowApiId))
            return false;

        foreach (string file in GetSavedWorkflows())
        {
            try
            {
                string json = File.ReadAllText(file);
                var wf = WorkflowDefinition.FromJson(json);
                if (wf != null && wf.ApiId == workflowApiId)
                {
                    libraryFilePath = file;
                    return true;
                }
            }
            catch
            {
                // skip invalid files
            }
        }

        return false;
    }

    /// <summary>
    /// Snapshots the outputs from the active Workflow State into the Job object and saves the job.
    /// Used when a job finishes to record what the results were.
    /// </summary>
    public static void UpdateJobOutputsFromState(AtlasWorkflowJobState job, AtlasWorkflowState state)
    {
        if (state.Outputs == null)
        {
            job.OutputsSnapshot = new List<AtlasWorkflowParamState>();
        }
        else
        {
            job.OutputsSnapshot = state.Outputs.ConvertAll(CloneParam);
        }

        SaveJobToDisk(job);
    }

    /// <summary>
    /// Loads outputs from a historical Job object back into the active Workflow State.
    /// Used to "View" the results of an old job in the UI.
    /// </summary>
    public static void ApplyJobOutputsToState(AtlasWorkflowJobState job, AtlasWorkflowState state)
    {
        if (job.OutputsSnapshot == null)
        {
            state.Outputs = new List<AtlasWorkflowParamState>();
            return;
        }

        state.Outputs = job.OutputsSnapshot.ConvertAll(CloneParam);
    }

    #endregion

    #region Job Persistence (Load/Save)

    /// <summary>
    /// Serializes a specific Job object to its dedicated JSON file on disk.
    /// Handles directory creation if missing.
    /// </summary>
    /// <returns>True if the job file was written successfully.</returns>
    public static bool SaveJobToDisk(AtlasWorkflowJobState job)
    {
        if (job == null)
        {
            AtlasLogger.LogError("SaveJobToDisk: job is null.");
            return false;
        }

        // Opening Atlas Job History calls LoadJobsFromDisk(), which clears Jobs and reloads new instances.
        // The async workflow run still holds the original object reference, so completion would otherwise
        // update a "detached" job (dialog + disk OK) while WorkflowManager.Jobs still shows Running.
        SyncJobInListFromLiveInstance(job);

        try
        {
            if (string.IsNullOrEmpty(job.JobFolderPath))
            {
                job.JobFolderPath = GetJobFolderPath(job);
            }

            job.JobFolderPath = Path.GetFullPath(job.JobFolderPath);

            if (!Directory.Exists(job.JobFolderPath))
                Directory.CreateDirectory(job.JobFolderPath);

            string jobFilePath = Path.Combine(job.JobFolderPath, JobFileName);

            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                PreserveReferencesHandling = PreserveReferencesHandling.None,
                TypeNameHandling = TypeNameHandling.None,
                NullValueHandling = NullValueHandling.Ignore
            };

            var json = JsonConvert.SerializeObject(job, Formatting.Indented, settings);
            File.WriteAllText(jobFilePath, json);

            AtlasLogger.LogJob($"Saved job {job.JobId} to: {jobFilePath}");
            return true;
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex,
                $"Failed to save job {job.JobId}. Check folder permissions and that JobFolderPath matches the job.json location.");
            return false;
        }
    }

    /// <summary>
    /// If <paramref name="live"/> is not the same instance as the job with the same JobId in <see cref="Jobs"/>,
    /// copies runtime fields onto the listed instance so UI and history keep stable references.
    /// </summary>
    private static void SyncJobInListFromLiveInstance(AtlasWorkflowJobState live)
    {
        if (live == null || string.IsNullOrEmpty(live.JobId))
            return;

        foreach (var listed in Jobs)
        {
            if (listed == null || listed.JobId != live.JobId)
                continue;

            if (ReferenceEquals(listed, live))
                return;

            CopyRuntimeJobFields(live, listed);
            return;
        }
    }

    private static void CopyRuntimeJobFields(AtlasWorkflowJobState from, AtlasWorkflowJobState to)
    {
        to.WorkflowId = from.WorkflowId;
        to.WorkflowName = from.WorkflowName;
        to.WorkflowVersion = from.WorkflowVersion;
        to.BatchId = from.BatchId;
        to.BatchIndex = from.BatchIndex;
        to.BatchName = from.BatchName;
        to.RetryOfJobId = from.RetryOfJobId;
        to.CompletedAtUtc = from.CompletedAtUtc;
        to.Status = from.Status;
        to.ExecutionStatus = from.ExecutionStatus;
        to.ExecutionId = from.ExecutionId;
        to.ErrorMessage = from.ErrorMessage;
        to.ErrorNodeName = from.ErrorNodeName;
        to.ErrorNodeType = from.ErrorNodeType;
        to.ErrorNodeId = from.ErrorNodeId;
        to.Progress01 = from.Progress01;
        to.InputsSnapshot = from.InputsSnapshot;
        to.OutputsSnapshot = from.OutputsSnapshot;
        to.JobFolderPath = from.JobFolderPath;
    }

    /// <summary>
    /// Scans the Jobs root folder recursively for job.json files and repopulates the Jobs list.
    /// </summary>
    public static void LoadJobsFromDisk()
    {
        Jobs.Clear();

        try
        {
            var root = GetJobsRootDirectory();
            if (!Directory.Exists(root))
                return;

            foreach (var jobFilePath in Directory.GetFiles(root, JobFileName, SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(jobFilePath);
                    var job = JsonConvert.DeserializeObject<AtlasWorkflowJobState>(json);

                    if (job != null)
                    {
                        // Always derive folder from where job.json was found. Serialized JobFolderPath can be
                        // wrong (other machine, moved project, renamed parent) and breaks SaveJobToDisk.
                        string folder = Path.GetDirectoryName(jobFilePath);
                        if (!string.IsNullOrEmpty(folder))
                            job.JobFolderPath = Path.GetFullPath(folder);

                        Jobs.Add(job);
                    }
                }
                catch (System.Exception ex)
                {
                    AtlasLogger.LogException(ex, $"Failed to load job from {jobFilePath}");
                }
            }

            AtlasLogger.LogJob($"Loaded {Jobs.Count} job(s) from disk.");

            ReconcileStaleRunningJobs();
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to load jobs");
        }
        finally
        {
            NotifyJobsMutated();
        }
    }

    /// <summary>
    /// Marks Running jobs as failed if they are older than <see cref="StaleRunningJobMaxHours"/> (no completion was saved).
    /// </summary>
    public static int ReconcileStaleRunningJobs()
    {
        int n = 0;
        var cutoff = TimeSpan.FromHours(StaleRunningJobMaxHours);
        foreach (var job in Jobs)
        {
            if (job == null || job.Status != JobStatus.Running)
                continue;
            if (DateTime.UtcNow - job.CreatedAtUtc <= cutoff)
                continue;

            var prevExecution = job.ExecutionStatus;
            var prevProgress = job.Progress01;
            var prevCompleted = job.CompletedAtUtc;

            job.Status = JobStatus.Failed;
            job.ExecutionStatus = ExecutionStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.ErrorMessage =
                $"Stale job: still marked Running after {StaleRunningJobMaxHours}+ hours (editor may have closed before completion).";
            job.Progress01 = 1f;

            if (SaveJobToDisk(job))
                n++;
            else
            {
                job.Status = JobStatus.Running;
                job.ExecutionStatus = prevExecution;
                job.CompletedAtUtc = prevCompleted;
                job.ErrorMessage = null;
                job.Progress01 = prevProgress;
                AtlasLogger.LogError(
                    $"Stale job {job.JobId} could not be updated on disk (see save error above). Fix permissions or delete the job folder under AtlasWorkflowJobs.");
            }
        }

        if (n > 0)
            AtlasLogger.LogWarning($"Marked {n} stale workflow job(s) as failed. Use Stop on Running Jobs or complete runs normally to avoid orphaned Running state.");

        return n;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Creates a shallow clone of a parameter state, stripping out Unity Objects (Images/Meshes)
    /// to ensure clean JSON serialization.
    /// </summary>
    private static AtlasWorkflowParamState CloneParam(AtlasWorkflowParamState source)
    {
        return new AtlasWorkflowParamState
        {
            ParamId = source.ParamId,
            Label = source.Label,
            ParamType = source.ParamType,
            SourceType = source.SourceType,

            BoolValue = source.BoolValue,
            NumberValue = source.NumberValue,
            StringValue = source.StringValue,

            // IMPORTANT: do NOT serialize UnityEngine.Object references in jobs
            ImageValue = null,
            MeshValue = null,

            // For image/mesh/audio, this is what we actually care about in history:
            FilePath = source.FilePath,
            Format = source.Format
        };
    }

    /// <summary>
    /// returns the root directory where all Jobs are stored (Sibling to the Assets folder).
    /// </summary>
    private static string GetJobsRootDirectory()
    {
        // Put jobs next to Assets, but outside the Assets folder
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var jobsRoot = Path.Combine(projectRoot, JobsRootFolderName);

        if (!Directory.Exists(jobsRoot))
            Directory.CreateDirectory(jobsRoot);

        return jobsRoot;
    }

    /// <summary>
    /// Generates a valid full path for a specific Job's folder.
    /// Structure: [ProjectRoot]/[JobsFolder]/[WorkflowName_Slug]/[JobGuid]
    /// </summary>
    private static string GetJobFolderPath(AtlasWorkflowJobState job)
    {
        var root = GetJobsRootDirectory();
        var workflowSlug = SanitizeFolderName(job.WorkflowName ?? job.WorkflowId ?? "Workflow");

        // Use creation time + short id for nicer folder names
        var created = job.CreatedAtUtc == default ? DateTime.UtcNow : job.CreatedAtUtc;
        string timeStamp = created.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss");

        // Shorten the GUID for readability, keep uniqueness
        string shortId = string.IsNullOrEmpty(job.JobId)
            ? Guid.NewGuid().ToString("N").Substring(0, 8)
            : job.JobId.Replace("-", "").Substring(0, 8);

        string folderName = $"{timeStamp}_{shortId}";
        folderName = SanitizeFolderName(folderName);

        return Path.Combine(root, workflowSlug, folderName);
    }

    /// <summary>
    /// Replaces invalid file name characters with underscores.
    /// </summary>
    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Workflow" : name;
    }

    /// <summary>
    /// Deletes a job from the in-memory list and removes its folder from disk.
    /// Returns true if deletion succeeded.
    /// </summary>
    public static bool DeleteJob(AtlasWorkflowJobState job)
    {
        if (job == null)
            return false;

        try
        {
            // Delete folder on disk
            if (!string.IsNullOrEmpty(job.JobFolderPath) &&
                Directory.Exists(job.JobFolderPath))
            {
                Directory.Delete(job.JobFolderPath, true);
                AtlasLogger.LogJob($"Deleted job folder: {job.JobFolderPath}");
            }

            // Remove from runtime list
            bool removed = Jobs.Remove(job);
            if (removed)
            {
                AtlasLogger.LogJob($"Deleted job {job.JobId}");
                NotifyJobsMutated();
            }

            return removed;
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, $"Failed to delete job {job.JobId}");
            return false;
        }
    }


    #endregion
}