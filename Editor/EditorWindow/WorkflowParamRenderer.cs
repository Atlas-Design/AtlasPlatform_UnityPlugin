using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
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
    public VisualElement RenderOutput(AtlasWorkflowParamState param, bool isEditable)
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

    /// <summary>
    /// Renders a simple preview row for an output parameter (used in Current Workflow before job runs).
    /// Shows type indicator, param name, and "(pending)" status - no interactive elements.
    /// </summary>
    public VisualElement RenderOutputPreview(AtlasWorkflowParamState param)
    {
        var row = new VisualElement();
        row.AddToClassList("param-row");
        row.AddToClassList("output-preview-row");

        // Type indicator dot
        var indicator = new VisualElement();
        indicator.name = "type-indicator";
        indicator.AddToClassList("type-indicator");
        row.Add(indicator);
        
        // Apply color based on type
        Color color = WorkflowGUIUtils.GetParamColor(param.ParamType);
        indicator.style.backgroundColor = new StyleColor(color);

        // Param label
        var label = new Label(param.Label);
        label.AddToClassList("param-label");
        row.Add(label);

        // Pending status
        var statusLabel = new Label("(pending)");
        statusLabel.AddToClassList("output-preview-status");
        row.Add(statusLabel);

        return row;
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

        // New label-based file path display
        var filePathLabel = root.Q<Label>("file-path-label");
        // Fallback to old TextField if label not found
        var filePathField = root.Q<TextField>("file-path-field");
        
        void UpdateFilePathDisplay(string path)
        {
            if (filePathLabel != null)
            {
                filePathLabel.text = TruncateFilePath(path);
                filePathLabel.tooltip = path ?? "";
            }
            else if (filePathField != null)
            {
                filePathField.value = path ?? "";
            }
        }
        
        UpdateFilePathDisplay(inputState.FilePath);

        var browseButton = root.Q<Button>("browse-button");
        if (browseButton != null)
        {
            browseButton.SetEnabled(isEditable);
            if (isEditable)
            {
                browseButton.clicked += () => {
                    string path = EditorUtility.OpenFilePanel("Select Image", "", "png,jpg,jpeg");
                    if (!string.IsNullOrEmpty(path))
                    {
                        inputState.FilePath = path;
                        UpdateFilePathDisplay(path);
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

        // New label-based file path display
        var filePathLabel = root.Q<Label>("file-path-label");
        // Fallback to old TextField if label not found
        var filePathField = root.Q<TextField>("file-path-field");
        
        void UpdateFilePathDisplay(string path)
        {
            if (filePathLabel != null)
            {
                filePathLabel.text = TruncateFilePath(path);
                filePathLabel.tooltip = path ?? "";
            }
            else if (filePathField != null)
            {
                filePathField.value = path ?? "";
            }
        }
        
        UpdateFilePathDisplay(inputState.FilePath);

        var browseButton = root.Q<Button>("browse-button");
        if (browseButton != null)
        {
            browseButton.SetEnabled(isEditable);
            if (isEditable)
            {
                browseButton.clicked += () => {
                    string path = EditorUtility.OpenFilePanel("Select Mesh", "", "glb,gltf,fbx,obj");
                    if (!string.IsNullOrEmpty(path))
                    {
                        inputState.FilePath = path;
                        UpdateFilePathDisplay(path);
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
                    // Use GLTFast Runtime API to bypass ScriptedImporter limitations
                    ImportMeshWithGltfastRuntime(tempPath, outputState.ParamId, assetField);
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

    /// <summary>
    /// Editor-compatible defer agent that processes everything immediately (synchronously).
    /// This avoids the DontDestroyOnLoad issue in GLTFast's default defer agent.
    /// </summary>
    private class EditorDeferAgent : IDeferAgent
    {
        public bool ShouldDefer() => false;
        public bool ShouldDefer(float duration) => false;
        public Task BreakPoint() => Task.CompletedTask;
        public Task BreakPoint(float duration) => Task.CompletedTask;
    }

    /// <summary>
    /// Custom logger to capture GLTFast errors and warnings.
    /// </summary>
    private class GltfLogger : ICodeLogger
    {
        public void Error(LogCode code, params string[] messages)
        {
            Debug.LogError($"[GLTFast Error] {code}: {string.Join(" ", messages)}");
        }

        public void Warning(LogCode code, params string[] messages)
        {
            Debug.LogWarning($"[GLTFast Warning] {code}: {string.Join(" ", messages)}");
        }

        public void Info(LogCode code, params string[] messages)
        {
            Debug.Log($"[GLTFast Info] {code}: {string.Join(" ", messages)}");
        }

        public void Error(string message) => Debug.LogError($"[GLTFast Error] {message}");
        public void Warning(string message) => Debug.LogWarning($"[GLTFast Warning] {message}");
        public void Info(string message) => Debug.Log($"[GLTFast Info] {message}");
    }

    /// <summary>
    /// Imports a GLB mesh using GLTFast's Runtime API (more flexible than ScriptedImporter)
    /// and saves it as a prefab in the Assets folder.
    /// </summary>
    private async void ImportMeshWithGltfastRuntime(string sourcePath, string paramId, ObjectField assetField)
    {
        string folder = SettingsManager.GetSavePath();
        if (string.IsNullOrEmpty(folder))
            folder = "Assets";

        // Ensure the folder exists
        if (!AssetDatabase.IsValidFolder(folder))
        {
            string parentFolder = Path.GetDirectoryName(folder);
            string newFolderName = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parentFolder)) parentFolder = "Assets";
            AssetDatabase.CreateFolder(parentFolder, newFolderName);
        }

        string prefabName = $"{paramId}.prefab";
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, prefabName));

        Debug.Log($"[Atlas] Loading mesh with GLTFast Runtime API from: {sourcePath}");

        try
        {
            // Read the GLB file as bytes - more reliable than file:// URI on Windows
            byte[] glbData = File.ReadAllBytes(sourcePath);
            Debug.Log($"[Atlas] Read {glbData.Length} bytes from GLB file");

            // Create GLTFast importer with Editor-compatible defer agent and logger
            var deferAgent = new EditorDeferAgent();
            var logger = new GltfLogger();
            var gltfImport = new GltfImport(null, deferAgent, null, logger);
            
            // Load the GLB file from bytes
            bool success = await gltfImport.LoadGltfBinary(glbData, new Uri(sourcePath));
            
            if (!success)
            {
                Debug.LogError($"[Atlas] GLTFast failed to load mesh. Check above for detailed errors.");
                return;
            }

            // Create a temporary parent GameObject to hold the instantiated mesh
            var tempParent = new GameObject($"TempMesh_{paramId}");
            
            try
            {
                // Instantiate the loaded mesh
                bool instantiateSuccess = await gltfImport.InstantiateMainSceneAsync(tempParent.transform);
                
                if (!instantiateSuccess)
                {
                    Debug.LogError("[Atlas] GLTFast failed to instantiate the mesh.");
                    UnityEngine.Object.DestroyImmediate(tempParent);
                    return;
                }

                // The mesh is now instantiated as children of tempParent
                // We need to save it as a prefab
                
                // Rename the root to something meaningful
                tempParent.name = paramId;

                // Save as prefab
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tempParent, prefabPath);
                
                if (prefab != null)
                {
                    Debug.Log($"[Atlas] Successfully imported mesh as prefab: {prefabPath}");
                    
                    // Update the asset field
                    if (assetField != null)
                    {
                        assetField.value = prefab;
                    }
                    
                    // Ping the asset in the Project window
                    EditorGUIUtility.PingObject(prefab);
                }
                else
                {
                    Debug.LogError($"[Atlas] Failed to save prefab to: {prefabPath}");
                }
            }
            finally
            {
                // Clean up the temporary scene object
                UnityEngine.Object.DestroyImmediate(tempParent);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Atlas] Error importing mesh: {ex.Message}\n{ex.StackTrace}");
        }
    }
      
    private void SetupLabel(VisualElement row, string text)
    {
        var label = row.Q<Label>("label");
        if (label != null) label.text = text;
    }

    private void SetupSourceToggle(VisualElement root, AtlasWorkflowParamState pState, bool isEditable)
    {
        var sourceDropdown = root.Q<DropdownField>("source-dropdown");
        var projectField = root.Q("project-asset-field");
        var fileRow = root.Q("external-file-row");

        // Fallback to old button-based system if dropdown not found
        var projectButton = root.Q<Button>("source-project-button");
        var fileButton = root.Q<Button>("source-file-button");

        void EnsureValidSourceType()
        {
            bool hasProjectAsset = (pState.ImageValue != null || pState.MeshValue != null);
            bool hasFilePath = !string.IsNullOrEmpty(pState.FilePath);

            if (pState.SourceType == InputSourceType.Project && !hasProjectAsset && hasFilePath)
            {
                pState.SourceType = InputSourceType.FilePath;
            }
            else if (pState.SourceType == InputSourceType.FilePath && !hasFilePath && hasProjectAsset)
            {
                pState.SourceType = InputSourceType.Project;
            }
        }

        void UpdateVisibility()
        {
            EnsureValidSourceType();

            bool isProject = pState.SourceType == InputSourceType.Project;

            if (projectField != null)
                projectField.style.display = isProject ? DisplayStyle.Flex : DisplayStyle.None;
            if (fileRow != null)
                fileRow.style.display = isProject ? DisplayStyle.None : DisplayStyle.Flex;

            // Update dropdown value if using new system
            if (sourceDropdown != null)
            {
                sourceDropdown.SetValueWithoutNotify(isProject ? "Project" : "External");
            }

            // Update buttons if using old system
            projectButton?.EnableInClassList("source-toggle-button-active", isProject);
            fileButton?.EnableInClassList("source-toggle-button-active", !isProject);
        }

        // New dropdown-based system
        if (sourceDropdown != null)
        {
            sourceDropdown.choices = new System.Collections.Generic.List<string> { "Project", "External" };
            sourceDropdown.SetEnabled(isEditable);

            if (isEditable)
            {
                sourceDropdown.RegisterValueChangedCallback(evt =>
                {
                    pState.SourceType = evt.newValue == "Project" 
                        ? InputSourceType.Project 
                        : InputSourceType.FilePath;
                    UpdateVisibility();
                    SaveState();
                });
            }
        }
        // Old button-based system (fallback)
        else if (isEditable)
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

        UpdateVisibility();
    }
    
    /// <summary>
    /// Truncates a file path to show only the filename, with full path in tooltip.
    /// </summary>
    private string TruncateFilePath(string path, int maxLength = 40)
    {
        if (string.IsNullOrEmpty(path)) return "No file selected";
        
        string fileName = Path.GetFileName(path);
        
        if (fileName.Length <= maxLength)
            return fileName;
            
        // Truncate with ellipsis
        return "..." + fileName.Substring(fileName.Length - maxLength + 3);
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