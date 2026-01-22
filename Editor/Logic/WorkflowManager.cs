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
    /// Runtime cache of all loaded job states.
    /// </summary>
    public static readonly List<AtlasWorkflowJobState> Jobs = new List<AtlasWorkflowJobState>();

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

        return job;
    }

    /// <summary>
    /// Marks a running job as Succeeded, sets progress to 100%, and saves to disk.
    /// </summary>
    public static void MarkJobSucceeded(AtlasWorkflowJobState job)
    {
        job.Status = JobStatus.Succeeded;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.Progress01 = 1f;
        SaveJobToDisk(job);
    }

    /// <summary>
    /// Marks a running job as Failed, records the error message, and saves to disk.
    /// </summary>
    public static void MarkJobFailed(AtlasWorkflowJobState job, string errorMessage)
    {
        job.Status = JobStatus.Failed;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.ErrorMessage = errorMessage;
        job.Progress01 = 1f;
        SaveJobToDisk(job);
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

                    FilePath = src.FilePath
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

                    FilePath = src.FilePath
                };
                clone.Outputs.Add(p);
            }
        }

        return clone;
    }

    #endregion

    #region State & Data Mapping

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
    public static void SaveJobToDisk(AtlasWorkflowJobState job)
    {
        try
        {
            if (string.IsNullOrEmpty(job.JobFolderPath))
            {
                job.JobFolderPath = GetJobFolderPath(job);
            }

            if (!Directory.Exists(job.JobFolderPath))
                Directory.CreateDirectory(job.JobFolderPath);

            string jobFilePath = Path.Combine(job.JobFolderPath, JobFileName);

            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                PreserveReferencesHandling = PreserveReferencesHandling.None,
                TypeNameHandling = TypeNameHandling.None
            };

            var json = JsonConvert.SerializeObject(job, Formatting.Indented, settings);
            File.WriteAllText(jobFilePath, json);
            
            AtlasLogger.LogJob($"Saved job {job.JobId} to: {jobFilePath}");
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, $"Failed to save job {job.JobId}");
        }
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
                        // Ensure JobFolderPath is set (in case older files didn't have it)
                        if (string.IsNullOrEmpty(job.JobFolderPath))
                            job.JobFolderPath = Path.GetDirectoryName(jobFilePath);

                        Jobs.Add(job);
                    }
                }
                catch (System.Exception ex)
                {
                    AtlasLogger.LogException(ex, $"Failed to load job from {jobFilePath}");
                }
            }

            AtlasLogger.LogJob($"Loaded {Jobs.Count} job(s) from disk.");
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to load jobs");
        }
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

            // For image/mesh, this is what we actually care about in history:
            FilePath = source.FilePath
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