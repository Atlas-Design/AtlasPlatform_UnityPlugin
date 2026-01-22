// In Packages/com.atlas.workflow/Editor/EditorWindow/WorkflowJobView.cs

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
                    workflowSubtitle.text = $"{version} ? {domain}";
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

    public void PopulateFromJob(AtlasWorkflowJobState job, WorkflowParamRenderer renderer)
    {
        if (job == null || renderer == null)
            return;

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
            
            if (!string.IsNullOrEmpty(duration))
                workflowSubtitle.text = $"{localStart:MMM d, HH:mm} • {duration}";
            else
                workflowSubtitle.text = $"{localStart:MMM d, HH:mm}";
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
            default: return Color.gray;
        }
    }
}