using System;
using System.Collections.Generic;
using System.Linq;
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

    // Filter UI elements
    private DropdownField statusFilter;
    private DropdownField typeFilter;
    private DropdownField dateFilter;

    // Filter state
    private string currentStatusFilter = "All";
    private string currentTypeFilter = "All";
    private string currentDateFilter = "All Time";

    // Date group collapse state (persists across refreshes)
    private Dictionary<string, bool> dateGroupCollapsed = new Dictionary<string, bool>();

    // All jobs reference for filtering
    private List<AtlasWorkflowJobState> allJobs = new List<AtlasWorkflowJobState>();

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

            // Add filter toolbar to left pane
            BuildFilterToolbar(leftPane);

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

            // Add filter toolbar
            BuildFilterToolbar(leftPane);

            jobListContainer = new ScrollView();
            jobListContainer.style.flexGrow = 1f;
            leftPane.Add(jobListContainer);
        }
    }

    /// <summary>
    /// Builds the filter toolbar with status, type, and date filter dropdowns.
    /// </summary>
    private void BuildFilterToolbar(VisualElement parent)
    {
        var filterTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Packages/com.atlas.workflow/Editor/EditorWindow/Elements/_JobHistoryFilters.uxml"
        );

        VisualElement toolbar;
        if (filterTree != null)
        {
            toolbar = filterTree.Instantiate();
        }
        else
        {
            // Fallback: build dynamically with explicit rows
            toolbar = new VisualElement();
            toolbar.name = "filter-toolbar";
            toolbar.AddToClassList("filter-toolbar");

            var grid = new VisualElement();
            grid.AddToClassList("filter-grid");

            // Row 1: Labels
            var labelsRow = new VisualElement();
            labelsRow.AddToClassList("filter-labels-row");

            var statusLabel = new Label("Status");
            statusLabel.AddToClassList("filter-label");
            labelsRow.Add(statusLabel);

            var typeLabel = new Label("Type");
            typeLabel.AddToClassList("filter-label");
            labelsRow.Add(typeLabel);

            var dateLabel = new Label("Date");
            dateLabel.AddToClassList("filter-label");
            labelsRow.Add(dateLabel);

            grid.Add(labelsRow);

            // Row 2: Dropdowns
            var dropdownsRow = new VisualElement();
            dropdownsRow.AddToClassList("filter-dropdowns-row");

            statusFilter = new DropdownField();
            statusFilter.name = "status-filter";
            statusFilter.AddToClassList("filter-chip");
            dropdownsRow.Add(statusFilter);

            typeFilter = new DropdownField();
            typeFilter.name = "type-filter";
            typeFilter.AddToClassList("filter-chip");
            dropdownsRow.Add(typeFilter);

            dateFilter = new DropdownField();
            dateFilter.name = "date-filter";
            dateFilter.AddToClassList("filter-chip");
            dropdownsRow.Add(dateFilter);

            grid.Add(dropdownsRow);
            toolbar.Add(grid);
        }

        parent.Add(toolbar);

        // Query the filter elements
        statusFilter = toolbar.Q<DropdownField>("status-filter");
        typeFilter = toolbar.Q<DropdownField>("type-filter");
        dateFilter = toolbar.Q<DropdownField>("date-filter");

        // Setup status filter
        if (statusFilter != null)
        {
            statusFilter.choices = new List<string> { "All", "Success", "Failed", "Running" };
            statusFilter.value = currentStatusFilter;
            statusFilter.RegisterValueChangedCallback(evt =>
            {
                currentStatusFilter = evt.newValue;
                RefreshFilteredList();
            });
        }

        // Setup date filter
        if (dateFilter != null)
        {
            dateFilter.choices = new List<string> { "All Time", "Today", "Last 7 Days", "Last 30 Days" };
            dateFilter.value = currentDateFilter;
            dateFilter.RegisterValueChangedCallback(evt =>
            {
                currentDateFilter = evt.newValue;
                RefreshFilteredList();
            });
        }

        // Setup type filter (will be populated with actual job types)
        if (typeFilter != null)
        {
            typeFilter.choices = new List<string> { "All" };
            typeFilter.value = currentTypeFilter;
            typeFilter.RegisterValueChangedCallback(evt =>
            {
                currentTypeFilter = evt.newValue;
                RefreshFilteredList();
            });
        }
    }

    /// <summary>
    /// Clears all job history. Can be called externally (e.g., from menu).
    /// </summary>
    public void ClearHistory()
    {
        bool confirm = EditorUtility.DisplayDialog(
            "Clear Job History",
            "Delete all job history? This cannot be undone.",
            "Clear All",
            "Cancel");

        if (!confirm)
            return;

        // Delete all jobs
        var jobsToDelete = new List<AtlasWorkflowJobState>(allJobs);
        foreach (var job in jobsToDelete)
        {
            WorkflowManager.DeleteJob(job);
        }

        selectedJob = null;
        ShowJobDetails(null);
        Refresh(WorkflowManager.Jobs);
    }

    /// <summary>
    /// Rebuilds the job list on the left.
    /// </summary>
    public void Refresh(List<AtlasWorkflowJobState> jobs)
    {
        if (jobListContainer == null)
            return;

        allJobs = jobs ?? new List<AtlasWorkflowJobState>();

        // Update type filter with available job types
        UpdateTypeFilterChoices();

        // Apply filters and refresh the display
        RefreshFilteredList();

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
    /// Updates the type filter dropdown with unique job types from the job list.
    /// </summary>
    private void UpdateTypeFilterChoices()
    {
        if (typeFilter == null)
            return;

        var types = allJobs
            .Where(j => !string.IsNullOrEmpty(j.WorkflowName))
            .Select(j => j.WorkflowName)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        types.Insert(0, "All");
        typeFilter.choices = types;

        // Reset to "All" if current selection no longer exists
        if (!types.Contains(currentTypeFilter))
        {
            currentTypeFilter = "All";
            typeFilter.value = currentTypeFilter;
        }
    }

    /// <summary>
    /// Applies current filters and rebuilds the job list with date grouping.
    /// </summary>
    private void RefreshFilteredList()
    {
        if (jobListContainer == null)
            return;

        jobListContainer.Clear();

        // Apply filters
        var filteredJobs = ApplyFilters(allJobs);

        // Group by date
        var groups = GroupJobsByDate(filteredJobs);

        // Build UI for each group
        foreach (var group in groups)
        {
            var groupElement = CreateDateGroup(group.Key, group.Value);
            jobListContainer.Add(groupElement);
        }

        // Show message if no jobs match filters
        if (!filteredJobs.Any())
        {
            var emptyLabel = new Label("No jobs match the current filters.");
            emptyLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            emptyLabel.style.marginTop = 10;
            emptyLabel.style.marginLeft = 6;
            emptyLabel.style.fontSize = 11;
            jobListContainer.Add(emptyLabel);
        }
    }

    /// <summary>
    /// Applies the current filter settings to the job list.
    /// </summary>
    private List<AtlasWorkflowJobState> ApplyFilters(List<AtlasWorkflowJobState> jobs)
    {
        var filtered = jobs.AsEnumerable();

        // Status filter
        if (currentStatusFilter != "All")
        {
            filtered = filtered.Where(j =>
            {
                switch (currentStatusFilter)
                {
                    case "Success": return j.Status == JobStatus.Succeeded;
                    case "Failed": return j.Status == JobStatus.Failed;
                    case "Running": return j.Status == JobStatus.Running;
                    default: return true;
                }
            });
        }

        // Type filter
        if (currentTypeFilter != "All")
        {
            filtered = filtered.Where(j => j.WorkflowName == currentTypeFilter);
        }

        // Date filter
        if (currentDateFilter != "All Time")
        {
            var now = DateTime.UtcNow;
            DateTime cutoff;
            switch (currentDateFilter)
            {
                case "Today":
                    cutoff = now.Date;
                    break;
                case "Last 7 Days":
                    cutoff = now.AddDays(-7);
                    break;
                case "Last 30 Days":
                    cutoff = now.AddDays(-30);
                    break;
                default:
                    cutoff = DateTime.MinValue;
                    break;
            }
            filtered = filtered.Where(j => j.CreatedAtUtc >= cutoff);
        }

        return filtered.ToList();
    }

    /// <summary>
    /// Groups jobs by date category (Today, Yesterday, Last Week, Older).
    /// </summary>
    private List<KeyValuePair<string, List<AtlasWorkflowJobState>>> GroupJobsByDate(List<AtlasWorkflowJobState> jobs)
    {
        var now = DateTime.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var lastWeekStart = today.AddDays(-7);

        var groups = new Dictionary<string, List<AtlasWorkflowJobState>>
        {
            { "Today", new List<AtlasWorkflowJobState>() },
            { "Yesterday", new List<AtlasWorkflowJobState>() },
            { "Last 7 Days", new List<AtlasWorkflowJobState>() },
            { "Older", new List<AtlasWorkflowJobState>() }
        };

        foreach (var job in jobs.OrderByDescending(j => j.CreatedAtUtc))
        {
            var jobDate = job.CreatedAtUtc.ToLocalTime().Date;

            if (jobDate == today)
                groups["Today"].Add(job);
            else if (jobDate == yesterday)
                groups["Yesterday"].Add(job);
            else if (jobDate > lastWeekStart)
                groups["Last 7 Days"].Add(job);
            else
                groups["Older"].Add(job);
        }

        // Return only non-empty groups, in order
        var result = new List<KeyValuePair<string, List<AtlasWorkflowJobState>>>();
        foreach (var key in new[] { "Today", "Yesterday", "Last 7 Days", "Older" })
        {
            if (groups[key].Count > 0)
                result.Add(new KeyValuePair<string, List<AtlasWorkflowJobState>>(key, groups[key]));
        }

        return result;
    }

    /// <summary>
    /// Creates a collapsible date group UI element.
    /// </summary>
    private VisualElement CreateDateGroup(string groupName, List<AtlasWorkflowJobState> jobs)
    {
        var group = new VisualElement();
        group.AddToClassList("date-group");

        // Initialize collapse state if not set (default: Today expanded, others collapsed)
        if (!dateGroupCollapsed.ContainsKey(groupName))
        {
            dateGroupCollapsed[groupName] = groupName != "Today";
        }

        bool isCollapsed = dateGroupCollapsed[groupName];

        // Header
        var header = new VisualElement();
        header.AddToClassList("date-group-header");
        if (isCollapsed)
            header.AddToClassList("date-group-header--collapsed");

        var toggle = new Label(isCollapsed ? "▶" : "▼");
        toggle.AddToClassList("date-group-toggle");

        var title = new Label(groupName);
        title.AddToClassList("date-group-title");

        var count = new Label($"({jobs.Count})");
        count.AddToClassList("date-group-count");

        header.Add(toggle);
        header.Add(title);
        header.Add(count);

        // Content
        var content = new VisualElement();
        content.AddToClassList("date-group-content");
        if (isCollapsed)
            content.AddToClassList("date-group-content--hidden");

        foreach (var job in jobs)
        {
            var row = CreateJobRow(job);
            content.Add(row);
        }

        // Toggle collapse on header click
        header.RegisterCallback<ClickEvent>(evt =>
        {
            dateGroupCollapsed[groupName] = !dateGroupCollapsed[groupName];
            bool collapsed = dateGroupCollapsed[groupName];

            toggle.text = collapsed ? "▶" : "▼";

            if (collapsed)
            {
                header.AddToClassList("date-group-header--collapsed");
                content.AddToClassList("date-group-content--hidden");
            }
            else
            {
                header.RemoveFromClassList("date-group-header--collapsed");
                content.RemoveFromClassList("date-group-content--hidden");
            }

            evt.StopPropagation();
        });

        group.Add(header);
        group.Add(content);

        return group;
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

        // LEFT: workflow name
        var nameLabel = new Label(job.WorkflowName ?? "Unnamed");
        nameLabel.AddToClassList("job-row-name");
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.whiteSpace = WhiteSpace.NoWrap;

        // Relative time label (e.g., "2m ago")
        var timeLabel = new Label(FormatRelativeTime(job.CreatedAtUtc));
        timeLabel.AddToClassList("job-row-time");

        // Tooltip with full details
        var startLocal = job.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        string duration = FormatJobDuration(job);
        row.tooltip = $"{job.WorkflowName}\nStarted: {startLocal}\nDuration: {duration}\nStatus: {job.Status}";

        // RIGHT: colored status indicator
        var statusIndicator = new VisualElement();
        statusIndicator.AddToClassList("job-status-indicator");
        SetStatusIndicatorColor(statusIndicator, job.Status);

        // Build row
        row.Add(nameLabel);
        row.Add(timeLabel);
        row.Add(statusIndicator);

        // Selection behaviour unchanged
        row.RegisterCallback<ClickEvent>(evt =>
        {
            selectedJob = job;
            onJobSelected?.Invoke(job);
            ShowJobDetails(job);
            HighlightSelectedRow(row);
            evt.StopPropagation();
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

    /// <summary>
    /// Formats a DateTime as a relative time string (e.g., "2m ago", "3h ago", "Yesterday").
    /// </summary>
    private string FormatRelativeTime(DateTime utcTime)
    {
        var localTime = utcTime.ToLocalTime();
        var now = DateTime.Now;
        var span = now - localTime;

        if (span.TotalSeconds < 60)
            return "just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 2)
            return "yesterday";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}d ago";

        return localTime.ToString("MMM d");
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
