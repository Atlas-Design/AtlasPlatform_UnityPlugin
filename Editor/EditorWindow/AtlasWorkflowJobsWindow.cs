using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Dedicated window for Running Jobs and Job History (Phase 9).
/// </summary>
public class AtlasWorkflowJobsWindow : EditorWindow
{
    private WorkflowUIBuilder uiBuilder;
    private JobHistoryView historyView;
    private ScrollView jobsList;
    private Button jobsMenuBtn;
    private VisualElement runningJobsPanel;
    private ScrollView runningJobsList;
    private RunningJobsView runningJobsView;

    /// <summary>Priority 20 (vs 0–1 for workflow/batch) so Unity inserts a menu separator above this item.</summary>
    [MenuItem("Atlas/Atlas Job History", false, 20)]
    public static void ShowWindow()
    {
        GetWindow<AtlasWorkflowJobsWindow>("Atlas Job History");
    }

    private void CreateGUI()
    {
        var root = rootVisualElement;
        var visualTree = LoadAsset<VisualTreeAsset>("AtlasWorkflowJobsWindow");
        if (visualTree == null)
        {
            root.Add(new Label("Could not load AtlasWorkflowJobsWindow.uxml."));
            return;
        }

        visualTree.CloneTree(root);

        root.style.flexGrow = 1;
        root.style.minHeight = 0;
        root.style.flexDirection = FlexDirection.Column;

        var paramStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(
            "Packages/com.atlas.workflow/Editor/EditorWindow/Styles/_ParamStyles.uss");
        if (paramStyles != null)
            root.styleSheets.Add(paramStyles);

        var state = LoadAsset<AtlasWorkflowState>("AtlasWorkflowState");
        if (state == null)
        {
            root.Add(new Label("Critical Error: AtlasWorkflowState asset not found."));
            return;
        }

        uiBuilder = new WorkflowUIBuilder(state);

        runningJobsPanel = root.Q<VisualElement>("running-jobs-panel");
        runningJobsList = root.Q<ScrollView>("running-jobs-list");
        jobsList = root.Q<ScrollView>("jobs-list");
        jobsMenuBtn = root.Q<Button>("jobs-menu-btn");

        if (jobsList != null)
            historyView = new JobHistoryView(jobsList, uiBuilder.Renderer, WorkflowEditorRunSession.NotifyJobSelected);

        if (runningJobsPanel != null && runningJobsList != null)
            runningJobsView = new RunningJobsView(runningJobsPanel, runningJobsList, WorkflowEditorRunSession.HandleRunningJobStop);

        if (jobsMenuBtn != null)
            jobsMenuBtn.clicked += OnJobsMenuClicked;

        WorkflowManager.LoadJobsFromDisk();
        WorkflowManager.JobsMutated += OnWorkflowJobsListMutated;
        historyView?.Refresh(WorkflowManager.Jobs);
        RefreshRunningJobsPanel();
    }

    private void OnWorkflowJobsListMutated()
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null)
                return;

            historyView?.Refresh(WorkflowManager.Jobs);
            RefreshRunningJobsPanel();
        };
    }

    private void RefreshRunningJobsPanel()
    {
        if (runningJobsView != null)
            runningJobsView.Refresh(WorkflowManager.Jobs, WorkflowEditorRunSession.ActiveRunningJobId);
    }

    private void OnJobsMenuClicked()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Clear History"), false, () => { historyView?.ClearHistory(); });
        menu.ShowAsContext();
    }

    private void OnDestroy()
    {
        WorkflowManager.JobsMutated -= OnWorkflowJobsListMutated;
        if (jobsMenuBtn != null)
            jobsMenuBtn.clicked -= OnJobsMenuClicked;
    }

    private static T LoadAsset<T>(string assetName) where T : UnityEngine.Object
    {
        var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name} {assetName}");
        if (guids.Length == 0)
        {
            Debug.LogError($"Could not find asset: {assetName}");
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
