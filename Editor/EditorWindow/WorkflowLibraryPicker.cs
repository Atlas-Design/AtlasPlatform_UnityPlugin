using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// Workflow library dropdown, active indicator, Import and Delete controls.
/// Shared by Atlas Workflow Editor and Workflow Batch Editor.
/// </summary>
public class WorkflowLibraryPicker : VisualElement
{
    private const string UxmlPath =
        "Packages/com.atlas.workflow/Editor/EditorWindow/Elements/WorkflowLibraryPicker.uxml";

    public DropdownField Dropdown { get; private set; }
    public Button ImportButton { get; private set; }
    public Button DeleteButton { get; private set; }
    public VisualElement ActiveDot { get; private set; }

    public WorkflowLibraryPicker()
    {
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        if (tree == null)
        {
            Add(new Label($"[Atlas] Missing {UxmlPath}"));
            return;
        }

        tree.CloneTree(this);

        ActiveDot = this.Q<VisualElement>("library-active-dot");
        Dropdown = this.Q<DropdownField>("library-dropdown");
        ImportButton = this.Q<Button>("load-file-button");
        DeleteButton = this.Q<Button>("delete-workflow-button");
    }

    /// <summary>
    /// Refreshes choices from disk and resets display when the current value is invalid or empty.
    /// </summary>
    public void RefreshDropdownChoices()
    {
        if (Dropdown == null)
            return;

        var workflows = WorkflowManager.GetSavedWorkflows().Select(Path.GetFileName).ToList();
        Dropdown.choices = workflows;

        if (string.IsNullOrEmpty(Dropdown.value) || !workflows.Contains(Dropdown.value))
        {
            if (workflows.Count == 0)
            {
                Dropdown.SetValueWithoutNotify("");
                SetDropdownPlaceholderText("No workflows - click Import");
            }
            else
            {
                Dropdown.SetValueWithoutNotify("");
                SetDropdownPlaceholderText("Select a workflow...");
            }
        }
    }

    public void SetDropdownPlaceholderText(string text)
    {
        if (Dropdown == null)
            return;

        var textElement = Dropdown.Q<TextElement>(className: "unity-base-popup-field__text");
        if (textElement != null)
            textElement.text = text;
    }

    /// <summary>
    /// When no workflow is loaded in state, show placeholder in the dropdown field.
    /// </summary>
    public void RefreshPlaceholderForEmptyState(bool isWorkflowLoaded)
    {
        if (isWorkflowLoaded || Dropdown == null)
            return;

        var workflows = WorkflowManager.GetSavedWorkflows();
        SetDropdownPlaceholderText(workflows.Count == 0
            ? "No workflows - click Import"
            : "Select a workflow...");
    }

    public void SetActiveWorkflowLoaded(bool loaded)
    {
        if (ActiveDot != null)
            ActiveDot.EnableInClassList("inactive", !loaded);
    }

    public void SetDeleteEnabled(bool enabled)
    {
        DeleteButton?.SetEnabled(enabled);
    }
}
