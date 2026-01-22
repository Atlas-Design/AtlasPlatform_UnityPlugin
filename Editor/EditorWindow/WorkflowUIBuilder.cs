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

    public void PopulateOutputs(VisualElement container)
    {
        container.Clear();
        foreach (var outputState in state.Outputs)
        {
            // Outputs are always rendered fully so you can use "Import"/"View" buttons
            var element = renderer.RenderOutput(outputState,false);
            if (element != null)
                container.Add(element);
        }
    }
}