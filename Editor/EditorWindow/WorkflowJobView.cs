// In Packages/com.atlas.workflow/Editor/EditorWindow/WorkflowJobView.cs

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Our new class inherits from VisualElement, making it a UI component itself.
public class WorkflowJobView : VisualElement
{
    // --- Internal UI References ---
    private Label workflowNameLabel;
    private Label workflowSubtitle;
    private VisualElement statusDot;
    private Label statusLabel;
    private ProgressBar progressBar;
    private VisualElement inputsContainer;
    private VisualElement outputsContainer;
    private VisualElement jobErrorRow;
    private Label jobErrorLabel;
    private VisualElement jobRetryRow;
    private Button jobRetryButton;
    private Action _retryClickAction;
    private VisualElement latestJobActions;
    private Button viewLatestJobButton;
    private Button openLatestOutputsButton;
    private Action _viewLatestJobClickAction;
    private Action _openLatestOutputsClickAction;

    private const int JobErrorPreviewMaxChars = 200;

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
        workflowSubtitle = this.Q<Label>("workflow-subtitle");
        statusDot = this.Q<VisualElement>("workflow-status-dot");
        statusLabel = this.Q<Label>("status-label");
        progressBar = this.Q<ProgressBar>("progress-bar");
        inputsContainer = this.Q<VisualElement>("inputs-container");
        outputsContainer = this.Q<VisualElement>("outputs-container");
        jobErrorRow = this.Q<VisualElement>("job-error-row");
        jobErrorLabel = this.Q<Label>("job-error-label");
        jobRetryRow = this.Q<VisualElement>("job-retry-row");
        jobRetryButton = this.Q<Button>("job-retry-button");
        latestJobActions = this.Q<VisualElement>("latest-job-actions");
        viewLatestJobButton = this.Q<Button>("view-latest-job-button");
        openLatestOutputsButton = this.Q<Button>("open-latest-outputs-button");
    }

    // Public method to update this component's UI from the state object.
    public void Populate(AtlasWorkflowState state, WorkflowUIBuilder uiBuilder)
    {
        if (state == null || uiBuilder == null) return;

        bool isLoaded = !string.IsNullOrEmpty(state.ActiveName);

        // Populate the header
        workflowNameLabel.text = isLoaded ? state.ActiveName : "No Workflow Loaded";
        
        // Build subtitle with version and domain
        if (workflowSubtitle != null)
        {
            if (isLoaded)
            {
                string domain = ExtractDomain(state.BaseUrl);
                string version = string.IsNullOrEmpty(state.Version) ? "" : $"v{state.Version}";
                
                if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(domain))
                    workflowSubtitle.text = $"{version} \u2022 {domain}";
                else if (!string.IsNullOrEmpty(domain))
                    workflowSubtitle.text = domain;
                else if (!string.IsNullOrEmpty(version))
                    workflowSubtitle.text = version;
                else
                    workflowSubtitle.text = "";
                    
                workflowSubtitle.tooltip = $"API ID: {state.ActiveApiId}\nBase URL: {state.BaseUrl}";
            }
            else
            {
                workflowSubtitle.text = "";
                workflowSubtitle.tooltip = "";
            }
        }
        
        // Set status dot to green (ready) for live workflow
        if (statusDot != null)
        {
            statusDot.style.backgroundColor = new StyleColor(new Color(0.4f, 0.8f, 0.4f)); // Green
        }

        // Use the UIBuilder to populate the dynamic lists
        uiBuilder.PopulateInputs(inputsContainer);
        uiBuilder.PopulateOutputs(outputsContainer);

        if (jobErrorRow != null)
            jobErrorRow.style.display = DisplayStyle.None;
        if (jobErrorLabel != null)
        {
            jobErrorLabel.text = "";
            jobErrorLabel.tooltip = "";
        }

        // Single-workflow panel (Populate) — never show job-history-only retry UI.
        if (jobRetryRow != null)
            jobRetryRow.style.display = DisplayStyle.None;
        if (jobRetryButton != null && _retryClickAction != null)
        {
            jobRetryButton.clicked -= _retryClickAction;
            _retryClickAction = null;
        }
        ClearLatestJobActions();
    }

    public void ConfigureLatestJobActions(
        AtlasWorkflowJobState job,
        Action onViewJob,
        Action onOpenOutputsFolder,
        bool hasOutputsFolder)
    {
        ClearLatestJobActions();

        if (job == null || onViewJob == null || latestJobActions == null || viewLatestJobButton == null)
            return;

        latestJobActions.style.display = DisplayStyle.Flex;
        viewLatestJobButton.style.display = DisplayStyle.Flex;
        viewLatestJobButton.text = job.Status == JobStatus.Running ? "View Job" : "View Results";
        viewLatestJobButton.tooltip = "Open Job History focused on this run.";
        _viewLatestJobClickAction = onViewJob;
        viewLatestJobButton.clicked += _viewLatestJobClickAction;

        bool canOpenOutputs = job.Status == JobStatus.Succeeded &&
                              hasOutputsFolder &&
                              onOpenOutputsFolder != null &&
                              openLatestOutputsButton != null;

        if (openLatestOutputsButton != null)
        {
            openLatestOutputsButton.style.display = canOpenOutputs ? DisplayStyle.Flex : DisplayStyle.None;
            if (canOpenOutputs)
            {
                openLatestOutputsButton.tooltip = "Reveal this job's generated output files.";
                _openLatestOutputsClickAction = onOpenOutputsFolder;
                openLatestOutputsButton.clicked += _openLatestOutputsClickAction;
            }
        }
    }

    private void ClearLatestJobActions()
    {
        if (viewLatestJobButton != null && _viewLatestJobClickAction != null)
        {
            viewLatestJobButton.clicked -= _viewLatestJobClickAction;
            _viewLatestJobClickAction = null;
        }

        if (openLatestOutputsButton != null && _openLatestOutputsClickAction != null)
        {
            openLatestOutputsButton.clicked -= _openLatestOutputsClickAction;
            _openLatestOutputsClickAction = null;
        }

        if (latestJobActions != null)
            latestJobActions.style.display = DisplayStyle.None;
        if (viewLatestJobButton != null)
            viewLatestJobButton.style.display = DisplayStyle.None;
        if (openLatestOutputsButton != null)
            openLatestOutputsButton.style.display = DisplayStyle.None;
    }
    
    /// <summary>
    /// Extracts just the domain from a URL for display.
    /// </summary>
    private string ExtractDomain(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            var uri = new System.Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }

    public void PopulateFromJob(AtlasWorkflowJobState job, WorkflowParamRenderer renderer, Action onRetryRequested = null)
    {
        if (job == null || renderer == null)
            return;

        if (jobRetryButton != null && _retryClickAction != null)
        {
            jobRetryButton.clicked -= _retryClickAction;
            _retryClickAction = null;
        }
        ClearLatestJobActions();

        // Set job context for import folder detection
        renderer.SetJobContext(job);

        // --- Header: workflow name ---
        if (workflowNameLabel != null)
        {
            workflowNameLabel.text = job.WorkflowName ?? "Unnamed";
        }
        
        // --- Subtitle: timestamp and duration ---
        if (workflowSubtitle != null)
        {
            var localStart = job.CreatedAtUtc.ToLocalTime();
            string duration = FormatJobDurationForHeader(job);

            string baseLine = !string.IsNullOrEmpty(duration)
                ? $"{localStart:MMM d, HH:mm} • {duration}"
                : $"{localStart:MMM d, HH:mm}";

            if (!string.IsNullOrEmpty(job.RetryOfJobId))
            {
                string shortPrev = job.RetryOfJobId.Length > 8
                    ? job.RetryOfJobId.Substring(0, 8) + "…"
                    : job.RetryOfJobId;
                baseLine += $" • Retry of {shortPrev}";
            }

            workflowSubtitle.text = baseLine;
        }

        // --- Error (API / workflow message when present) ---
        string err = job.ErrorMessage != null ? job.ErrorMessage.Trim() : "";
        bool hasErrorText = err.Length > 0;
        if (jobErrorRow != null)
            jobErrorRow.style.display = hasErrorText ? DisplayStyle.Flex : DisplayStyle.None;
        if (jobErrorLabel != null)
        {
            if (hasErrorText)
            {
                jobErrorLabel.tooltip = err;
                jobErrorLabel.text = err.Length > JobErrorPreviewMaxChars
                    ? err.Substring(0, JobErrorPreviewMaxChars).TrimEnd() + "…"
                    : err;
            }
            else
            {
                jobErrorLabel.text = "";
                jobErrorLabel.tooltip = "";
            }
        }

        // --- Retry (failed / cancelled only) ---
        bool canRetry = onRetryRequested != null &&
                        (job.Status == JobStatus.Failed || job.Status == JobStatus.Cancelled);
        if (jobRetryRow != null)
            jobRetryRow.style.display = canRetry ? DisplayStyle.Flex : DisplayStyle.None;
        if (jobRetryButton != null && canRetry)
        {
            _retryClickAction = () => onRetryRequested();
            jobRetryButton.clicked += _retryClickAction;
        }
        
        // --- Status dot color ---
        if (statusDot != null)
        {
            statusDot.style.backgroundColor = new StyleColor(GetStatusColor(job.Status));
        }

        // --- Status text + color ---
        if (statusLabel != null)
        {
            statusLabel.text = job.Status.ToString();
            statusLabel.style.color = GetStatusColor(job.Status);
        }

        // We don't use a progress bar in history; hide it if you have one.
        if (progressBar != null)
            progressBar.style.display = DisplayStyle.None;

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
                    // true here if you want ?Import/View? buttons to be enabled
                    outputsContainer.Add(renderer.RenderOutput(output, true));
                }
            }
        }
    }
    /// <summary>
    /// Same idea as your current FormatJobDuration in JobHistoryView.
    /// Copy that logic here, or simplify if you don?t care about duration.
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
            case JobStatus.Cancelled: return new Color(0.75f, 0.55f, 0.35f);
            default: return Color.gray;
        }
    }
}