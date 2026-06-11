using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class RunningJobsView
{
    private readonly VisualElement panelRoot;
    private readonly ScrollView listRoot;
    private VisualTreeAsset jobHeaderTemplate;
    private readonly Action<AtlasWorkflowJobState> onStopJob;
    private string activeRunningJobId;

    private readonly Dictionary<string, RunningJobRow> trackedRows = new Dictionary<string, RunningJobRow>();
    private bool editorUpdateHooked;
    private double lastAnimationTickTime;
    private double lastElapsedTickTime;

    // EditorApplication.update can fire many times per rendered frame — advance visuals on a fixed wall-clock tick only.
    private const double AnimationTickIntervalSeconds = 0.1;   // 10 ticks / second
    private const double ElapsedTickIntervalSeconds = 0.25;    // elapsed label refresh
    private const float ProgressStepPerTick = 2.5f;            // ~4 s per full bar sweep
    private const float PulseStepPerTick = 0.04f;              // green-dot breathe

    private sealed class RunningJobRow
    {
        public AtlasWorkflowJobState Job;
        public VisualElement Root;
        public Label TotalTimeField;
        public ProgressBar ProgressBar;
        public VisualElement Spinner;
        public float ProgressValue;
        public float PulsePhase;
        public Action StopClickedHandler;
    }

    public RunningJobsView(VisualElement panelRoot, ScrollView listRoot, Action<AtlasWorkflowJobState> onStopJob = null)
    {
        this.panelRoot = panelRoot;
        this.listRoot = listRoot;
        this.onStopJob = onStopJob;

        LoadTemplate();
    }

    private void LoadTemplate()
    {
        jobHeaderTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Packages/com.atlas.workflow/Editor/EditorWindow/Elements/_JobHeader.uxml");
        if (jobHeaderTemplate == null)
        {
            Debug.LogError("[RunningJobsView] Could not load _JobHeader.uxml");
        }
    }

    /// <param name="jobs">All jobs; only Running are shown.</param>
    /// <param name="activeRunningJobId">Job currently executing in this editor session (Cancel vs Dismiss label).</param>
    public void Refresh(List<AtlasWorkflowJobState> jobs, string activeRunningJobId = null)
    {
        this.activeRunningJobId = activeRunningJobId;

        if (panelRoot == null || listRoot == null)
            return;

        var running = jobs?
            .Where(j => j.Status == JobStatus.Running)
            .ToList() ?? new List<AtlasWorkflowJobState>();

        if (running.Count == 0)
        {
            panelRoot.style.display = DisplayStyle.None;
            ClearTrackedRows();
            SetEditorUpdateHooked(false);
            return;
        }

        panelRoot.style.display = DisplayStyle.Flex;

        var runningIds = new HashSet<string>(running.Select(j => j.JobId));

        foreach (var staleId in trackedRows.Keys.Where(id => !runningIds.Contains(id)).ToList())
            RemoveTrackedRow(staleId);

        foreach (var job in running)
        {
            if (trackedRows.TryGetValue(job.JobId, out var existing) && existing.Root?.panel != null)
            {
                existing.Job = job;
                UpdateRowStaticFields(existing, job);
                continue;
            }

            if (trackedRows.ContainsKey(job.JobId))
                RemoveTrackedRow(job.JobId);

            var row = CreateRunningRow(job);
            listRoot.Add(row.Root);
            trackedRows[job.JobId] = row;
        }

        SetEditorUpdateHooked(true);
    }

    private void ClearTrackedRows()
    {
        foreach (var id in trackedRows.Keys.ToList())
            RemoveTrackedRow(id);
    }

    private void RemoveTrackedRow(string jobId)
    {
        if (!trackedRows.TryGetValue(jobId, out var row))
            return;

        row.Root?.RemoveFromHierarchy();
        trackedRows.Remove(jobId);
    }

    private void SetEditorUpdateHooked(bool hooked)
    {
        if (hooked)
        {
            if (!editorUpdateHooked)
            {
                lastAnimationTickTime = 0;
                lastElapsedTickTime = 0;
                EditorApplication.update += OnEditorUpdate;
                editorUpdateHooked = true;
            }
        }
        else if (editorUpdateHooked)
        {
            EditorApplication.update -= OnEditorUpdate;
            editorUpdateHooked = false;
            lastAnimationTickTime = 0;
            lastElapsedTickTime = 0;
        }
    }

    private void OnEditorUpdate()
    {
        if (trackedRows.Count == 0)
        {
            SetEditorUpdateHooked(false);
            return;
        }

        double editorNow = EditorApplication.timeSinceStartup;

        bool shouldAnimate = lastAnimationTickTime <= 0
            || editorNow - lastAnimationTickTime >= AnimationTickIntervalSeconds;
        if (shouldAnimate)
            lastAnimationTickTime = editorNow;

        bool shouldUpdateElapsed = lastElapsedTickTime <= 0
            || editorNow - lastElapsedTickTime >= ElapsedTickIntervalSeconds;
        if (shouldUpdateElapsed)
            lastElapsedTickTime = editorNow;

        if (!shouldAnimate && !shouldUpdateElapsed)
            return;

        var utcNow = DateTime.UtcNow;

        foreach (var row in trackedRows.Values)
        {
            if (row.Root?.panel == null || row.Job == null)
                continue;

            if (shouldUpdateElapsed && row.TotalTimeField != null)
            {
                var elapsed = utcNow - row.Job.CreatedAtUtc;
                row.TotalTimeField.text = FormatTimeSpan(elapsed);
            }

            if (!shouldAnimate)
                continue;

            row.ProgressValue = (row.ProgressValue + ProgressStepPerTick) % 100f;
            if (row.ProgressBar != null)
            {
                row.ProgressBar.lowValue = 0f;
                row.ProgressBar.highValue = 100f;
                row.ProgressBar.value = row.ProgressValue;
            }

            row.PulsePhase = (row.PulsePhase + PulseStepPerTick) % (2f * Mathf.PI);
            if (row.Spinner != null)
                row.Spinner.style.opacity = 0.55f + 0.35f * Mathf.Sin(row.PulsePhase);
        }
    }

    private RunningJobRow CreateRunningRow(AtlasWorkflowJobState job)
    {
        VisualElement root;

        if (jobHeaderTemplate != null)
            root = jobHeaderTemplate.Instantiate();
        else
            root = new VisualElement();

        var row = new RunningJobRow
        {
            Job = job,
            Root = root,
            TotalTimeField = root.Q<Label>("TotalTime"),
            ProgressBar = root.Q<ProgressBar>("Progress"),
            Spinner = root.Q<VisualElement>("Spinner")
        };

        UpdateRowStaticFields(row, job);
        return row;
    }

    private void UpdateRowStaticFields(RunningJobRow row, AtlasWorkflowJobState job)
    {
        var titleField = row.Root.Q<Label>("Title");
        var startTimeField = row.Root.Q<Label>("StartTime");
        var statusField = row.Root.Q<Label>("Status");
        var stopButton = row.Root.Q<Button>("StopJob");

        if (titleField != null)
        {
            titleField.text = job.WorkflowName ?? "Unnamed";
            if (job.BatchIndex.HasValue && !string.IsNullOrEmpty(job.BatchId))
            {
                titleField.text += $"  ·  batch #{job.BatchIndex.Value + 1}";
                var label = string.IsNullOrEmpty(job.BatchName) ? job.BatchId : job.BatchName;
                titleField.tooltip = $"Batch: {label}";
            }
        }

        if (startTimeField != null)
        {
            var localStart = job.CreatedAtUtc.ToLocalTime();
            startTimeField.text = "Started " + localStart.ToString("HH:mm:ss");
        }

        if (statusField != null)
            statusField.text = job.Status.ToString();

        if (row.TotalTimeField != null)
        {
            var elapsed = DateTime.UtcNow - job.CreatedAtUtc;
            row.TotalTimeField.text = FormatTimeSpan(elapsed);
        }

        if (stopButton != null)
        {
            bool isActiveSession = !string.IsNullOrEmpty(activeRunningJobId) && job.JobId == activeRunningJobId;
            stopButton.text = isActiveSession ? "Cancel" : "Dismiss";
            stopButton.tooltip = isActiveSession
                ? "Request cancellation of the workflow run in this editor session."
                : "Remove this job from Running (editor is not executing it; likely stale after restart). Server-side work may continue.";

            stopButton.clicked -= row.StopClickedHandler;
            row.StopClickedHandler = onStopJob != null ? () => onStopJob(job) : null;
            if (row.StopClickedHandler != null)
                stopButton.clicked += row.StopClickedHandler;
            else
                stopButton.SetEnabled(false);
        }
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1.0)
        {
            return string.Format("{0:00}:{1:00}:{2:00}",
                (int)span.TotalHours, span.Minutes, span.Seconds);
        }

        return string.Format("{0:00}:{1:00}", span.Minutes, span.Seconds);
    }
}
