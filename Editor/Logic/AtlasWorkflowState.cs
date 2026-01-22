using System.Collections.Generic;
using UnityEngine;


public enum InputSourceType { Project, FilePath }

// This class is a simple data container for the state of a SINGLE parameter.
// It's marked as [System.Serializable] so Unity knows how to show it in the Inspector
// when it's part of the AtlasWorkflowState ScriptableObject.
[System.Serializable]
public class AtlasWorkflowParamState
{
    public string ParamId;
    public string Label;
    public ParamType ParamType;

    // --- ADDED: Source Type Control ---
    public InputSourceType SourceType = InputSourceType.Project;

    // Value storage for simple types
    public bool BoolValue;
    public float NumberValue;
    public string StringValue;

    // Unity-specific references
    public Texture2D ImageValue;
    public GameObject MeshValue;

    // For referencing external files on disk
    public string FilePath;
}

// This is our main state manager. It inherits from ScriptableObject, making it a data asset.
// The [CreateAssetMenu] attribute adds a convenient menu item to create an instance of it.
[CreateAssetMenu(fileName = "AtlasWorkflowState", menuName = "Atlas/Workflow State Asset")]
public class AtlasWorkflowState : ScriptableObject
{
    [Header("Workflow Metadata")]
    public string ActiveApiId;
    public string ActiveName;
    public string BaseUrl; 
    public string Version; 

    [Header("Parameter Collections")]
    public List<AtlasWorkflowParamState> Inputs = new List<AtlasWorkflowParamState>();
    public List<AtlasWorkflowParamState> Outputs = new List<AtlasWorkflowParamState>();

    [Header("Job Status")]
    public bool IsJobRunning;
    public string JobStatus;
    public float JobProgress; // 0.0 to 1.0
}