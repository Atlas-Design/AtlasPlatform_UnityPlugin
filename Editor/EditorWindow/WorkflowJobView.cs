// In Packages/com.atlas.workflow/Editor/EditorWindow/WorkflowJobView.cs

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Our new class inherits from VisualElement, making it a UI component itself.
public class WorkflowJobView : VisualElement
{
    // --- Internal UI References ---
    private Label workflowNameLabel;
    private Label statusLabel;
    private ProgressBar progressBar;
    private VisualElement inputsContainer;
    private VisualElement outputsContainer;
    private Label detailsApiId, detailsBaseUrl, detailsVersion;
    private Foldout detailsFoldout;

    public WorkflowJobView()
    {
        // Load the UXML template for this component
        var guids = AssetDatabase.FindAssets("t:VisualTreeAsset _WorkflowJobView");
        if (guids.Length == 0) return;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

        // Clone the UXML into this custom element
        visualTree.CloneTree(this);

        // Query for the internal elements that we need to control
        workflowNameLabel = this.Q<Label>("workflow-name-label");
        statusLabel = this.Q<Label>("status-label");
        progressBar = this.Q<ProgressBar>("progress-bar");
        inputsContainer = this.Q<VisualElement>("inputs-container");
        outputsContainer = this.Q<VisualElement>("outputs-container");
        detailsApiId = this.Q<Label>("details-api-id");
        detailsBaseUrl = this.Q<Label>("details-base-url");
        detailsVersion = this.Q<Label>("details-version");
        detailsFoldout = this.Q<Foldout>("details-foldout");

    }

    // Public method to update this component's UI from the state object.
    public void Populate(AtlasWorkflowState state, WorkflowUIBuilder uiBuilder)
    {
        if (state == null || uiBuilder == null) return;

        bool isLoaded = !string.IsNullOrEmpty(state.ActiveName);

        // Populate the details
        workflowNameLabel.text = isLoaded ? state.ActiveName : "No Workflow Loaded";
        detailsApiId.text = $"API ID: {state.ActiveApiId}";
        detailsBaseUrl.text = $"Base URL: {state.BaseUrl}";
        detailsVersion.text = $"Version: {state.Version}";

        // Use the UIBuilder to populate the dynamic lists
        uiBuilder.PopulateInputs(inputsContainer);
        uiBuilder.PopulateOutputs(outputsContainer);
    }

    public void PopulateFromJob(AtlasWorkflowJobState job, WorkflowParamRenderer renderer)
    {
        if (job == null || renderer == null)
            return;

        // --- Header: workflow name + start time ---
        if (workflowNameLabel != null)
        {
            var localStart = job.CreatedAtUtc.ToLocalTime();
            workflowNameLabel.text = $"{job.WorkflowName ?? "Unnamed"} | {localStart:HH:mm:ss}";
        }

        // --- Status text + color (you can tweak this to match your taste) ---
        if (statusLabel != null)
        {
            string duration = FormatJobDurationForHeader(job);
            string statusText = job.Status.ToString();
            string rightText = string.IsNullOrEmpty(duration)
                ? statusText
                : $"{statusText} ({duration})";

            statusLabel.text = rightText;
            statusLabel.style.color = GetStatusColor(job.Status);
        }

        // We don't use a progress bar in history; hide it if you have one.
        if (progressBar != null)
            progressBar.style.display = DisplayStyle.None;

        // Hide the details foldout in Job History
        if (detailsFoldout != null)
            detailsFoldout.style.display = DisplayStyle.None;

        // --- Inputs from snapshot (read-only) ---
        if (inputsContainer != null)
        {
            inputsContainer.Clear();

            if (job.InputsSnapshot != null)
            {
                foreach (var input in job.InputsSnapshot)
                {
                    inputsContainer.Add(renderer.RenderInput(input, isEditable: false));
                }
            }
        }

        // --- Outputs from snapshot (read-only) ---
        if (outputsContainer != null)
        {
            outputsContainer.Clear();

            if (job.OutputsSnapshot != null)
            {
                foreach (var output in job.OutputsSnapshot)
                {
                    // true here if you want “Import/View” buttons to be enabled
                    outputsContainer.Add(renderer.RenderOutput(output, true));
                }
            }
        }
    }
    /// <summary>
    /// Same idea as your current FormatJobDuration in JobHistoryView.
    /// Copy that logic here, or simplify if you don’t care about duration.
    /// </summary>
    private string FormatJobDurationForHeader(AtlasWorkflowJobState job)
    {
        // Example: use CreatedAtUtc / CompletedAtUtc like you already do
        if (job.CompletedAtUtc == default)
            return string.Empty;

        var span = job.CompletedAtUtc - job.CreatedAtUtc;
        if (span.GetValueOrDefault().TotalHours >= 1.0)
            return string.Format("{0:00}:{1:00}:{2:00}",
                (int)span.GetValueOrDefault().TotalHours, span.GetValueOrDefault().Minutes, span.GetValueOrDefault().Seconds);

        return string.Format("{0:00}:{1:00}", span.GetValueOrDefault().Minutes, span.GetValueOrDefault().Seconds);
    }

    private Color GetStatusColor(JobStatus status)
    {
        switch (status)
        {
            case JobStatus.Running: return Color.yellow;
            case JobStatus.Succeeded: return Color.green;
            case JobStatus.Failed: return Color.red;
            default: return Color.gray;
        }
    }
}