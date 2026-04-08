using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 10: save/load batch drafts and run manifests under persistentDataPath (JSON, Newtonsoft).
/// </summary>
public static class WorkflowBatchPersistence
{
    public const int SchemaVersion = 1;
    private const string BatchesFolderName = "AtlasWorkflowBatches";
    private const string DraftsSubfolder = "drafts";
    private const string RunsSubfolder = "runs";
    private const string DraftExtension = ".atlasbatch.json";

    private static JsonSerializerSettings JsonSettings => new JsonSerializerSettings
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
    };

    public static string GetBatchesRootDirectory()
    {
        string path = Path.Combine(Application.persistentDataPath, BatchesFolderName);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDraftsDirectory()
    {
        string path = Path.Combine(GetBatchesRootDirectory(), DraftsSubfolder);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    public static string GetRunsDirectory()
    {
        string path = Path.Combine(GetBatchesRootDirectory(), RunsSubfolder);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Lists draft files (full paths), newest first by write time.</summary>
    public static List<string> ListDraftFiles()
    {
        var dir = GetDraftsDirectory();
        if (!Directory.Exists(dir))
            return new List<string>();
        return Directory.GetFiles(dir, "*" + DraftExtension, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    /// <summary>Writes a draft to an absolute path (e.g. from Save File dialog).</summary>
    public static bool SaveDraftToPath(
        string fullPath,
        WorkflowBatchDefinition definition,
        AtlasWorkflowState workflow,
        int maxConcurrentRuns,
        int maxTransientRetriesPerInstance,
        out string error)
    {
        error = null;

        if (definition == null || workflow == null)
        {
            error = "Invalid batch or workflow.";
            return false;
        }

        if (string.IsNullOrEmpty(fullPath))
        {
            error = "No path.";
            return false;
        }

        try
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            WorkflowBatchValidator.CaptureWorkflowFingerprint(workflow, definition);

            var doc = new WorkflowBatchDraftDocument
            {
                SchemaVersion = SchemaVersion,
                Kind = "draft",
                WorkflowLibraryFileName = definition.WorkflowLibraryFileName,
                WorkflowActiveApiId = definition.WorkflowActiveApiId,
                WorkflowActiveName = definition.WorkflowActiveName,
                WorkflowVersion = definition.WorkflowVersion,
                WorkflowBaseUrl = definition.WorkflowBaseUrl,
                MaxConcurrentRuns = maxConcurrentRuns,
                MaxTransientRetriesPerInstance = maxTransientRetriesPerInstance,
                Rows = definition.Rows.Select(RowToDto).ToList()
            };

            File.WriteAllText(fullPath, JsonConvert.SerializeObject(doc, JsonSettings));
            AtlasLogger.LogFile($"Saved batch draft: {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool LoadDraft(
        string fullPath,
        AtlasWorkflowState workflow,
        WorkflowStateController controller,
        WorkflowBatchDefinition definition,
        out int maxConcurrentRuns,
        out int maxTransientRetriesPerInstance,
        out string error)
    {
        maxConcurrentRuns = 2;
        maxTransientRetriesPerInstance = 2;
        error = null;
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            error = "File not found.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var doc = JsonConvert.DeserializeObject<WorkflowBatchDraftDocument>(json, JsonSettings);
            if (doc == null || doc.SchemaVersion < 1 || doc.Kind != "draft")
            {
                error = "Unrecognized batch draft format.";
                return false;
            }

            if (string.IsNullOrEmpty(doc.WorkflowLibraryFileName))
            {
                error = "Draft has no workflow library filename.";
                return false;
            }

            string workflowPath = Path.Combine(WorkflowManager.GetLibraryDirectory(), doc.WorkflowLibraryFileName);
            if (!File.Exists(workflowPath))
            {
                error = $"Workflow file not in library: {doc.WorkflowLibraryFileName}";
                return false;
            }

            controller.LoadWorkflowFromFile(workflowPath);

            definition.WorkflowLibraryFileName = doc.WorkflowLibraryFileName;
            definition.WorkflowActiveApiId = doc.WorkflowActiveApiId;
            definition.WorkflowActiveName = doc.WorkflowActiveName;
            definition.WorkflowVersion = doc.WorkflowVersion;
            definition.WorkflowBaseUrl = doc.WorkflowBaseUrl;
            definition.Rows.Clear();

            if (doc.Rows != null)
            {
                foreach (var rowDto in doc.Rows)
                {
                    var row = RowFromDto(rowDto, workflow.Inputs);
                    definition.Rows.Add(row);
                }
            }

            maxConcurrentRuns = doc.MaxConcurrentRuns > 0 ? doc.MaxConcurrentRuns : 2;
            maxTransientRetriesPerInstance = doc.MaxTransientRetriesPerInstance >= 0 ? doc.MaxTransientRetriesPerInstance : 2;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static BatchRowDto RowToDto(WorkflowBatchRow row)
    {
        var dto = new BatchRowDto { InputsByParamId = new Dictionary<string, ParamCellDto>() };
        if (row?.InputsByParamId == null)
            return dto;

        foreach (var kv in row.InputsByParamId)
        {
            if (kv.Value != null)
                dto.InputsByParamId[kv.Key] = ParamCellDto.FromState(kv.Value);
        }

        return dto;
    }

    private static WorkflowBatchRow RowFromDto(BatchRowDto dto, IReadOnlyList<AtlasWorkflowParamState> schemaInputs)
    {
        var row = new WorkflowBatchRow();
        if (dto?.InputsByParamId == null || schemaInputs == null)
            return row;

        foreach (var schema in schemaInputs)
        {
            if (schema == null || string.IsNullOrEmpty(schema.ParamId))
                continue;
            if (!dto.InputsByParamId.TryGetValue(schema.ParamId, out var cellDto) || cellDto == null)
                continue;

            var cell = WorkflowBatchRow.CloneParamCell(schema);
            ParamCellDto.ApplyToCell(cellDto, cell);
            row.InputsByParamId[schema.ParamId] = cell;
        }

        return row;
    }

    #region DTOs (draft + manifest rows)

    [Serializable]
    private class WorkflowBatchDraftDocument
    {
        public int SchemaVersion;
        public string Kind;
        public string WorkflowLibraryFileName;
        public string WorkflowActiveApiId;
        public string WorkflowActiveName;
        public string WorkflowVersion;
        public string WorkflowBaseUrl;
        public int MaxConcurrentRuns;
        public int MaxTransientRetriesPerInstance;
        public List<BatchRowDto> Rows;
    }

    [Serializable]
    public class BatchRowDto
    {
        public Dictionary<string, ParamCellDto> InputsByParamId;
    }

    [Serializable]
    public class ParamCellDto
    {
        public string ParamType;
        public string SourceType;
        public bool BoolValue;
        public float NumberValue;
        public string StringValue;
        public string FilePath;
        public string ImageAssetGuid;
        public string MeshAssetGuid;
        /// <summary>Optional; mirrors <see cref="AtlasWorkflowParamState.Format"/> (e.g. audio) for draft round-trip.</summary>
        public string Format;

        public static ParamCellDto FromState(AtlasWorkflowParamState p)
        {
            var d = new ParamCellDto
            {
                ParamType = p.ParamType.ToString(),
                SourceType = p.SourceType.ToString(),
                BoolValue = p.BoolValue,
                NumberValue = p.NumberValue,
                StringValue = p.StringValue,
                FilePath = p.FilePath,
                Format = p.Format
            };

            if (p.SourceType == InputSourceType.Project)
            {
                if (p.ImageValue != null)
                {
                    string ap = AssetDatabase.GetAssetPath(p.ImageValue);
                    if (!string.IsNullOrEmpty(ap))
                        d.ImageAssetGuid = AssetDatabase.AssetPathToGUID(ap);
                }

                if (p.MeshValue != null)
                {
                    string ap = AssetDatabase.GetAssetPath(p.MeshValue);
                    if (!string.IsNullOrEmpty(ap))
                        d.MeshAssetGuid = AssetDatabase.AssetPathToGUID(ap);
                }
            }

            return d;
        }

        public static void ApplyToCell(ParamCellDto d, AtlasWorkflowParamState cell)
        {
            if (d == null || cell == null)
                return;

            if (!string.IsNullOrEmpty(d.SourceType) && Enum.TryParse(d.SourceType, out InputSourceType st))
                cell.SourceType = st;

            cell.BoolValue = d.BoolValue;
            cell.NumberValue = d.NumberValue;
            cell.StringValue = d.StringValue ?? "";
            cell.FilePath = d.FilePath;
            if (d.Format != null)
                cell.Format = d.Format;

            cell.ImageValue = null;
            cell.MeshValue = null;

            if (cell.SourceType == InputSourceType.FilePath)
                return;

            if (!string.IsNullOrEmpty(d.ImageAssetGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(d.ImageAssetGuid);
                if (!string.IsNullOrEmpty(path))
                    cell.ImageValue = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            if (!string.IsNullOrEmpty(d.MeshAssetGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(d.MeshAssetGuid);
                if (!string.IsNullOrEmpty(path))
                    cell.MeshValue = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        }
    }

    #endregion

    #region Run manifest

    [Serializable]
    internal class WorkflowBatchRunManifestDocument
    {
        public int SchemaVersion;
        public string Kind;
        public string BatchId;
        public string BatchName;
        public string WorkflowLibraryFileName;
        public string WorkflowActiveApiId;
        public string WorkflowActiveName;
        public string WorkflowVersion;
        public string WorkflowBaseUrl;
        public int MaxConcurrentRuns;
        public int MaxTransientRetriesPerInstance;
        public string StartedUtc;
        public string CompletedUtc;
        /// <summary>running | completed | cancelled</summary>
        public string RunStatus;
        public List<BatchRowDto> RowsSnapshot;
        public List<string> JobIdsByRowIndex;
    }

    /// <summary>Thread-safe manifest updates for a single batch run.</summary>
    public sealed class RunManifestWriter
    {
        private readonly string _path;
        private readonly object _sync = new object();

        public RunManifestWriter(string manifestFilePath)
        {
            _path = manifestFilePath;
        }

        public static string CreateManifestFilePath(string batchId)
        {
            string safe = string.IsNullOrEmpty(batchId) ? Guid.NewGuid().ToString("N") : batchId;
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            return Path.Combine(GetRunsDirectory(), $"run-{safe}.json");
        }

        public void WriteInitial(
            string batchId,
            string batchName,
            WorkflowBatchDefinition definition,
            int maxConcurrentRuns,
            int maxTransientRetriesPerInstance,
            IReadOnlyList<WorkflowBatchRow> rowsSnapshot)
        {
            var doc = new WorkflowBatchRunManifestDocument
            {
                SchemaVersion = SchemaVersion,
                Kind = "run",
                BatchId = batchId,
                BatchName = batchName,
                WorkflowLibraryFileName = definition.WorkflowLibraryFileName,
                WorkflowActiveApiId = definition.WorkflowActiveApiId,
                WorkflowActiveName = definition.WorkflowActiveName,
                WorkflowVersion = definition.WorkflowVersion,
                WorkflowBaseUrl = definition.WorkflowBaseUrl,
                MaxConcurrentRuns = maxConcurrentRuns,
                MaxTransientRetriesPerInstance = maxTransientRetriesPerInstance,
                StartedUtc = DateTime.UtcNow.ToString("o"),
                RunStatus = "running",
                RowsSnapshot = rowsSnapshot.Select(RowToDto).ToList(),
                JobIdsByRowIndex = Enumerable.Range(0, rowsSnapshot.Count).Select(_ => (string)null).ToList()
            };

            WriteDoc(doc);
        }

        public void RecordJobCreated(int rowIndex, string jobId)
        {
            lock (_sync)
            {
                var doc = ReadDoc();
                if (doc == null || doc.JobIdsByRowIndex == null)
                    return;
                while (doc.JobIdsByRowIndex.Count <= rowIndex)
                    doc.JobIdsByRowIndex.Add(null);
                doc.JobIdsByRowIndex[rowIndex] = jobId;
                WriteDoc(doc);
            }
        }

        public void Finalize(bool cancelled)
        {
            lock (_sync)
            {
                var doc = ReadDoc();
                if (doc == null)
                    return;
                doc.CompletedUtc = DateTime.UtcNow.ToString("o");
                doc.RunStatus = cancelled ? "cancelled" : "completed";
                WriteDoc(doc);
            }
        }

        private WorkflowBatchRunManifestDocument ReadDoc()
        {
            try
            {
                if (!File.Exists(_path))
                    return null;
                return JsonConvert.DeserializeObject<WorkflowBatchRunManifestDocument>(File.ReadAllText(_path), JsonSettings);
            }
            catch
            {
                return null;
            }
        }

        private void WriteDoc(WorkflowBatchRunManifestDocument doc)
        {
            File.WriteAllText(_path, JsonConvert.SerializeObject(doc, JsonSettings));
        }
    }

    /// <summary>
    /// After a manual retry from job history, record the new job id for this batch row in the run manifest (if present).
    /// </summary>
    public static void TryUpdateManifestJobIdForBatchRow(string batchId, int rowIndex, string newJobId)
    {
        if (string.IsNullOrEmpty(batchId) || string.IsNullOrEmpty(newJobId) || rowIndex < 0)
            return;

        string path = RunManifestWriter.CreateManifestFilePath(batchId);
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonConvert.DeserializeObject<WorkflowBatchRunManifestDocument>(json, JsonSettings);
            if (doc?.JobIdsByRowIndex == null)
                return;
            while (doc.JobIdsByRowIndex.Count <= rowIndex)
                doc.JobIdsByRowIndex.Add(null);
            doc.JobIdsByRowIndex[rowIndex] = newJobId;
            File.WriteAllText(path, JsonConvert.SerializeObject(doc, JsonSettings));
        }
        catch (Exception ex)
        {
            AtlasLogger.LogException(ex, "TryUpdateManifestJobIdForBatchRow");
        }
    }

    #endregion
}
