// In Packages/com.atlas.workflow/Editor/EditorWindow/AtlasWorkflowEditor.cs

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class AtlasWorkflowEditor : EditorWindow
{
    private WorkflowStateController stateController;
    private WorkflowUIBuilder uiBuilder;

    private AtlasWorkflowState state;

    private WorkflowLibraryPicker libraryPicker;
    private VisualElement jobViewContainer;
    private Button runWorkflowButton;
    private Button openWorkflowJobsButton;
    private WorkflowJobView jobView;

    private CancellationTokenSource _activeRunCts;

    [MenuItem("Atlas/Atlas Workflow", false, 0)]
    public static void ShowWindow() { GetWindow<AtlasWorkflowEditor>("Atlas Workflow"); }

    #region Lifecycle & Initialization

    private void CreateGUI()
    {
        var root = rootVisualElement;
        var visualTree = LoadAsset<VisualTreeAsset>("AtlasWorkflowEditor");
        if (visualTree == null) return;
        visualTree.CloneTree(root);

        // Docked/tabbed EditorWindows need an explicit flex cap so nested ScrollViews can scroll.
        root.style.flexGrow = 1;
        root.style.minHeight = 0;
        root.style.flexDirection = FlexDirection.Column;

        state = LoadAsset<AtlasWorkflowState>("AtlasWorkflowState");
        if (state == null) { root.Add(new Label("Critical Error: AtlasWorkflowState asset not found.")); return; }

        AssetExporter.CleanupTempFiles();

        var paramStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(
            "Packages/com.atlas.workflow/Editor/EditorWindow/Styles/_ParamStyles.uss");
        if (paramStyles != null)
            rootVisualElement.styleSheets.Add(paramStyles);

        stateController = new WorkflowStateController(state);
        uiBuilder = new WorkflowUIBuilder(state);

        var pickerHost = root.Q<VisualElement>("workflow-library-picker-host");
        if (pickerHost != null)
        {
            libraryPicker = new WorkflowLibraryPicker();
            pickerHost.Add(libraryPicker);
        }

        QueryUIElements(root);
        EnforceWorkflowFooterLayout();
        RegisterCallbacks();

        WorkflowEditorRunSession.JobSelectedForStatus += OnJobSelectedForStatus;

        WorkflowManager.LoadJobsFromDisk();

        jobView = new WorkflowJobView();
        jobViewContainer.Add(jobView);

        libraryPicker?.RefreshDropdownChoices();
        UpdateUIBasedOnState();
    }

    private void QueryUIElements(VisualElement root)
    {
        jobViewContainer = root.Q<VisualElement>("job-view-container");
        runWorkflowButton = root.Q<Button>("run-workflow-button");
        openWorkflowJobsButton = root.Q<Button>("open-workflow-jobs-button");
    }

    /// <summary>
    /// Ensures footer order/labels even if an older cached UXML or theme is in play; close and reopen the window after package updates.
    /// </summary>
    private void EnforceWorkflowFooterLayout()
    {
        if (openWorkflowJobsButton == null || runWorkflowButton == null)
            return;

        var row = openWorkflowJobsButton.parent;
        var spacer = row?.Q<VisualElement>(className: "workflow-actions-spacer");
        if (row == null || spacer == null)
            return;

        openWorkflowJobsButton.RemoveFromHierarchy();
        spacer.RemoveFromHierarchy();
        runWorkflowButton.RemoveFromHierarchy();
        row.Add(openWorkflowJobsButton);
        row.Add(spacer);
        row.Add(runWorkflowButton);

        openWorkflowJobsButton.text = "Open Job History";
        openWorkflowJobsButton.tooltip = "Open Atlas Job History (running jobs and past runs)";
        openWorkflowJobsButton.AddToClassList("open-workflow-jobs-button");
    }

    private void RegisterCallbacks()
    {
        if (libraryPicker?.ImportButton != null)
            libraryPicker.ImportButton.clicked += OnLoadFromFileClicked;
        if (libraryPicker?.Dropdown != null)
            libraryPicker.Dropdown.RegisterValueChangedCallback(OnLibrarySelectionChanged);
        if (runWorkflowButton != null)
            runWorkflowButton.clicked += OnRunWorkflowClicked;

        if (libraryPicker?.DeleteButton != null)
            libraryPicker.DeleteButton.clicked += OnDeleteWorkflowClicked;

        if (openWorkflowJobsButton != null)
            openWorkflowJobsButton.clicked += OnOpenWorkflowJobsClicked;
    }

    private void OnOpenWorkflowJobsClicked()
    {
        AtlasWorkflowJobsWindow.ShowWindow();
    }

    private void OnDestroy()
    {
        WorkflowEditorRunSession.JobSelectedForStatus -= OnJobSelectedForStatus;
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

        libraryPicker?.RefreshDropdownChoices();
        libraryPicker?.Dropdown?.SetValueWithoutNotify(Path.GetFileName(savedPath));
        UpdateUIBasedOnState();
    }

    private void OnLibrarySelectionChanged(ChangeEvent<string> evt)
    {
        if (string.IsNullOrEmpty(evt.newValue) ||
            evt.newValue == "Select a workflow..." ||
            evt.newValue == "No workflows - click Import")
            return;

        string filePath = Path.Combine(WorkflowManager.GetLibraryDirectory(), evt.newValue);
        stateController.LoadWorkflowFromFile(filePath);
        UpdateUIBasedOnState();
    }

    private void OnDeleteWorkflowClicked()
    {
        string selectedWorkflow = libraryPicker?.Dropdown?.value;

        if (string.IsNullOrEmpty(selectedWorkflow) ||
            selectedWorkflow == "Select a workflow..." ||
            selectedWorkflow == "No workflows - click Import")
        {
            EditorUtility.DisplayDialog("No Workflow Selected", "Please select a workflow to delete.", "OK");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Delete Workflow",
            $"Are you sure you want to delete '{selectedWorkflow}'?\n\nThis action cannot be undone.",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        bool deleted = WorkflowManager.DeleteWorkflowFromLibrary(selectedWorkflow);

        if (deleted)
        {
            stateController.ClearState();

            libraryPicker?.RefreshDropdownChoices();
            UpdateUIBasedOnState();

            Debug.Log($"[Atlas] Deleted workflow: {selectedWorkflow}");
        }
        else
        {
            EditorUtility.DisplayDialog("Error", $"Failed to delete workflow '{selectedWorkflow}'.", "OK");
        }
    }

    #endregion

    #region Workflow Execution Logic

    async void OnRunWorkflowClicked()
    {
        var statusLabel = jobView.Q<Label>("status-label");

        var jobState = WorkflowManager.CloneStateForJobRun(state);
        var job = WorkflowManager.CreateJobFromState(jobState);

        _activeRunCts = new CancellationTokenSource();
        WorkflowEditorRunSession.BeginActiveRun(job, _activeRunCts);

        try
        {
            statusLabel.text = "Running...";

            var inputFilesForUpload = await WorkflowJobRunHelper.PrepareInputFilesForJobAsync(job, jobState);

            var outputResults = await AtlasAPIController.RunWorkflowWithPollingAsync(
                jobState,
                job,
                inputFilesForUpload,
                cancellationToken: _activeRunCts.Token);

            if (outputResults != null)
            {
                WorkflowJobRunHelper.MapOutputResultsToState(jobState, outputResults);
                WorkflowJobRunHelper.CopyOutputFilesToJobFolder(job, jobState);

                WorkflowManager.UpdateJobInputsFromState(job, jobState);
                WorkflowManager.UpdateJobOutputsFromState(job, jobState);
                WorkflowManager.MarkJobSucceeded(job);

                WorkflowJobRunHelper.MapOutputResultsToState(state, outputResults);
                EditorUtility.SetDirty(state);

                statusLabel.text = "Complete";

                WorkflowEditorRunSession.NotifyJobSelected(job);
            }
            else
            {
                if (job.Status == JobStatus.Cancelled)
                {
                    statusLabel.text = "Cancelled";
                }
                else
                {
                    statusLabel.text = "Failed";
                    WorkflowManager.MarkJobFailed(job, "Workflow execution returned null (check logs for details).");
                }
            }
        }
        catch (System.Exception ex)
        {
            statusLabel.text = "Error";
            WorkflowManager.MarkJobFailed(job, ex.Message);

            AtlasLogger.LogException(ex, "Workflow execution failed");
        }
        finally
        {
            _activeRunCts?.Dispose();
            _activeRunCts = null;
            WorkflowEditorRunSession.EndActiveRun();

            if (jobState != null)
                DestroyImmediate(jobState);
        }
    }

    #endregion

    #region UI State Management

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

        libraryPicker?.SetActiveWorkflowLoaded(isWorkflowLoaded);
        libraryPicker?.SetDeleteEnabled(isWorkflowLoaded);
        libraryPicker?.RefreshPlaceholderForEmptyState(isWorkflowLoaded);

        if (isWorkflowLoaded)
        {
            jobView.Populate(state, uiBuilder);
        }
    }

    private void OnJobSelectedForStatus(AtlasWorkflowJobState job)
    {
        SelectJob(job);
    }

    private void SelectJob(AtlasWorkflowJobState job)
    {
        if (jobView == null) return;

        var statusLabel = jobView.Q<Label>("status-label");
        if (statusLabel != null)
        {
            statusLabel.text = (job == null)
                ? "Idle"
                : $"Last job: {job.Status} ({job.CreatedAtUtc.ToLocalTime():HH:mm:ss})";
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
