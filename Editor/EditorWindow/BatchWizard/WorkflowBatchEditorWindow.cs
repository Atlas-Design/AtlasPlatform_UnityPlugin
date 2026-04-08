using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Single-screen batch editor (workflow, rows, settings, run). Executor is Phase 6.
/// </summary>
public sealed class WorkflowBatchEditorWindow : EditorWindow
{
    private const string UxmlPath =
        "Packages/com.atlas.workflow/Editor/EditorWindow/Elements/WorkflowBatchEditor.uxml";

    private WorkflowBatchEditorSession _session;
    private WorkflowBatchEditorView _view;

    [MenuItem("Atlas/Atlas Batch", false, 1)]
    public static void Open()
    {
        var w = GetWindow<WorkflowBatchEditorWindow>();
        w.titleContent = new GUIContent("Atlas Batch");
        w.minSize = new Vector2(460, 420);
    }

    public void CreateGUI()
    {
        rootVisualElement.Clear();

        _view?.Unbind();
        _view = null;

        _session?.Dispose();
        _session = new WorkflowBatchEditorSession();

        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        if (tree == null)
        {
            rootVisualElement.Add(new Label($"[Atlas] Missing UXML: {UxmlPath}"));
            return;
        }

        tree.CloneTree(rootVisualElement);

        _view = new WorkflowBatchEditorView(rootVisualElement, _session);
        _view.Bind();
    }

    private void OnDestroy()
    {
        _view?.Unbind();
        _view = null;

        _session?.Dispose();
        _session = null;
    }
}
