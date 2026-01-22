using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;

public class JobHistoryView
{
    // The original container passed from AtlasWorkflowEditor (jobsList ScrollView)
    private readonly VisualElement rootContainer;
    private readonly System.Action<AtlasWorkflowJobState> onJobSelected;
    private readonly WorkflowParamRenderer renderer;

    // Split layout
    private VisualElement splitRoot;
    private VisualElement leftPane;
    private VisualElement rightPane;
    private ScrollView jobListContainer;
    private WorkflowJobView jobDetailsView;

    // Selection state
    private AtlasWorkflowJobState selectedJob;
    private VisualElement lastSelectedRow;

    public JobHistoryView(
        VisualElement container,
        WorkflowParamRenderer renderer,
        System.Action<AtlasWorkflowJobState> onJobSelected)
    {
        this.rootContainer = container;
        this.renderer = renderer;
        this.onJobSelected = onJobSelected;

        BuildLayout();
    }

    /// <summary>
    /// Builds the split layout: left = job list, right = job details.
    /// </summary>
    private void BuildLayout()
    {
        if (rootContainer == null)
            return;

        rootContainer.Clear();

        // Load our split-view UXML template
        var splitTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Packages/com.atlas.workflow/Editor/EditorWindow/Elements/_JobSplitView.uxml"
        );

        if (splitTree != null)
        {
            splitRoot = splitTree.Instantiate();
            splitRoot.style.flexGrow = 1f;
            rootContainer.Add(splitRoot);

            leftPane = splitRoot.Q<VisualElement>("LeftPane");
            rightPane = splitRoot.Q<VisualElement>("RightPane");

            if (leftPane == null)
                leftPane = splitRoot;

            if (rightPane == null)
            {
                rightPane = new VisualElement();
                rightPane.style.flexGrow = 1f;
                splitRoot.Add(rightPane);
            }

            jobListContainer = new ScrollView();
            jobListContainer.name = "jobs-list-container";
            jobListContainer.style.flexGrow = 1f;
            jobListContainer.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            jobListContainer.verticalScrollerVisibility = ScrollerVisibility.Auto;
            leftPane.Add(jobListContainer);

            jobDetailsView = new WorkflowJobView();
            rightPane.Add(jobDetailsView);
        }
        else
        {
            // Fallback: if the template is missing, behave like before (single pane)
            splitRoot = null;
            leftPane = rootContainer;
            rightPane = null;

            jobListContainer = new ScrollView();
            jobListContainer.style.flexGrow = 1f;
            leftPane.Add(jobListContainer);
        }
    }

    /// <summary>
    /// Rebuilds the job list on the left.
    /// </summary>
    public void Refresh(List<AtlasWorkflowJobState> jobs)
    {
        if (jobListContainer == null)
            return;

        jobListContainer.Clear();

        foreach (var job in jobs)
        {
            var row = CreateJobRow(job);
            jobListContainer.Add(row);
        }

        // If we already had a selected job, re-show its details if it still exists
        if (selectedJob != null && jobs.Contains(selectedJob))
        {
            ShowJobDetails(selectedJob);
        }
        else
        {
            // No valid selection -> show neutral message on the right
            selectedJob = null;
            ShowJobDetails(null);

        }
    }

    /// <summary>
    /// Creates a single, compact row for the job list (left pane).
    /// </summary>
    private VisualElement CreateJobRow(AtlasWorkflowJobState job)
    {
        var row = new VisualElement();
        row.AddToClassList("job-history-row");
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingLeft = 4;
        row.style.paddingRight = 4;
        row.style.paddingTop = 2;
        row.style.paddingBottom = 2;
        row.style.minHeight = 18;   // keeps rows consistent

        // LEFT: workflow name only (no start time text)
        var nameLabel = new Label(job.WorkflowName ?? "Unnamed");
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.whiteSpace = WhiteSpace.NoWrap;

        // Tooltip with start time
        var startLocal = job.CreatedAtUtc.ToLocalTime().ToString("HH:mm:ss");
        nameLabel.tooltip = $"Start Time: {startLocal}";

        // Spacer to push status to the right
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;

        // RIGHT: colored status indicator (+ tooltip with status + duration)
        var statusIndicator = new VisualElement();
        statusIndicator.AddToClassList("job-status-indicator");

        string duration = FormatJobDuration(job);
        string statusTooltip = job.Status.ToString();
        if (!string.IsNullOrEmpty(duration))
            statusTooltip += $" ({duration})";

        statusIndicator.tooltip = statusTooltip;
        SetStatusIndicatorColor(statusIndicator, job.Status);

        // Build row
        row.Add(nameLabel);
        row.Add(spacer);
        row.Add(statusIndicator);

        // Selection behaviour unchanged
        row.RegisterCallback<ClickEvent>(_ =>
        {
            selectedJob = job;
            onJobSelected?.Invoke(job);
            ShowJobDetails(job);
            HighlightSelectedRow(row);
        });

        row.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction(
                "Open Folder",
                _ => OpenJobFolder(job),
                DropdownMenuAction.AlwaysEnabled);

            evt.menu.AppendSeparator();

            evt.menu.AppendAction(
                "Delete Job",
                _ => DeleteJobWithConfirm(job),
                DropdownMenuAction.AlwaysEnabled);
        }));

        return row;
    }

    private void SetStatusIndicatorColor(VisualElement element, JobStatus status)
    {
        Color color;
        switch (status)
        {
            case JobStatus.Running:
                color = Color.yellow;
                break;
            case JobStatus.Succeeded:
                color = Color.green;
                break;
            case JobStatus.Failed:
                color = Color.red;
                break;
            default:
                color = Color.gray;
                break;
        }

        element.style.backgroundColor = new StyleColor(color);
    }

    private void HighlightSelectedRow(VisualElement row)
    {
        if (lastSelectedRow != null)
            lastSelectedRow.RemoveFromClassList("job-history-row--selected");

        lastSelectedRow = row;
        if (lastSelectedRow != null)
            lastSelectedRow.AddToClassList("job-history-row--selected");
    }

    /// <summary>
    /// Populates the right-hand details pane for the selected job.
    /// </summary>
    private void ShowJobDetails(AtlasWorkflowJobState job)
    {
        if (rightPane == null)
            return;

        rightPane.Clear();

        if (job == null)
        {
            rightPane.Add(new Label("No job selected."));
            return;
        }

        if (jobDetailsView == null)
        {
            jobDetailsView = new WorkflowJobView();
        }

        rightPane.Add(jobDetailsView);
        jobDetailsView.PopulateFromJob(job, renderer);
    }
    /// <summary>
    /// Formats job duration from CreatedAtUtc to CompletedAtUtc (or now).
    /// </summary>
    private string FormatJobDuration(AtlasWorkflowJobState job)
    {
        var start = job.CreatedAtUtc;
        var end = job.CompletedAtUtc ?? System.DateTime.UtcNow;

        if (end < start)
            end = start;

        var span = end - start;

        if (span.TotalHours >= 1.0)
        {
            return string.Format("{0:00}:{1:00}:{2:00}",
                (int)span.TotalHours, span.Minutes, span.Seconds);
        }

        return string.Format("{0:00}:{1:00}", span.Minutes, span.Seconds);
    }

    private void SetStatusColor(Label label, JobStatus status)
    {
        switch (status)
        {
            case JobStatus.Running:
                label.style.color = new StyleColor(Color.yellow);
                break;
            case JobStatus.Succeeded:
                label.style.color = new StyleColor(Color.green);
                break;
            case JobStatus.Failed:
                label.style.color = new StyleColor(Color.red);
                break;
            default:
                label.style.color = new StyleColor(Color.gray);
                break;
        }
    }

    private void OpenJobFolder(AtlasWorkflowJobState job)
    {
        if (job == null)
            return;

        var path = job.JobFolderPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            //Debug.LogWarning($"[JobHistoryView] Job folder not found for {job.JobId}: {path}");
            return;
        }

        EditorUtility.RevealInFinder(path);
    }

    private void DeleteJobWithConfirm(AtlasWorkflowJobState job)
    {
        if (job == null)
            return;

        string message = $"Delete job '{job.WorkflowName}' and its folder?\n\n" +
                         job.JobFolderPath;

        bool confirm = EditorUtility.DisplayDialog(
            "Delete Job",
            message,
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        bool deleted = WorkflowManager.DeleteJob(job);
        if (!deleted)
        {
            EditorUtility.DisplayDialog(
                "Delete Job",
                "Failed to delete this job. Check the Console for details.",
                "OK");
            return;
        }

        // Clear selection if we just deleted the selected job
        if (selectedJob == job)
        {
            selectedJob = null;
            if (rightPane != null)
            {
                rightPane.Clear();
                rightPane.Add(new Label("No job selected."));
            }
        }

        // Refresh list from the current in-memory jobs
        Refresh(WorkflowManager.Jobs);
    }

}
