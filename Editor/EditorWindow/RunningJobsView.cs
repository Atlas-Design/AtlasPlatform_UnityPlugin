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

    public RunningJobsView(VisualElement panelRoot, ScrollView listRoot)
    {
        this.panelRoot = panelRoot;
        this.listRoot = listRoot;

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

    public void Refresh(List<AtlasWorkflowJobState> jobs)
    {
        if (panelRoot == null || listRoot == null)
            return;

        listRoot.Clear();

        var running = jobs?
            .Where(j => j.Status == JobStatus.Running)
            .ToList() ?? new List<AtlasWorkflowJobState>();

        if (running.Count == 0)
        {
            // Hide the whole panel when nothing is running
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

        // Left side - job info
        if (titleField != null)
            titleField.text = job.WorkflowName ?? "Unnamed";

        if (startTimeField != null)
        {
            var localStart = job.CreatedAtUtc.ToLocalTime();
            startTimeField.text = "Started " + localStart.ToString("HH:mm:ss");
        }

        // Right side - status
        if (statusField != null)
        {
            statusField.text = job.Status.ToString();
        }

        if (totalTimeField != null)
        {
            var elapsed = System.DateTime.UtcNow - job.CreatedAtUtc;
            totalTimeField.text = FormatTimeSpan(elapsed);
        }

        // Animated progress bar and spinner pulse
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

                // Animate progress bar
                v = (v + 2f) % 100f;
                if (progressBar != null)
                    progressBar.value = v;

                // Pulse the spinner opacity
                pulse = (pulse + 0.05f) % (2f * Mathf.PI);
                if (spinner != null)
                {
                    float alpha = 0.6f + 0.4f * Mathf.Sin(pulse);
                    spinner.style.opacity = alpha;
                }

                // Update elapsed time
                var elapsed = System.DateTime.UtcNow - startTime;
                if (totalTimeField != null)
                    totalTimeField.text = FormatTimeSpan(elapsed);

            }).Every(50);

            row.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                scheduledItem?.Pause();
            });
        }
        else if (progressBar != null)
        {
            progressBar.style.display = DisplayStyle.None;
        }

        return row;
    }

    private string FormatTimeSpan(System.TimeSpan span)
    {
        if (span.TotalHours >= 1.0)
        {
            return string.Format("{0:00}:{1:00}:{2:00}",
                (int)span.TotalHours, span.Minutes, span.Seconds);
        }

        return string.Format("{0:00}:{1:00}", span.Minutes, span.Seconds);
    }

}
