using UnityEngine.UIElements;

public class WorkflowUIBuilder
{
    private readonly AtlasWorkflowState state;
    private readonly WorkflowParamRenderer renderer;
    public WorkflowParamRenderer Renderer => renderer;

    public WorkflowUIBuilder(AtlasWorkflowState state)
    {
        this.state = state;
        // We initialize the renderer here. 
        // It loads the UXML templates once and holds the logic.
        this.renderer = new WorkflowParamRenderer(state);
    }

    public void PopulateInputs(VisualElement container)
    {
        container.Clear();
        foreach (var inputState in state.Inputs)
        {
            // isEditable = true -> Creates Toggles, TextFields, etc.
            var element = renderer.RenderInput(inputState, isEditable: true);
            if (element != null)
                container.Add(element);
        }
    }

    /// <summary>
    /// Populates outputs with full interactive UI (for job history with results).
    /// </summary>
    public void PopulateOutputs(VisualElement container, bool showFullOutput = false)
    {
        container.Clear();
        foreach (var outputState in state.Outputs)
        {
            VisualElement element;
            if (showFullOutput)
            {
                // Full output rendering with Import/View buttons (for job history)
                element = renderer.RenderOutput(outputState, true);
            }
            else
            {
                // Simple preview for Current Workflow (before job runs)
                element = renderer.RenderOutputPreview(outputState);
            }
            
            if (element != null)
                container.Add(element);
        }
    }
}