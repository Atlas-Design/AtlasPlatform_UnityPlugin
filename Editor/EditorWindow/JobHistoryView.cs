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

    private Button viewModeJobsBtn;
    private Button viewModeBatchesBtn;
    private bool viewModeIsBatches;

    /// <summary>When in Batches mode: null = batch catalog, non-null = drilled into that batch's jobs.</summary>
    private string batchesDrilldownBatchId;

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

            var viewPanel = new VisualElement();
            viewPanel.name = "view-mode-panel";
            viewPanel.AddToClassList("view-mode-panel");
            var viewRow = new VisualElement();
            viewRow.AddToClassList("view-mode-options-row");
            viewModeJobsBtn = new Button { name = "view-mode-jobs", text = "Jobs" };
            viewModeJobsBtn.AddToClassList("view-mode-option");
            viewModeJobsBtn.AddToClassList("view-mode-option--active");
            viewModeBatchesBtn = new Button { name = "view-mode-batches", text = "Batches" };
            viewModeBatchesBtn.AddToClassList("view-mode-option");
            viewModeBatchesBtn.AddToClassList("view-mode-option--last");
            viewRow.Add(viewModeJobsBtn);
            viewRow.Add(viewModeBatchesBtn);
            viewPanel.Add(viewRow);
            toolbar.Add(viewPanel);

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

        viewModeJobsBtn = toolbar.Q<Button>("view-mode-jobs");
        viewModeBatchesBtn = toolbar.Q<Button>("view-mode-batches");
        if (viewModeJobsBtn != null)
        {
            viewModeJobsBtn.clicked += OnViewModeJobsClicked;
            viewModeJobsBtn.tooltip = "List every job, including batch instances.";
        }

        if (viewModeBatchesBtn != null)
        {
            viewModeBatchesBtn.clicked += OnViewModeBatchesClicked;
            viewModeBatchesBtn.tooltip = "Browse batch runs, then open one to see its jobs.";
        }

        viewModeIsBatches = false;
        UpdateViewModeButtonStyles();

        // Setup status filter
        if (statusFilter != null)
        {
            statusFilter.choices = new List<string> { "All", "Success", "Failed", "Running", "Cancelled" };
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

        ApplyFilterToolbarState();
    }

    private void OnViewModeJobsClicked()
    {
        if (!viewModeIsBatches)
            return;

        viewModeIsBatches = false;
        batchesDrilldownBatchId = null;
        UpdateViewModeButtonStyles();
        ApplyFilterToolbarState();
        RefreshFilteredList();
        if (selectedJob != null)
            ShowJobDetails(selectedJob);
        else
            ShowJobDetails(null);
    }

    private void OnViewModeBatchesClicked()
    {
        if (viewModeIsBatches)
            return;

        viewModeIsBatches = true;
        batchesDrilldownBatchId = null;
        UpdateViewModeButtonStyles();
        selectedJob = null;
        lastSelectedRow = null;
        ApplyFilterToolbarState();
        ShowJobDetails(null);
        RefreshFilteredList();
    }

    private void UpdateViewModeButtonStyles()
    {
        if (viewModeJobsBtn != null)
            viewModeJobsBtn.EnableInClassList("view-mode-option--active", !viewModeIsBatches);
        if (viewModeBatchesBtn != null)
            viewModeBatchesBtn.EnableInClassList("view-mode-option--active", viewModeIsBatches);
    }

    /// <summary>
    /// Jobs: Status, Type, Date. Batches catalog: Status + Date (batch-level semantics); Type off.
    /// Batches drill-in: Status only; Type and Date off (single workflow, same run window).
    /// </summary>
    private void ApplyFilterToolbarState()
    {
        if (statusFilter == null && typeFilter == null && dateFilter == null)
            return;

        if (!viewModeIsBatches)
        {
            statusFilter?.SetEnabled(true);
            typeFilter?.SetEnabled(true);
            dateFilter?.SetEnabled(true);
            return;
        }

        if (string.IsNullOrEmpty(batchesDrilldownBatchId))
        {
            statusFilter?.SetEnabled(true);
            typeFilter?.SetEnabled(false);
            dateFilter?.SetEnabled(true);
        }
        else
        {
            statusFilter?.SetEnabled(true);
            typeFilter?.SetEnabled(false);
            dateFilter?.SetEnabled(false);
        }

        UpdateFilterControlTooltips();
    }

    private void UpdateFilterControlTooltips()
    {
        if (statusFilter != null)
        {
            if (!viewModeIsBatches)
                statusFilter.tooltip = "Filter jobs by status.";
            else if (string.IsNullOrEmpty(batchesDrilldownBatchId))
            {
                statusFilter.tooltip =
                    "Batches: Success = every job in the batch succeeded. Failed, Running, or Cancelled = at least one job has that status.";
            }
            else
                statusFilter.tooltip = "Filter jobs in this batch by status.";
        }

        if (typeFilter != null)
        {
            if (!viewModeIsBatches)
                typeFilter.tooltip = "Filter by workflow name.";
            else
                typeFilter.tooltip = "Not used in Batches view — each batch uses a single workflow.";
        }

        if (dateFilter != null)
        {
            if (!viewModeIsBatches)
                dateFilter.tooltip = "Filter by job start time.";
            else if (string.IsNullOrEmpty(batchesDrilldownBatchId))
            {
                dateFilter.tooltip =
                    "Show batches that have at least one job with a start time in this period.";
            }
            else
            {
                dateFilter.tooltip =
                    "Not used inside a batch — all jobs in a run share the same time window.";
            }
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
        viewModeIsBatches = false;
        batchesDrilldownBatchId = null;
        UpdateViewModeButtonStyles();
        ApplyFilterToolbarState();
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
    /// Opens the jobs list view and selects the requested job, clearing filters that might hide it.
    /// </summary>
    public bool SelectJobById(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
            return false;

        var job = allJobs.FirstOrDefault(j => j != null && j.JobId == jobId);
        if (job == null)
            return false;

        viewModeIsBatches = false;
        batchesDrilldownBatchId = null;
        currentStatusFilter = "All";
        currentTypeFilter = "All";
        currentDateFilter = "All Time";
        selectedJob = job;
        lastSelectedRow = null;

        statusFilter?.SetValueWithoutNotify(currentStatusFilter);
        typeFilter?.SetValueWithoutNotify(currentTypeFilter);
        dateFilter?.SetValueWithoutNotify(currentDateFilter);

        dateGroupCollapsed["Today"] = false;
        dateGroupCollapsed["Yesterday"] = false;
        dateGroupCollapsed["Last 7 Days"] = false;
        dateGroupCollapsed["Older"] = false;

        UpdateViewModeButtonStyles();
        ApplyFilterToolbarState();
        RefreshFilteredList();
        ShowJobDetails(job);
        onJobSelected?.Invoke(job);

        return true;
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

        if (viewModeIsBatches)
        {
            if (string.IsNullOrEmpty(batchesDrilldownBatchId))
                RefreshBatchCatalog();
            else
                RefreshBatchDrilldownList();
            return;
        }

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

        RestoreJobSelectionHighlight();
    }

    private void RefreshBatchCatalog()
    {
        var aggregates = BuildBatchAggregates();
        foreach (var batch in aggregates)
        {
            var row = CreateBatchCatalogRow(batch);
            jobListContainer.Add(row);
        }

        if (!aggregates.Any())
        {
            bool anyBatchJobsEver = allJobs.Any(j => !string.IsNullOrEmpty(j.BatchId));
            var emptyLabel = new Label(
                anyBatchJobsEver
                    ? "No batches match the current filters."
                    : "No batch runs yet. Batch jobs appear here after you run Atlas Batch.");
            emptyLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            emptyLabel.style.marginTop = 10;
            emptyLabel.style.marginLeft = 8;
            emptyLabel.style.marginRight = 8;
            emptyLabel.style.fontSize = 11;
            emptyLabel.style.whiteSpace = WhiteSpace.Normal;
            jobListContainer.Add(emptyLabel);
        }
    }

    private void RefreshBatchDrilldownList()
    {
        var backBar = new VisualElement();
        backBar.AddToClassList("batch-drill-back-bar");
        var backBtn = new Button(OnBatchDrillBack) { text = "← Batches" };
        backBtn.AddToClassList("batch-drill-back-btn");
        backBar.Add(backBtn);
        jobListContainer.Add(backBar);

        var inBatch = allJobs.Where(j => j.BatchId == batchesDrilldownBatchId).ToList();
        var filtered = ApplyFilters(inBatch, applyDateFilter: false, applyTypeFilter: false)
            .OrderBy(j => j.BatchIndex ?? int.MaxValue)
            .ThenBy(j => j.CreatedAtUtc)
            .ToList();

        foreach (var job in filtered)
            jobListContainer.Add(CreateJobRow(job));

        if (!filtered.Any())
        {
            var emptyLabel = new Label("No jobs in this batch match the current filters.");
            emptyLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            emptyLabel.style.marginTop = 8;
            emptyLabel.style.marginLeft = 8;
            emptyLabel.style.fontSize = 11;
            jobListContainer.Add(emptyLabel);
        }

        RestoreJobSelectionHighlight();
    }

    private List<BatchAggregateInfo> BuildBatchAggregates()
    {
        return allJobs
            .Where(j => !string.IsNullOrEmpty(j.BatchId))
            .GroupBy(j => j.BatchId)
            .Select(g =>
            {
                var jobs = g.OrderBy(j => j.BatchIndex ?? int.MaxValue).ThenBy(j => j.CreatedAtUtc).ToList();
                var first = jobs[0];
                var shortId = g.Key.Length <= 10 ? g.Key : g.Key.Substring(0, 8) + "…";
                var title = !string.IsNullOrEmpty(first.BatchName) ? first.BatchName : $"Batch {shortId}";
                return new BatchAggregateInfo
                {
                    BatchId = g.Key,
                    DisplayTitle = title,
                    WorkflowName = first.WorkflowName ?? "Workflow",
                    Jobs = jobs
                };
            })
            .Where(b => BatchMatchesDateFilter(b.Jobs))
            .Where(b => BatchMatchesStatusFilter(b.Jobs))
            .OrderByDescending(b => b.Jobs.Max(j => j.CreatedAtUtc))
            .ToList();
    }

    /// <summary>
    /// Catalog only: Success = every job succeeded; Failed/Running/Cancelled = at least one job matches.
    /// </summary>
    private bool BatchMatchesStatusFilter(List<AtlasWorkflowJobState> jobs)
    {
        if (jobs == null || jobs.Count == 0)
            return currentStatusFilter == "All";

        if (currentStatusFilter == "All")
            return true;

        switch (currentStatusFilter)
        {
            case "Success":
                return jobs.All(j => j.Status == JobStatus.Succeeded);
            case "Failed":
                return jobs.Any(j => j.Status == JobStatus.Failed);
            case "Running":
                return jobs.Any(j => j.Status == JobStatus.Running);
            case "Cancelled":
                return jobs.Any(j => j.Status == JobStatus.Cancelled);
            default:
                return true;
        }
    }

    private bool BatchMatchesDateFilter(List<AtlasWorkflowJobState> jobs)
    {
        var cutoff = GetDateCutoffUtc();
        if (!cutoff.HasValue)
            return true;
        return jobs.Any(j => j.CreatedAtUtc >= cutoff.Value);
    }

    private VisualElement CreateBatchCatalogRow(BatchAggregateInfo batch)
    {
        var row = new VisualElement();
        row.AddToClassList("batch-history-row");
        row.style.flexShrink = 0;

        var inner = new VisualElement();
        inner.AddToClassList("batch-history-row__row");

        var textCol = new VisualElement();
        textCol.style.flexGrow = 1;
        textCol.style.flexShrink = 1;

        var title = new Label(batch.DisplayTitle);
        title.AddToClassList("batch-history-row__title");

        var newest = batch.Jobs.Max(j => j.CreatedAtUtc);
        var meta = new Label(
            $"{batch.WorkflowName} · {batch.Jobs.Count} job(s) · {FormatRelativeTime(newest)}");
        meta.AddToClassList("batch-history-row__meta");

        textCol.Add(title);
        textCol.Add(meta);

        var statusIndicator = new VisualElement();
        statusIndicator.AddToClassList("job-status-indicator");
        SetStatusIndicatorColor(statusIndicator, GetAggregateBatchStatus(batch.Jobs));

        inner.Add(textCol);
        inner.Add(statusIndicator);
        row.Add(inner);

        row.tooltip =
            $"{batch.DisplayTitle}\n{batch.WorkflowName}\n{batch.Jobs.Count} job(s)\nBatchId: {batch.BatchId}";

        row.RegisterCallback<ClickEvent>(evt =>
        {
            batchesDrilldownBatchId = batch.BatchId;
            selectedJob = null;
            lastSelectedRow = null;
            ApplyFilterToolbarState();
            ShowJobDetails(null);
            RefreshFilteredList();
            evt.StopPropagation();
        });

        return row;
    }

    private static JobStatus GetAggregateBatchStatus(List<AtlasWorkflowJobState> jobs)
    {
        if (jobs == null || jobs.Count == 0)
            return JobStatus.Queued;
        if (jobs.Any(j => j.Status == JobStatus.Failed))
            return JobStatus.Failed;
        if (jobs.Any(j => j.Status == JobStatus.Running))
            return JobStatus.Running;
        if (jobs.Any(j => j.Status == JobStatus.Cancelled))
            return JobStatus.Cancelled;
        if (jobs.All(j => j.Status == JobStatus.Succeeded))
            return JobStatus.Succeeded;
        return JobStatus.Queued;
    }

    private void OnBatchDrillBack()
    {
        batchesDrilldownBatchId = null;
        selectedJob = null;
        lastSelectedRow = null;
        ApplyFilterToolbarState();
        ShowJobDetails(null);
        RefreshFilteredList();
    }

    private void RestoreJobSelectionHighlight()
    {
        if (selectedJob == null || jobListContainer == null)
            return;

        VisualElement match = null;
        FindMatchingJobRow(jobListContainer);
        if (match != null)
            HighlightSelectedRow(match);

        void FindMatchingJobRow(VisualElement ve)
        {
            if (match != null)
                return;

            if (ve.ClassListContains("job-history-row") &&
                ve.userData is AtlasWorkflowJobState j &&
                j.JobId == selectedJob.JobId)
            {
                match = ve;
                return;
            }

            foreach (var child in ve.Children())
                FindMatchingJobRow(child);
        }
    }

    /// <summary>UTC cutoff for the current date filter, or null when "All Time".</summary>
    private DateTime? GetDateCutoffUtc()
    {
        switch (currentDateFilter)
        {
            case "All Time":
                return null;
            case "Today":
                return DateTime.UtcNow.Date;
            case "Last 7 Days":
                return DateTime.UtcNow.AddDays(-7);
            case "Last 30 Days":
                return DateTime.UtcNow.AddDays(-30);
            default:
                return null;
        }
    }

    /// <summary>
    /// Applies the current filter settings to the job list. In batch drill-in, date and type are skipped (toolbar disabled; avoids stale Type from Jobs mode).
    /// </summary>
    private List<AtlasWorkflowJobState> ApplyFilters(
        List<AtlasWorkflowJobState> jobs,
        bool applyDateFilter = true,
        bool applyTypeFilter = true)
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
                    case "Cancelled": return j.Status == JobStatus.Cancelled;
                    default: return true;
                }
            });
        }

        if (applyTypeFilter && currentTypeFilter != "All")
            filtered = filtered.Where(j => j.WorkflowName == currentTypeFilter);

        if (applyDateFilter)
        {
            var cutoff = GetDateCutoffUtc();
            if (cutoff.HasValue)
                filtered = filtered.Where(j => j.CreatedAtUtc >= cutoff.Value);
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

        // LEFT: workflow name (+ optional batch instance hint)
        var nameLabel = new Label(job.WorkflowName ?? "Unnamed");
        nameLabel.AddToClassList("job-row-name");
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.whiteSpace = WhiteSpace.NoWrap;

        if (job.BatchIndex.HasValue && !string.IsNullOrEmpty(job.BatchId))
        {
            int total = GetBatchMemberCount(job.BatchId);
            if (total < 1)
                total = 1;
            int n = job.BatchIndex.Value + 1;
            nameLabel.text += $"  ·  batch {n}/{total}";
        }

        // Relative time label (e.g., "2m ago")
        var timeLabel = new Label(FormatRelativeTime(job.CreatedAtUtc));
        timeLabel.AddToClassList("job-row-time");

        // Tooltip with full details
        var startLocal = job.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        string duration = FormatJobDuration(job);
        row.tooltip = $"{job.WorkflowName}\nStarted: {startLocal}\nDuration: {duration}\nStatus: {job.Status}";
        if (job.BatchIndex.HasValue && !string.IsNullOrEmpty(job.BatchId))
        {
            int total = GetBatchMemberCount(job.BatchId);
            if (total < 1)
                total = 1;
            string batchLabel = string.IsNullOrEmpty(job.BatchName) ? job.BatchId : job.BatchName;
            row.tooltip += $"\nBatch: {batchLabel} (instance {job.BatchIndex.Value + 1} of {total})";
        }

        // RIGHT: colored status indicator
        var statusIndicator = new VisualElement();
        statusIndicator.AddToClassList("job-status-indicator");
        SetStatusIndicatorColor(statusIndicator, job.Status);

        // Build row
        row.Add(nameLabel);
        row.Add(timeLabel);
        row.Add(statusIndicator);

        // Selection behaviour unchanged
        row.userData = job;

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
            case JobStatus.Cancelled:
                color = new Color(0.75f, 0.55f, 0.35f);
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
            string hint;
            if (viewModeIsBatches && string.IsNullOrEmpty(batchesDrilldownBatchId))
                hint = "Select a batch to open its jobs.";
            else if (viewModeIsBatches && !string.IsNullOrEmpty(batchesDrilldownBatchId))
                hint = "Select a job from this batch.";
            else
                hint = "No job selected.";

            var empty = new Label(hint);
            empty.style.whiteSpace = WhiteSpace.Normal;
            empty.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
            empty.style.marginTop = 8;
            empty.style.marginLeft = 6;
            empty.style.fontSize = 11;
            rightPane.Add(empty);
            return;
        }

        if (jobDetailsView == null)
        {
            jobDetailsView = new WorkflowJobView();
        }

        rightPane.Add(jobDetailsView);
        jobDetailsView.PopulateFromJob(job, renderer, () => WorkflowJobRetry.RunRetryAsync(job));
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
            case JobStatus.Cancelled:
                label.style.color = new StyleColor(new Color(0.75f, 0.55f, 0.35f));
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
            ShowJobDetails(null);
        }

        // Refresh list from the current in-memory jobs
        Refresh(WorkflowManager.Jobs);
    }

    private int GetBatchMemberCount(string batchId)
    {
        if (string.IsNullOrEmpty(batchId) || allJobs == null)
            return 0;
        return allJobs.Count(j => j != null && j.BatchId == batchId);
    }

    private sealed class BatchAggregateInfo
    {
        public string BatchId;
        public string DisplayTitle;
        public string WorkflowName;
        public List<AtlasWorkflowJobState> Jobs;
    }
}
