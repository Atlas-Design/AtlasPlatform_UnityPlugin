// In Packages/com.atlas.workflow/Editor/EditorWindow/AtlasWorkflowEditor.cs

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

public class AtlasWorkflowEditor : EditorWindow
{
    // --- Controllers and State ---
    private WorkflowStateController stateController;
    private WorkflowUIBuilder uiBuilder;
    private JobHistoryView historyView; // NEW

    private AtlasWorkflowState state;
    private AtlasWorkflowJobState selectedJob;


    // --- UI Element References ---
    private Button loadFileButton;
    private DropdownField libraryDropdown;
    private VisualElement libraryActiveDot;
    private VisualElement jobViewContainer;
    private Button runWorkflowButton;
    private ScrollView jobsList;
    private WorkflowJobView jobView;
    private Button jobsMenuBtn;

    private VisualElement runningJobsPanel;
    private ScrollView runningJobsList;
    private RunningJobsView runningJobsView;

    // A reference to our custom UI component instance


    [MenuItem("Window/Atlas Workflow")]
    public static void ShowWindow() { GetWindow<AtlasWorkflowEditor>("Atlas Workflow"); }

    #region Lifecycle & Initialization

    private void CreateGUI()
    {
        var root = rootVisualElement;
        var visualTree = LoadAsset<VisualTreeAsset>("AtlasWorkflowEditor");
        if (visualTree == null) return;
        visualTree.CloneTree(root);

        state = LoadAsset<AtlasWorkflowState>("AtlasWorkflowState");
        if (state == null) { root.Add(new Label("Critical Error: AtlasWorkflowState asset not found.")); return; }

        // --- Cleanup old temp files on editor window open ---
        AssetExporter.CleanupTempFiles();





        var paramStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(
            "Packages/com.atlas.workflow/Editor/EditorWindow/Styles/_ParamStyles.uss");
        if (paramStyles != null)
            rootVisualElement.styleSheets.Add(paramStyles);

        stateController = new WorkflowStateController(state);
        uiBuilder = new WorkflowUIBuilder(state);

        QueryUIElements(root);
        historyView = new JobHistoryView(jobsList, uiBuilder.Renderer, SelectJob);

        if (runningJobsPanel != null && runningJobsList != null)
        {
            runningJobsView = new RunningJobsView(runningJobsPanel, runningJobsList);
        }

        RegisterCallbacks();

        WorkflowManager.LoadJobsFromDisk();
        historyView.Refresh(WorkflowManager.Jobs);
        if (runningJobsView != null)
            runningJobsView.Refresh(WorkflowManager.Jobs);


        // --- Create and add our custom component ---
        jobView = new WorkflowJobView();
        jobViewContainer.Add(jobView);

        PopulateLibraryDropdown();
        UpdateUIBasedOnState();
    }

    private void QueryUIElements(VisualElement root)
    {
        loadFileButton = root.Q<Button>("load-file-button");
        libraryDropdown = root.Q<DropdownField>("library-dropdown");
        libraryActiveDot = root.Q<VisualElement>("library-active-dot");
        jobViewContainer = root.Q<VisualElement>("job-view-container");
        runWorkflowButton = root.Q<Button>("run-workflow-button");

        runningJobsPanel = root.Q<VisualElement>("running-jobs-panel");
        runningJobsList = root.Q<ScrollView>("running-jobs-list");

        jobsList = root.Q<ScrollView>("jobs-list");
        jobsMenuBtn = root.Q<Button>("jobs-menu-btn");
    }

    private void RegisterCallbacks()
    {
        loadFileButton.clicked += OnLoadFromFileClicked;
        libraryDropdown.RegisterValueChangedCallback(OnLibrarySelectionChanged);
        runWorkflowButton.clicked += OnRunWorkflowClicked;

        // Jobs History menu button
        if (jobsMenuBtn != null)
        {
            jobsMenuBtn.clicked += OnJobsMenuClicked;
        }
    }

    private void OnJobsMenuClicked()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Clear History"), false, () =>
        {
            historyView?.ClearHistory();
        });
        menu.ShowAsContext();
    }

    #endregion

    #region Event Handlers

    private void OnLoadFromFileClicked()
    {
        string path = EditorUtility.OpenFilePanel("Load Workflow", "", "json");
        if (string.IsNullOrEmpty(path)) return;
        string savedPath = WorkflowManager.SaveWorkflowToLibrary(path);
        if (savedPath == null) { EditorUtility.DisplayDialog("Error", "Failed to save workflow.", "OK"); return; }

        stateController.LoadWorkflowFromFile(savedPath);

        PopulateLibraryDropdown();
        libraryDropdown.SetValueWithoutNotify(Path.GetFileName(savedPath));
        UpdateUIBasedOnState();
    }

    private void OnLibrarySelectionChanged(ChangeEvent<string> evt)
    {
        // Ignore placeholder text or empty values
        if (string.IsNullOrEmpty(evt.newValue) || 
            evt.newValue == "Select a workflow..." || 
            evt.newValue == "No workflows - click Import")
            return;
            
        string filePath = Path.Combine(WorkflowManager.GetLibraryDirectory(), evt.newValue);
        stateController.LoadWorkflowFromFile(filePath);
        UpdateUIBasedOnState();
    }

    #endregion

    #region Workflow Execution Logic

    async void OnRunWorkflowClicked()
    {
        var statusLabel = jobView.Q<Label>("status-label");

        // 1) Per-job clone of the current state (isolated Inputs/Outputs)
        var jobState = WorkflowManager.CloneStateForJobRun(state);

        // 2) Create the job from the per-job state (snapshots from jobState)
        var job = WorkflowManager.CreateJobFromState(jobState);

        // Show "Running" immediately in both panels
        historyView.Refresh(WorkflowManager.Jobs);
        if (runningJobsView != null)
            runningJobsView.Refresh(WorkflowManager.Jobs);

        try
        {
            statusLabel.text = "Running...";

            // 3) Prepare inputs inside this job folder (mutates jobState.Inputs[].FilePath)
            var inputFilesForUpload = await PrepareInputFilesForJob(job, jobState);

            // 4) Run workflow using the per-job state + job-local files
            var outputResults = await AtlasAPIController.RunWorkflowAsync(jobState, inputFilesForUpload);

            if (outputResults != null)
            {
                // --- JOB-LOCAL STATE (for history + job folder) -------------------

                // Map results into jobState
                MapOutputResultsToState(jobState, outputResults);

                // Copy generated outputs into the job folder and fix FilePath in jobState
                CopyOutputFilesToJobFolder(job, jobState);

                // Snapshot jobState into the job record -> job.json uses this
                WorkflowManager.UpdateJobInputsFromState(job, jobState);
                WorkflowManager.UpdateJobOutputsFromState(job, jobState);
                WorkflowManager.MarkJobSucceeded(job);

                // --- SHARED EDITOR STATE (for current workflow UI) ----------------
                MapOutputResultsToState(state, outputResults);
                EditorUtility.SetDirty(state);

                statusLabel.text = "Complete";

                // Refresh History + Running Jobs (job disappears from running)
                historyView.Refresh(WorkflowManager.Jobs);
                if (runningJobsView != null)
                    runningJobsView.Refresh(WorkflowManager.Jobs);

                SelectJob(job);
            }
            else
            {
                statusLabel.text = "Failed";
                WorkflowManager.MarkJobFailed(job, "RunWorkflowAsync returned null.");
                historyView.Refresh(WorkflowManager.Jobs);
                if (runningJobsView != null)
                    runningJobsView.Refresh(WorkflowManager.Jobs);
            }
        }
        catch (System.Exception ex)
        {
            statusLabel.text = "Error";
            WorkflowManager.MarkJobFailed(job, ex.Message);

            historyView.Refresh(WorkflowManager.Jobs);
            if (runningJobsView != null)
                runningJobsView.Refresh(WorkflowManager.Jobs);

            AtlasLogger.LogException(ex, "Workflow execution failed");
        }
        finally
        {
            // --- Memory cleanup: Destroy the cloned ScriptableObject to prevent memory leaks ---
            if (jobState != null)
            {
                DestroyImmediate(jobState);
            }
        }
    }

    /// <summary>
    /// Copies generated files (images/meshes) from their temp location to the permanent job history folder.
    /// </summary>
    private void CopyOutputFilesToJobFolder(AtlasWorkflowJobState job, AtlasWorkflowState state)
    {
        if (state.Outputs == null || string.IsNullOrEmpty(job.JobFolderPath)) return;

        try
        {
            var outputsFolder = Path.Combine(job.JobFolderPath, "outputs");
            if (!Directory.Exists(outputsFolder))
                Directory.CreateDirectory(outputsFolder);

            foreach (var outputState in state.Outputs)
            {
                if (outputState.ParamType != ParamType.image &&
                    outputState.ParamType != ParamType.mesh)
                    continue;

                if (string.IsNullOrEmpty(outputState.FilePath) ||
                    !File.Exists(outputState.FilePath))
                    continue;

                var ext = Path.GetExtension(outputState.FilePath);
                var safeParamId = string.IsNullOrEmpty(outputState.ParamId)
                    ? "Output"
                    : outputState.ParamId;

                foreach (var c in Path.GetInvalidFileNameChars())
                    safeParamId = safeParamId.Replace(c, '_');

                var fileName = $"Output_{safeParamId}{ext}";
                var destPath = Path.Combine(outputsFolder, fileName);

                File.Copy(outputState.FilePath, destPath, true);
                outputState.FilePath = destPath;
            }
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to copy output files to job folder");
        }
    }

    /// <summary>
    /// Ensures all asset inputs (image/mesh) are copied into this job's folder
    /// as "Input_*" files and returns a map ParamId -> file path to upload.
    /// </summary>
    private async Task<Dictionary<string, string>> PrepareInputFilesForJob(
        AtlasWorkflowJobState job,
        AtlasWorkflowState state)
    {
        var result = new Dictionary<string, string>();

        if (state.Inputs == null || string.IsNullOrEmpty(job.JobFolderPath))
            return result;

        try
        {
            var inputsFolder = Path.Combine(job.JobFolderPath, "inputs");
            if (!Directory.Exists(inputsFolder))
                Directory.CreateDirectory(inputsFolder);

            foreach (var input in state.Inputs)
            {
                if (input.ParamType != ParamType.image && input.ParamType != ParamType.mesh)
                    continue;

                string sourcePath = null;

                // 1) If input comes from a direct file path, use that first
                if (input.SourceType == InputSourceType.FilePath &&
                    !string.IsNullOrEmpty(input.FilePath) &&
                    File.Exists(input.FilePath))
                {
                    sourcePath = input.FilePath;
                }

                // 2) If input is a project asset, export it to a temp file
                if (sourcePath == null && input.SourceType == InputSourceType.Project)
                {
                    if (input.ParamType == ParamType.image && input.ImageValue != null)
                    {
                        sourcePath = await AssetExporter.ExportTextureAsPng(input.ImageValue);
                    }
                    else if (input.ParamType == ParamType.mesh && input.MeshValue != null)
                    {
                        sourcePath = await AssetExporter.ExportGameObjectAsGlb(input.MeshValue);
                    }
                }

                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                    continue;

                var ext = Path.GetExtension(sourcePath);
                var safeParamId = string.IsNullOrEmpty(input.ParamId) ? "Param" : input.ParamId;

                foreach (var c in Path.GetInvalidFileNameChars())
                    safeParamId = safeParamId.Replace(c, '_');

                var destName = $"Input_{safeParamId}{ext}";
                var destPath = Path.Combine(inputsFolder, destName);

                File.Copy(sourcePath, destPath, true);

                // This is now the canonical file path for this run
                input.FilePath = destPath;
                result[input.ParamId] = destPath;
            }
        }
        catch (System.Exception ex)
        {
            AtlasLogger.LogException(ex, "Failed to prepare input files for job");
        }

        return result;
    }

    /// <summary>
    /// Applies the raw output dictionary returned from the API back into the Workflow State.
    /// For asset outputs, this sets FilePath to the local path returned by the API controller.
    /// </summary>
    private void MapOutputResultsToState(
        AtlasWorkflowState state,
        Dictionary<string, object> outputResults)
    {
        if (state.Outputs == null || outputResults == null)
            return;

        foreach (var outputState in state.Outputs)
        {
            if (!outputResults.TryGetValue(outputState.ParamId, out var value) || value == null)
                continue;

            try
            {
                switch (outputState.ParamType)
                {
                    case ParamType.boolean:
                        outputState.BoolValue = System.Convert.ToBoolean(value);
                        break;

                    case ParamType.number:
                        outputState.NumberValue = System.Convert.ToSingle(value);
                        break;

                    case ParamType.@string:
                        outputState.StringValue = value.ToString();
                        break;

                    case ParamType.image:
                    case ParamType.mesh:
                        var path = value.ToString();
                        if (!string.IsNullOrEmpty(path))
                            outputState.FilePath = path;
                        break;
                }
            }
            catch (System.Exception ex)
            {
                AtlasLogger.LogException(ex, $"Failed to map output '{outputState.ParamId}'");
            }
        }
    }

    #endregion

    #region UI State Management

    /// <summary>
    /// Updates the main view visibility and content based on whether a workflow is currently loaded.
    /// </summary>
    private void UpdateUIBasedOnState()
    {
        bool isWorkflowLoaded = !string.IsNullOrEmpty(state.ActiveName);
        jobView.style.display = isWorkflowLoaded ? DisplayStyle.Flex : DisplayStyle.None;
        if (runWorkflowButton != null)
        {
            runWorkflowButton.EnableInClassList("hidden", !isWorkflowLoaded);
            if (isWorkflowLoaded)
            {
                runWorkflowButton.text = $"▶  Run {state.ActiveName}";
            }
        }

        // Update library active dot
        if (libraryActiveDot != null)
            libraryActiveDot.EnableInClassList("inactive", !isWorkflowLoaded);

        // Update dropdown placeholder when no workflow loaded
        if (!isWorkflowLoaded)
        {
            var workflows = WorkflowManager.GetSavedWorkflows();
            var textElement = libraryDropdown?.Q<TextElement>(className: "unity-base-popup-field__text");
            if (textElement != null)
            {
                textElement.text = workflows.Count == 0 
                    ? "No workflows - click Import" 
                    : "Select a workflow...";
            }
        }

        if (isWorkflowLoaded)
        {
            jobView.Populate(state, uiBuilder);
        }
    }

    private void SelectJob(AtlasWorkflowJobState job)
    {
        selectedJob = job;
        if (job == null || jobView == null) return;

        var statusLabel = jobView.Q<Label>("status-label");
        if (statusLabel != null)
        {
            statusLabel.text = (job == null)
                ? "Idle"
                : $"Last job: {job.Status} ({job.CreatedAtUtc.ToLocalTime():HH:mm:ss})";
        }
    }

    private void PopulateLibraryDropdown()
    {
        var workflows = WorkflowManager.GetSavedWorkflows().Select(Path.GetFileName).ToList();
        libraryDropdown.choices = workflows;
        
        // Set placeholder text when no workflow is selected
        if (string.IsNullOrEmpty(libraryDropdown.value) || !workflows.Contains(libraryDropdown.value))
        {
            if (workflows.Count == 0)
            {
                libraryDropdown.SetValueWithoutNotify("");
                // Use a visual hint through the text input
                var textElement = libraryDropdown.Q<TextElement>(className: "unity-base-popup-field__text");
                if (textElement != null)
                    textElement.text = "No workflows - click Import";
            }
            else
            {
                libraryDropdown.SetValueWithoutNotify("");
                var textElement = libraryDropdown.Q<TextElement>(className: "unity-base-popup-field__text");
                if (textElement != null)
                    textElement.text = "Select a workflow...";
            }
        }
    }

    #endregion

    #region Helpers

    private T LoadAsset<T>(string assetName) where T : UnityEngine.Object
    {
        var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name} {assetName}");
        if (guids.Length == 0) { Debug.LogError($"Could not find asset: {assetName}"); return null;
                                 }
        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    #endregion



}