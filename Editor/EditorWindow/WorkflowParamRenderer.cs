using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class WorkflowParamRenderer
{
    private readonly AtlasWorkflowState state;
    private readonly VisualTreeAsset boolIn, numIn, strIn, imgIn, meshIn;
    private readonly VisualTreeAsset boolOut, numOut, strOut, imgOut, meshOut;

    public WorkflowParamRenderer(AtlasWorkflowState state)
    {
        this.state = state;

        // Load Templates once
        boolIn = LoadTemplate("_ParamInputBoolean");
        numIn = LoadTemplate("_ParamInputNumber");
        strIn = LoadTemplate("_ParamInputString");
        imgIn = LoadTemplate("_ParamInputImage");
        meshIn = LoadTemplate("_ParamInputImage"); // Mesh input uses Image template structure

        boolOut = LoadTemplate("_ParamOutputBoolean");
        numOut = LoadTemplate("_ParamOutputNumber");
        strOut = LoadTemplate("_ParamOutputString");
        imgOut = LoadTemplate("_ParamOutputImage");
        meshOut = LoadTemplate("_ParamOutputMesh");
    }

    public VisualElement RenderInput(AtlasWorkflowParamState param, bool isEditable)
    {
        // FIX: We REMOVED the check for !isEditable.
        // We now route EVERYTHING through the UXML creators.
        // The creators handle the 'read-only' state internally.
        switch (param.ParamType)
        {
            case ParamType.boolean: return CreateBoolInput(param, isEditable);
            case ParamType.number: return CreateNumberInput(param, isEditable);
            case ParamType.@string: return CreateStringInput(param, isEditable);
            case ParamType.image: return CreateImageInput(param, isEditable);
            case ParamType.mesh: return CreateMeshInput(param, isEditable);
            default: return new Label($"Unknown Input Type: {param.ParamType}");
        }
    }
    public VisualElement RenderOutput(AtlasWorkflowParamState param,bool isEditable)
    {
        // Outputs are ALWAYS interactive (for importing/viewing), even in history.
        switch (param.ParamType)
        {
            case ParamType.boolean: return CreateBoolOutput(param);
            case ParamType.number: return CreateNumberOutput(param);
            case ParamType.@string: return CreateStringOutput(param);
            case ParamType.image: return CreateImageOutput(param, isEditable);
            case ParamType.mesh: return CreateMeshOutput(param, isEditable);
            default: return new Label($"Unknown Output Type: {param.ParamType}");
        }
    }


    #region Interactive Input Logic (From WorkflowUIBuilder)

    private VisualElement CreateBoolInput(AtlasWorkflowParamState inputState, bool isEditable)
    {
        var root = boolIn.CloneTree();
        var row = root.Q<VisualElement>(className: "param-row") ?? root;

        SetupLabel(row, inputState.Label);

        // This finds the "type-indicator" in the UXML and colors it
        WorkflowGUIUtils.StyleTypeIndicator(root, inputState.ParamType);

        var toggle = row.Q<Toggle>("value-field");
        if (toggle != null)
        {
            toggle.value = inputState.BoolValue;
            toggle.SetEnabled(isEditable); // Disable interaction for History

            if (isEditable)
            {
                toggle.RegisterValueChangedCallback(evt => {
                    inputState.BoolValue = evt.newValue;
                    SaveState();
                });
            }
        }
        return row;
    }

    private VisualElement CreateNumberInput(AtlasWorkflowParamState inputState, bool isEditable)
    {
        var root = numIn.CloneTree();
        var row = root.Q<VisualElement>(className: "param-row") ?? root;

        SetupLabel(row, inputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, inputState.ParamType);

        var field = row.Q<FloatField>("value-field");
        if (field != null)
        {
            field.value = inputState.NumberValue;
            field.SetEnabled(isEditable);

            if (isEditable)
            {
                field.RegisterValueChangedCallback(evt => {
                    inputState.NumberValue = evt.newValue;
                    SaveState();
                });
            }
        }
        return row;
    }

    private VisualElement CreateStringInput(AtlasWorkflowParamState inputState, bool isEditable)
    {
        var root = strIn.CloneTree();
        var row = root.Q<VisualElement>(className: "param-row") ?? root;

        SetupLabel(row, inputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, inputState.ParamType);

        var field = row.Q<TextField>("value-field");
        if (field != null)
        {
            field.value = inputState.StringValue;
            field.SetEnabled(isEditable);

            if (isEditable)
            {
                field.RegisterValueChangedCallback(evt => {
                    inputState.StringValue = evt.newValue;
                    SaveState();
                });
            }
        }
        return row;
    }

    private VisualElement CreateImageInput(AtlasWorkflowParamState inputState, bool isEditable)
    {
        var root = imgIn.CloneTree();
        var headerRow = root.Q<VisualElement>(className: "param-row-header") ?? root;

        SetupLabel(headerRow, inputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, inputState.ParamType);

        var projectField = root.Q<ObjectField>("project-asset-field");
        if (projectField != null)
        {
            projectField.objectType = typeof(Texture2D);
            projectField.value = inputState.ImageValue;
            projectField.SetEnabled(isEditable);

            if (isEditable)
            {
                projectField.RegisterValueChangedCallback(evt => {
                    inputState.ImageValue = evt.newValue as Texture2D;
                    SaveState();
                });
            }
        }

        var filePathField = root.Q<TextField>("file-path-field");
        if (filePathField != null)
        {
            filePathField.value = inputState.FilePath;
            filePathField.SetEnabled(isEditable);
        }

        var browseButton = root.Q<Button>("browse-button");
        if (browseButton != null)
        {
            browseButton.SetEnabled(isEditable);
            if (isEditable)
            {
                browseButton.clicked += () => {
                    string path = EditorUtility.OpenFilePanel("Select Image", "", "png");
                    if (!string.IsNullOrEmpty(path))
                    {
                        inputState.FilePath = path;
                        if (filePathField != null) filePathField.value = path;
                        SaveState();
                    }
                };
            }
        }

        SetupSourceToggle(root, inputState, isEditable);
        return root;
    }

    private VisualElement CreateMeshInput(AtlasWorkflowParamState inputState, bool isEditable)
    {
        var root = meshIn.CloneTree();
        var headerRow = root.Q<VisualElement>(className: "param-row-header") ?? root;

        SetupLabel(headerRow, inputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, inputState.ParamType);

        var projectField = root.Q<ObjectField>("project-asset-field");
        if (projectField != null)
        {
            projectField.objectType = typeof(GameObject);
            projectField.value = inputState.MeshValue;
            projectField.SetEnabled(isEditable);

            if (isEditable)
            {
                projectField.RegisterValueChangedCallback(evt => {
                    inputState.MeshValue = evt.newValue as GameObject;
                    SaveState();
                });
            }
        }

        var filePathField = root.Q<TextField>("file-path-field");
        if (filePathField != null)
        {
            filePathField.value = inputState.FilePath;
            filePathField.SetEnabled(isEditable);
        }

        var browseButton = root.Q<Button>("browse-button");
        if (browseButton != null)
        {
            browseButton.SetEnabled(isEditable);
            if (isEditable)
            {
                browseButton.clicked += () => {
                    string path = EditorUtility.OpenFilePanel("Select Mesh", "", "glb");
                    if (!string.IsNullOrEmpty(path))
                    {
                        inputState.FilePath = path;
                        if (filePathField != null) filePathField.value = path;
                        SaveState();
                    }
                };
            }
        }

        SetupSourceToggle(root, inputState, isEditable);
        return root;
    }
     
    #endregion

    #region Interactive Output Logic (From WorkflowUIBuilder)

    private VisualElement CreateBoolOutput(AtlasWorkflowParamState outputState)
    {
        var root = boolOut.CloneTree();
        var row = root.Q<VisualElement>(className: "param-row") ?? root;
        SetupLabel(row, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(row, outputState.ParamType);

        var toggle = row.Q<Toggle>("value-field");
        if (toggle != null)
        {
            toggle.value = outputState.BoolValue;
            toggle.SetEnabled(false);
        }
        return row;
    }

    private VisualElement CreateNumberOutput(AtlasWorkflowParamState outputState)
    {
        var root = numOut.CloneTree();
        var row = root.Q<VisualElement>(className: "param-row") ?? root;
        SetupLabel(row, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(row, outputState.ParamType);

        var field = row.Q<FloatField>("value-field");
        if (field != null)
        {
            field.value = outputState.NumberValue;
            field.SetEnabled(false);
            field.isReadOnly = true;
        }
        return row;
    }

    private VisualElement CreateStringOutput(AtlasWorkflowParamState outputState)
    {
        var root = strOut.CloneTree();
        var row = root.Q<VisualElement>(className: "param-row") ?? root;
        SetupLabel(row, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(row, outputState.ParamType);

        var field = row.Q<TextField>("value-field");
        if (field != null)
        {
            field.value = outputState.StringValue;
            field.SetEnabled(false);
            field.isReadOnly = true;
        }
        return row;
    }

    private VisualElement CreateImageOutput(AtlasWorkflowParamState outputState, bool editable)
    {
        var root = imgOut.CloneTree();
        var header = root.Q<VisualElement>(className: "param-row-header") ?? root;
        SetupLabel(header, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, outputState.ParamType);

        var preview = root.Q<Image>("preview-image");
        var fileLabel = root.Q<Label>("file-label");

        string path = outputState.FilePath;
        bool hasFile = !string.IsNullOrEmpty(path) && File.Exists(path);
        string fileName = hasFile ? Path.GetFileName(path) : null;

        // Track texture for cleanup
        Texture2D previewTexture = null;

        // --- File label (both live + history) ---------------------------------
        if (fileLabel != null)
        {
            if (hasFile)
            {
                fileLabel.text = fileName;
                fileLabel.tooltip = path;
            }
            else
            {
                fileLabel.text = "No image generated yet.";
                fileLabel.tooltip = string.Empty;
            }
        }

        // --- Preview (both live + history) ------------------------------------
        if (preview != null)
        {
            if (!hasFile)
            {
                preview.style.display = DisplayStyle.None;
            }
            else
            {
                preview.style.display = DisplayStyle.Flex;

                // Load thumbnail from file
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    previewTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (previewTexture.LoadImage(bytes))
                    {
                        preview.image = previewTexture;
                    }
                    else
                    {
                        // Failed to load - clean up immediately
                        UnityEngine.Object.DestroyImmediate(previewTexture);
                        previewTexture = null;
                    }
                }
                catch (Exception ex)
                {
                    AtlasLogger.LogWarning($"Failed to load image preview: {ex.Message}");
                    // Clean up on error
                    if (previewTexture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(previewTexture);
                        previewTexture = null;
                    }
                }

                // In live panel we let you click to reveal; history can be read-only
                if (editable)
                {
                    preview.AddToClassList("clickable-output-image");
                    preview.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (evt.button == 0)
                            EditorUtility.RevealInFinder(path);
                    });
                }
            }
        }

        // --- Memory cleanup: Destroy texture when element is removed from hierarchy ---
        root.RegisterCallback<DetachFromPanelEvent>(evt =>
        {
            if (previewTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        });

        return root;
    }
    private VisualElement CreateMeshOutput(AtlasWorkflowParamState outputState, bool editable)
    {
        var root = meshOut.CloneTree();
        var header = root.Q<VisualElement>(className: "param-row-header") ?? root;
        SetupLabel(header, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, outputState.ParamType);

        var importButton = root.Q<Button>("import-button");
        var assetField = root.Q<ObjectField>("imported-asset-field");

        if (assetField != null)
            assetField.objectType = typeof(GameObject);

        string tempPath = outputState.FilePath;
        bool hasFile = !string.IsNullOrEmpty(tempPath) && File.Exists(tempPath);
        string fileName = hasFile ? Path.GetFileName(tempPath) : null;

        // Helper to fill the ObjectField after import (live panel only)
        void RefreshAssetField(string assetPath)
        {
            if (assetField == null) return;
            GameObject go = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            assetField.value = go;
        }

        if (importButton != null)
        {
            if (!hasFile)
            {
                importButton.text = "No file yet";
                importButton.SetEnabled(false);
            }
            else if (editable)
            {
                // LIVE PANEL: allow importing the mesh into Assets
                importButton.text = "Import Mesh";
                importButton.SetEnabled(true);

                importButton.clicked += () =>
                {
                    string folder = SettingsManager.GetSavePath();
                    if (string.IsNullOrEmpty(folder))
                        folder = "Assets";

                    string destFileName = $"{outputState.ParamId}.glb";
                    string destPath = AssetDatabase.GenerateUniqueAssetPath(
                        Path.Combine(folder, destFileName));

                    File.Copy(tempPath, destPath, true);
                    AssetDatabase.ImportAsset(destPath);
                    AssetDatabase.Refresh();
                    RefreshAssetField(destPath);
                };
            }
            else
            {
                // JOB HISTORY: read-only, but let user open the file location
                importButton.text = fileName ?? "Open File";
                importButton.SetEnabled(true);

                importButton.clicked += () =>
                {
                    EditorUtility.RevealInFinder(tempPath);
                };

                // In history we don't want to show/modify imported asset field,
                // so we can hide or disable it:
                if (assetField != null)
                {
                    assetField.style.display = DisplayStyle.None;
                }
            }
        }

        return root;
    }
    #endregion

    #region Internal Helpers

    private void SaveState()
    {
        EditorUtility.SetDirty(state);
    }
      
    private void SetupLabel(VisualElement row, string text)
    {
        var label = row.Q<Label>("label");
        if (label != null) label.text = text;
    }

    private void SetupSourceToggle(VisualElement root, AtlasWorkflowParamState pState, bool isEditable)
    {
        var projectButton = root.Q<Button>("source-project-button");
        var fileButton = root.Q<Button>("source-file-button");
        var projectField = root.Q("project-asset-field");
        var fileRow = root.Q("external-file-row");

        void EnsureValidSourceType()
        {
            bool hasProjectAsset = (pState.ImageValue != null || pState.MeshValue != null);
            bool hasFilePath = !string.IsNullOrEmpty(pState.FilePath);

            // Fix: If defaulted to Project (0) but data suggests FilePath, switch it.
            if (pState.SourceType == InputSourceType.Project && !hasProjectAsset && hasFilePath)
            {
                pState.SourceType = InputSourceType.FilePath;
            }
            // Fix: If set to FilePath but data suggests Project, switch it.
            else if (pState.SourceType == InputSourceType.FilePath && !hasFilePath && hasProjectAsset)
            {
                pState.SourceType = InputSourceType.Project;
            }
        }

        void UpdateVisibility()
        {
            EnsureValidSourceType();

            bool isProject = pState.SourceType == InputSourceType.Project;

            // Toggle Fields
            if (projectField != null)
                projectField.style.display = isProject ? DisplayStyle.Flex : DisplayStyle.None;
            if (fileRow != null)
                fileRow.style.display = isProject ? DisplayStyle.None : DisplayStyle.Flex;

            // Toggle Visual Highlight
            // Note: This relies on the USS having 'background-image: none' to show clearly
            projectButton?.EnableInClassList("source-toggle-button-active", isProject);
            fileButton?.EnableInClassList("source-toggle-button-active", !isProject);
        }

        if (isEditable)
        {
            if (projectButton != null)
                projectButton.clicked += () =>
                {
                    pState.SourceType = InputSourceType.Project;
                    UpdateVisibility();
                    SaveState();
                };

            if (fileButton != null)
                fileButton.clicked += () =>
                {
                    pState.SourceType = InputSourceType.FilePath;
                    UpdateVisibility();
                    SaveState();
                };
        }
        else
        {
            projectButton?.SetEnabled(false);
            fileButton?.SetEnabled(false);
        }

        // Apply initial state
        UpdateVisibility();
    }

    private VisualTreeAsset LoadTemplate(string uxmlName)
    {
        var guids = AssetDatabase.FindAssets($"t:VisualTreeAsset {uxmlName}");
        if (guids.Length == 0)
        {
            //Debug.LogError($"Could not find UXML Template: {uxmlName}.uxml");
            return null;
        }
        return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    #endregion
}