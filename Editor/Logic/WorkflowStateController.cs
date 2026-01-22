// In Packages/com.atlas.workflow/Editor/Logic/WorkflowStateController.cs

using System.IO;
using UnityEditor;
using UnityEngine;

public class WorkflowStateController
{
    private readonly AtlasWorkflowState state;

    #region Initialization
    public WorkflowStateController(AtlasWorkflowState state)
    {
        this.state = state;
    }
    #endregion

    #region State Management Logic
    /// <summary>
    /// Reads the JSON workflow file and populates the AtlasWorkflowState asset.
    /// </summary>
    /// <param name="filePath">Absolute path to the JSON file.</param>
    public void LoadWorkflowFromFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            WorkflowDefinition wf = WorkflowDefinition.FromJson(json);

            // 1. Reset and Populate Metadata
            state.Inputs.Clear();
            state.Outputs.Clear();
            state.ActiveName = wf.Name;
            state.ActiveApiId = wf.ApiId;
            state.BaseUrl = wf.BaseUrl;
            state.Version = wf.Version;

            // 2. Populate Inputs
            // Logic kept inline to preserve type inference (var)
            foreach (var inputDef in wf.Inputs)
            {
                var paramState = new AtlasWorkflowParamState
                {
                    ParamId = inputDef.Id,
                    Label = inputDef.Label,
                    ParamType = inputDef.Type
                };

                if (inputDef.DefaultValue != null)
                {
                    switch (paramState.ParamType)
                    {
                        case ParamType.boolean:
                            paramState.BoolValue = System.Convert.ToBoolean(inputDef.DefaultValue);
                            break;
                        case ParamType.number:
                            paramState.NumberValue = System.Convert.ToSingle(inputDef.DefaultValue);
                            break;
                        case ParamType.@string:
                            paramState.StringValue = System.Convert.ToString(inputDef.DefaultValue);
                            break;
                    }
                }
                state.Inputs.Add(paramState);
            }

            // 3. Populate Outputs
            foreach (var outputDef in wf.Outputs)
            {
                var paramState = new AtlasWorkflowParamState
                {
                    ParamId = outputDef.Id,
                    Label = outputDef.Id,
                    ParamType = outputDef.Type
                };
                state.Outputs.Add(paramState);
            }

            EditorUtility.SetDirty(state);
            //Debug.Log($"Successfully loaded workflow: {wf.Name}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load or parse workflow at {filePath}: {e.Message}");
            ClearState();
        }
    }

    /// <summary>
    /// Clears all data from the state asset.
    /// </summary>
    public void ClearState()
    {
        state.ActiveName = "";
        state.ActiveApiId = "";
        state.BaseUrl = "";
        state.Version = "";
        state.Inputs.Clear();
        state.Outputs.Clear();
        EditorUtility.SetDirty(state);
    }
    #endregion
}