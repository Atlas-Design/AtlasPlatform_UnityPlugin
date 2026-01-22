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
        VisualElement header;

        if (jobHeaderTemplate != null)
            header = jobHeaderTemplate.Instantiate();
        else
            header = new VisualElement();

        header.style.flexGrow = 1f;
        header.style.width = Length.Percent(100);

        var titleField = header.Q<Label>("Title");
        var startTimeField = header.Q<Label>("StartTime");
        var statusField = header.Q<Label>("Status");
        var totalTimeField = header.Q<Label>("TotalTime");
        var progressBar = header.Q<ProgressBar>("Progress");

        // Left side
        if (titleField != null)
            titleField.text = (job.WorkflowName ?? "Unnamed") + " | ";

        if (startTimeField != null)
        {
            var localStart = job.CreatedAtUtc.ToLocalTime();
            startTimeField.text = localStart.ToString("HH:mm:ss");
        }

        // Right side
        if (statusField != null)
        {
            statusField.text = job.Status.ToString();
            SetStatusColor(statusField, job.Status);
        }

        if (totalTimeField != null)
        {
            var elapsed = System.DateTime.UtcNow - job.CreatedAtUtc;
            totalTimeField.text = "(" + FormatTimeSpan(elapsed) + ")";
        }

        // Spinner-style progress bar with proper cleanup
        if (progressBar != null)
        {
            if (job.Status == JobStatus.Running)
            {
                progressBar.style.display = DisplayStyle.Flex;

                float v = 0f;
                var startTime = job.CreatedAtUtc;

                // Store the scheduled item so we can stop it later
                IVisualElementScheduledItem scheduledItem = null;
                scheduledItem = progressBar.schedule.Execute(() =>
                {
                    // Safety check: stop if element is no longer attached to panel
                    if (header.panel == null)
                    {
                        scheduledItem?.Pause();
                        return;
                    }

                    v = (v + 3f) % 100f;
                    progressBar.value = v;

                    var elapsed = System.DateTime.UtcNow - startTime;
                    if (totalTimeField != null)
                        totalTimeField.text = "(" + FormatTimeSpan(elapsed) + ")";

                }).Every(50);

                // Stop the scheduled task when the element is removed from the visual tree
                header.RegisterCallback<DetachFromPanelEvent>(evt =>
                {
                    scheduledItem?.Pause();
                });
            }
            else
            {
                progressBar.style.display = DisplayStyle.None;
            }
        }

        return header;
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
}
