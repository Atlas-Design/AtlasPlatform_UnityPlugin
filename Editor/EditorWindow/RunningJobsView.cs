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

        listRoot.Clear();

        var running = jobs?
            .Where(j => j.Status == JobStatus.Running)
            .ToList() ?? new List<AtlasWorkflowJobState>();

        if (running.Count == 0)
        {
            panelRoot.style.display = DisplayStyle.None;
            return;
        }

        panelRoot.style.display = DisplayStyle.Flex;

        foreach (var job in running)
        {
            var row = CreateRunningRow(job);
            listRoot.Add(row);
        }
    }

    private VisualElement CreateRunningRow(AtlasWorkflowJobState job)
    {
        VisualElement row;

        if (jobHeaderTemplate != null)
            row = jobHeaderTemplate.Instantiate();
        else
            row = new VisualElement();

        var titleField = row.Q<Label>("Title");
        var startTimeField = row.Q<Label>("StartTime");
        var statusField = row.Q<Label>("Status");
        var totalTimeField = row.Q<Label>("TotalTime");
        var progressBar = row.Q<ProgressBar>("Progress");
        var spinner = row.Q<VisualElement>("Spinner");
        var stopButton = row.Q<Button>("StopJob");

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

        if (totalTimeField != null)
        {
            var elapsed = DateTime.UtcNow - job.CreatedAtUtc;
            totalTimeField.text = FormatTimeSpan(elapsed);
        }

        if (stopButton != null)
        {
            bool isActiveSession = !string.IsNullOrEmpty(activeRunningJobId) && job.JobId == activeRunningJobId;
            stopButton.text = isActiveSession ? "Cancel" : "Dismiss";
            stopButton.tooltip = isActiveSession
                ? "Request cancellation of the workflow run in this editor session."
                : "Remove this job from Running (editor is not executing it; likely stale after restart). Server-side work may continue.";
            if (onStopJob != null)
                stopButton.clicked += () => onStopJob(job);
            else
                stopButton.SetEnabled(false);
        }

        if (job.Status == JobStatus.Running)
        {
            float v = 0f;
            float pulse = 0f;
            var startTime = job.CreatedAtUtc;

            IVisualElementScheduledItem scheduledItem = null;
            scheduledItem = row.schedule.Execute(() =>
            {
                if (row.panel == null)
                {
                    scheduledItem?.Pause();
                    return;
                }

                v = (v + 2f) % 100f;
                if (progressBar != null)
                    progressBar.value = v;

                pulse = (pulse + 0.05f) % (2f * Mathf.PI);
                if (spinner != null)
                {
                    float alpha = 0.6f + 0.4f * Mathf.Sin(pulse);
                    spinner.style.opacity = alpha;
                }

                var elapsed = DateTime.UtcNow - startTime;
                if (totalTimeField != null)
                    totalTimeField.text = FormatTimeSpan(elapsed);

            }).Every(50);

            row.RegisterCallback<DetachFromPanelEvent>(_ => { scheduledItem?.Pause(); });
        }
        else if (progressBar != null)
        {
            progressBar.style.display = DisplayStyle.None;
        }

        return row;
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
