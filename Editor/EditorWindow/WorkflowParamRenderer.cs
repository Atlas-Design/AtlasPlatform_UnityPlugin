using System;
using System.IO;
using System.Threading.Tasks;
using Atlas.Workflow;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class WorkflowParamRenderer
{
    private const float ReadOnlyTextMaxHeight = 120f;
    private const float ReadOnlyTextLineHeight = 16f;
    private const float ReadOnlyTextVerticalPadding = 14f;

    private readonly AtlasWorkflowState state;
    private readonly bool _markWorkflowAssetDirtyOnInputChange;
    private readonly VisualTreeAsset boolIn, numIn, strIn, imgIn, meshIn;
    private readonly VisualTreeAsset boolOut, numOut, strOut, imgOut, meshOut, audioOut;
    
    // Current job context for import folder generation
    private AtlasWorkflowJobState currentJob;

    /// <summary>
    /// Raised after any editable input mutates its backing <see cref="AtlasWorkflowParamState"/>.
    /// Use for batch rows where values are not persisted on the workflow asset.
    /// </summary>
    public event System.Action InputValuesMutated;

    public WorkflowParamRenderer(AtlasWorkflowState state, bool markWorkflowAssetDirtyOnInputChange = true)
    {
        this.state = state;
        _markWorkflowAssetDirtyOnInputChange = markWorkflowAssetDirtyOnInputChange;

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
        audioOut = LoadTemplate("_ParamOutputAudio");
    }

    /// <summary>
    /// Sets the current job context for rendering outputs.
    /// Call this before rendering outputs to enable proper import folder detection.
    /// </summary>
    public void SetJobContext(AtlasWorkflowJobState job)
    {
        currentJob = job;
    }

    /// <summary>
    /// Clears the job context (use when rendering live workflow, not history).
    /// </summary>
    public void ClearJobContext()
    {
        currentJob = null;
    }

    /// <summary>
    /// Gets the import folder path for the current job context.
    /// Format: {BaseSavePath}/{WorkflowName}/{Date}_{ShortJobId}/
    /// </summary>
    private string GetImportFolderPath()
    {
        if (currentJob == null)
            return null;

        string basePath = SettingsManager.GetSavePath();
        if (string.IsNullOrEmpty(basePath))
            basePath = "Assets/Atlas/Imported";

        // Sanitize workflow name for folder
        string workflowName = SanitizeFolderName(currentJob.WorkflowName ?? "UnknownWorkflow");
        
        // Format: 2026-02-03_143052_a1b2c3d4
        string dateStr = currentJob.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd_HHmmss");
        string shortId = currentJob.JobId?.Length >= 8 ? currentJob.JobId.Substring(0, 8) : currentJob.JobId ?? "unknown";
        string folderName = $"{dateStr}_{shortId}";

        return $"{basePath}/{workflowName}/{folderName}";
    }

    /// <summary>
    /// Tries to find an existing imported prefab for the current job and param.
    /// </summary>
    private GameObject FindExistingImportedPrefab(string paramId)
    {
        string folder = GetImportFolderPath();
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            return null;

        // Look for prefab with the param ID name
        string sanitizedName = SanitizeFolderName(paramId);
        string prefabPath = $"{folder}/{sanitizedName}.prefab";
        
        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    /// <summary>
    /// Tries to find an existing imported texture for the current job and param.
    /// </summary>
    private Texture2D FindExistingImportedTexture(string paramId)
    {
        string folder = GetImportFolderPath();
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            return null;

        // Look for texture with the param ID name (try common extensions)
        string sanitizedName = SanitizeFolderName(paramId);
        string[] extensions = { ".png", ".jpg", ".jpeg" };
        
        foreach (var ext in extensions)
        {
            string texPath = $"{folder}/{sanitizedName}{ext}";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null)
                return tex;
        }
        
        return null;
    }

    /// <summary>
    /// Tries to find an existing imported <see cref="AudioClip"/> for the current job and param.
    /// </summary>
    private AudioClip FindExistingImportedAudioClip(string paramId)
    {
        string folder = GetImportFolderPath();
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            return null;

        string sanitizedName = SanitizeFolderName(paramId);
        string[] extensions = { ".mp3", ".wav", ".ogg", ".m4a", ".aiff", ".flac", ".aif" };

        foreach (var ext in extensions)
        {
            string clipPath = $"{folder}/{sanitizedName}{ext}";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (clip != null)
                return clip;
        }

        return null;
    }

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Unnamed";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        name = name.Replace(' ', '_');
        return name;
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
            case ParamType.audio:
                return CreateUnsupportedAudioInputRow(param.Label);
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
            case ParamType.audio: return CreateAudioOutput(param, isEditable);
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

        var statusText = param.ParamType == ParamType.audio
            ? (string.IsNullOrEmpty(param.Format) ? "(pending · audio)" : $"(pending · {param.Format})")
            : "(pending)";
        var statusLabel = new Label(statusText);
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

            if (isEditable)
            {
                field.RegisterValueChangedCallback(evt => {
                    inputState.StringValue = evt.newValue;
                    SaveState();
                });
            }
            else
            {
                field.multiline = true;
                field.isReadOnly = true;
                field.AddToClassList("output-readonly-text");
                ConfigureReadOnlyTextField(field, inputState.StringValue);
            }
        }

        var copyButton = row.Q<Button>("copy-button");
        if (copyButton != null)
        {
            copyButton.style.display = isEditable ? DisplayStyle.None : DisplayStyle.Flex;
            copyButton.clicked += () =>
            {
                EditorGUIUtility.systemCopyBuffer = inputState.StringValue ?? string.Empty;
            };
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

        Action refreshSourceVisibility = null;
        void ApplyExternalImagePath(string path)
        {
            if (!IsSupportedImageFile(path))
                return;

            inputState.FilePath = path;
            inputState.SourceType = InputSourceType.FilePath;
            inputState.ImageValue = null;
            projectField?.SetValueWithoutNotify(null);
            UpdateFilePathDisplay(path);
            refreshSourceVisibility?.Invoke();
            SaveState();
        }

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
                        ApplyExternalImagePath(path);
                    }
                };
            }
        }

        refreshSourceVisibility = SetupSourceToggle(root, inputState, isEditable);
        if (isEditable)
            RegisterImageFileDropTarget(root.Q<VisualElement>(className: "param-input-box") ?? root, ApplyExternalImagePath);

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
            field.multiline = true;
            field.isReadOnly = true;
            ConfigureReadOnlyTextField(field, outputState.StringValue);
        }

        var copyButton = row.Q<Button>("copy-button");
        if (copyButton != null)
        {
            copyButton.clicked += () =>
            {
                EditorGUIUtility.systemCopyBuffer = outputState.StringValue ?? string.Empty;
            };
        }
        return row;
    }

    private static VisualElement CreateUnsupportedAudioInputRow(string label)
    {
        var row = new VisualElement();
        row.AddToClassList("param-row");
        row.Add(new Label(string.IsNullOrEmpty(label)
            ? "Audio input is not supported in this plugin version."
            : $"{label}: audio input is not supported in this plugin version."));
        return row;
    }

    private VisualElement CreateAudioOutput(AtlasWorkflowParamState outputState, bool _editable)
    {
        var template = audioOut != null ? audioOut : meshOut;
        var root = template != null ? template.CloneTree() : strOut.CloneTree();
        var header = root.Q<VisualElement>(className: "param-row-header") ?? root.Q<VisualElement>(className: "param-row") ?? root;
        SetupLabel(header, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, outputState.ParamType);

        string tempPath = outputState.FilePath;
        bool hasFile = !string.IsNullOrEmpty(tempPath) && File.Exists(tempPath);

        var importButton = root.Q<Button>("import-button");
        var assetField = root.Q<ObjectField>("imported-asset-field");
        var sourceLabel = root.Q<Label>("source-file-label");

        if (assetField != null)
        {
            assetField.objectType = typeof(AudioClip);
            assetField.SetEnabled(false);
        }

        AudioClip existingClip = FindExistingImportedAudioClip(outputState.ParamId);
        bool alreadyImported = existingClip != null;
        if (assetField != null && alreadyImported)
            assetField.value = existingClip;

        void UpdateSourceLabel()
        {
            if (sourceLabel == null)
                return;
            if (hasFile)
            {
                sourceLabel.text = TruncateFilePath(tempPath);
                sourceLabel.tooltip = tempPath;
            }
            else
            {
                string fmt = string.IsNullOrEmpty(outputState.Format) ? "" : $" .{outputState.Format.TrimStart('.')}";
                sourceLabel.text = $"(no file yet){fmt}";
                sourceLabel.tooltip = string.IsNullOrEmpty(outputState.Format)
                    ? "Run the workflow to generate audio."
                    : $"Expected format: {outputState.Format}";
            }
        }
        UpdateSourceLabel();

        if (importButton != null)
        {
            if (!hasFile)
            {
                importButton.text = "No file yet";
                importButton.SetEnabled(false);
            }
            else
            {
                importButton.text = alreadyImported ? "Re-import" : "Import Audio";
                importButton.SetEnabled(true);
                importButton.clicked += () =>
                {
                    ImportAudioToAssets(tempPath, outputState.ParamId, outputState.Format, assetField);
                };
            }
        }

        if (importButton == null && sourceLabel == null && assetField == null)
        {
            var field = root.Q<TextField>("value-field");
            if (field != null)
            {
                string fmt = string.IsNullOrEmpty(outputState.Format) ? "" : $".{outputState.Format}";
                field.value = hasFile ? tempPath : $"(pending) audio{fmt}";
                field.SetEnabled(false);
                field.isReadOnly = true;
            }
        }

        return root;
    }

    private VisualElement CreateImageOutput(AtlasWorkflowParamState outputState, bool editable)
    {
        var root = imgOut.CloneTree();
        var header = root.Q<VisualElement>(className: "param-row-header") ?? root;
        SetupLabel(header, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, outputState.ParamType);

        var preview = root.Q<Image>("preview-image");
        var importButton = root.Q<Button>("import-button");
        var assetField = root.Q<ObjectField>("imported-asset-field");

        string tempPath = outputState.FilePath;
        bool hasFile = !string.IsNullOrEmpty(tempPath) && File.Exists(tempPath);

        // Setup ObjectField for imported texture
        if (assetField != null)
        {
            assetField.objectType = typeof(Texture2D);
            assetField.SetEnabled(false); // Read-only
        }

        // Check for existing imported texture
        Texture2D existingTexture = FindExistingImportedTexture(outputState.ParamId);
        bool alreadyImported = existingTexture != null;

        // Show existing texture in ObjectField
        if (assetField != null && alreadyImported)
        {
            assetField.value = existingTexture;
        }

        // Track texture for cleanup (preview only)
        Texture2D previewTexture = null;

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
                    var bytes = File.ReadAllBytes(tempPath);
                    previewTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (previewTexture.LoadImage(bytes))
                    {
                        preview.image = previewTexture;
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(previewTexture);
                        previewTexture = null;
                    }
                }
                catch (Exception ex)
                {
                    AtlasLogger.LogWarning($"Failed to load image preview: {ex.Message}");
                    if (previewTexture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(previewTexture);
                        previewTexture = null;
                    }
                }

                // Click to reveal in finder
                if (editable)
                {
                    preview.AddToClassList("clickable-output-image");
                    preview.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (evt.button == 0)
                            EditorUtility.RevealInFinder(tempPath);
                    });
                }
            }
        }

        // --- Import Button ---
        if (importButton != null)
        {
            if (!hasFile)
            {
                importButton.text = "No file yet";
                importButton.SetEnabled(false);
            }
            else if (editable)
            {
                importButton.text = alreadyImported ? "Re-import" : "Import Image";
                importButton.SetEnabled(true);

                importButton.clicked += () =>
                {
                    ImportImageToAssets(tempPath, outputState.ParamId, assetField);
                };
            }
            else
            {
                // JOB HISTORY: read-only, open file location
                importButton.text = Path.GetFileName(tempPath) ?? "Open File";
                importButton.SetEnabled(true);

                importButton.clicked += () =>
                {
                    EditorUtility.RevealInFinder(tempPath);
                };

                // Hide asset field in history
                if (assetField != null)
                {
                    assetField.style.display = DisplayStyle.None;
                }
            }
        }

        // --- Memory cleanup: Destroy preview texture when element is removed ---
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

    /// <summary>
    /// Imports an image file to the Assets folder using job-based folder structure.
    /// </summary>
    private void ImportImageToAssets(string sourcePath, string paramId, ObjectField assetField)
    {
        // Use job-based folder path if available
        string folder = GetImportFolderPath();
        if (string.IsNullOrEmpty(folder))
        {
            folder = SettingsManager.GetSavePath();
            if (string.IsNullOrEmpty(folder))
                folder = "Assets/Atlas/Imported";
        }

        try
        {
            // Determine output path (must be Assets-relative; see AtlasAssetPathUtilities)
            string sanitizedName = SanitizeFolderName(paramId);
            string extension = Path.GetExtension(sourcePath).ToLower();
            if (string.IsNullOrEmpty(extension))
                extension = ".png";

            string destAssetPath = $"{folder}/{sanitizedName}{extension}".Replace('\\', '/');
            destAssetPath = AtlasAssetPathUtilities.NormalizeToAssetsRelative(destAssetPath);
            AtlasAssetPathUtilities.EnsureParentDirectoryExistsOnDisk(destAssetPath);

            string absDest = AtlasAssetPathUtilities.AssetPathToAbsolute(destAssetPath);
            File.Copy(sourcePath, absDest, overwrite: true);

            AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceSynchronousImport);
            
            // Load the imported texture
            Texture2D importedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(destAssetPath);
            
            if (importedTex != null)
            {
                Debug.Log($"[Atlas] Successfully imported image: {destAssetPath}");
                
                if (assetField != null)
                {
                    assetField.value = importedTex;
                }
                
                EditorGUIUtility.PingObject(importedTex);
            }
            else
            {
                Debug.LogError($"[Atlas] Failed to load imported image: {destAssetPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Atlas] Error importing image: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Copies a generated audio file (e.g. .mp3) into the job import folder and imports it as an <see cref="AudioClip"/>.
    /// </summary>
    private void ImportAudioToAssets(string sourcePath, string paramId, string formatHint, ObjectField assetField)
    {
        string folder = GetImportFolderPath();
        if (string.IsNullOrEmpty(folder))
        {
            folder = SettingsManager.GetSavePath();
            if (string.IsNullOrEmpty(folder))
                folder = "Assets/Atlas/Imported";
        }

        try
        {
            string sanitizedName = SanitizeFolderName(paramId);
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                var fmt = formatHint?.Trim().TrimStart('.').ToLowerInvariant();
                extension = string.IsNullOrEmpty(fmt) ? ".mp3" : "." + fmt;
            }

            string destAssetPath = $"{folder}/{sanitizedName}{extension}".Replace('\\', '/');
            destAssetPath = AtlasAssetPathUtilities.NormalizeToAssetsRelative(destAssetPath);
            AtlasAssetPathUtilities.EnsureParentDirectoryExistsOnDisk(destAssetPath);

            string absDest = AtlasAssetPathUtilities.AssetPathToAbsolute(destAssetPath);
            File.Copy(sourcePath, absDest, overwrite: true);
            AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var imported = AssetDatabase.LoadAssetAtPath<AudioClip>(destAssetPath);
            if (imported != null)
            {
                if (assetField != null)
                    assetField.value = imported;
                EditorGUIUtility.PingObject(imported);
                AtlasLogger.Log($"Imported audio clip: {destAssetPath}");
            }
            else
                AtlasLogger.LogError($"[Atlas] Failed to load imported audio as AudioClip: {destAssetPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Atlas] Error importing audio: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private VisualElement CreateMeshOutput(AtlasWorkflowParamState outputState, bool editable)
    {
        var root = meshOut.CloneTree();
        var header = root.Q<VisualElement>(className: "param-row-header") ?? root;
        SetupLabel(header, outputState.Label);
        WorkflowGUIUtils.StyleTypeIndicator(root, outputState.ParamType);

        var importButton = root.Q<Button>("import-button");
        var importInstanceButton = root.Q<Button>("import-instance-button");
        var assetField = root.Q<ObjectField>("imported-asset-field");

        if (assetField != null)
        {
            assetField.objectType = typeof(GameObject);
            // Make read-only - users shouldn't manually change this, it shows the imported asset
            assetField.SetEnabled(false);
        }

        string tempPath = outputState.FilePath;
        bool hasFile = !string.IsNullOrEmpty(tempPath) && File.Exists(tempPath);
        string fileName = hasFile ? Path.GetFileName(tempPath) : null;

        // Check for existing imported prefab
        GameObject existingPrefab = FindExistingImportedPrefab(outputState.ParamId);
        bool alreadyImported = existingPrefab != null;
        
        // Show existing prefab in ObjectField
        if (assetField != null && alreadyImported)
        {
            assetField.value = existingPrefab;
        }

        // Helper to instantiate prefab at scene view camera position
        void InstantiatePrefabAtCamera(GameObject prefab)
        {
            if (prefab == null) return;
            
            // Get the scene view camera position
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                // Position in front of camera
                Camera cam = sceneView.camera;
                spawnPosition = cam.transform.position + cam.transform.forward * 5f;
                spawnRotation = Quaternion.Euler(0, cam.transform.eulerAngles.y, 0);
            }
            
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = spawnPosition;
            instance.transform.rotation = spawnRotation;
            
            // Select the new instance and frame it
            Selection.activeGameObject = instance;
            Undo.RegisterCreatedObjectUndo(instance, "Import & Instance Mesh");
            
            AtlasLogger.Log($"Instantiated '{prefab.name}' at {spawnPosition}");
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
                importButton.text = alreadyImported ? "Re-import" : "Import Mesh";
                importButton.SetEnabled(true);

                importButton.clicked += () =>
                {
                    // Use custom GLB importer (no external dependencies)
                    ImportMeshWithGLBImporter(tempPath, outputState.ParamId, assetField);
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

        // Setup Import & Instance button
        if (importInstanceButton != null)
        {
            if (!hasFile)
            {
                importInstanceButton.SetEnabled(false);
            }
            else if (editable)
            {
                // If already imported, just instance the existing prefab
                if (alreadyImported)
                {
                    importInstanceButton.text = "Instance";
                    importInstanceButton.clicked += () =>
                    {
                        InstantiatePrefabAtCamera(existingPrefab);
                    };
                }
                else
                {
                    importInstanceButton.text = "Import & Instance";
                    importInstanceButton.clicked += () =>
                    {
                        // Import first, then instantiate
                        ImportMeshWithGLBImporter(tempPath, outputState.ParamId, assetField, (prefab) =>
                        {
                            InstantiatePrefabAtCamera(prefab);
                        });
                    };
                }
                importInstanceButton.SetEnabled(true);
            }
            else
            {
                // Hide in history view
                importInstanceButton.style.display = DisplayStyle.None;
            }
        }

        return root;
    }
    #endregion

    #region Internal Helpers

    private void SaveState()
    {
        InputValuesMutated?.Invoke();
        if (_markWorkflowAssetDirtyOnInputChange)
            EditorUtility.SetDirty(state);
    }

    /// <summary>
    /// Imports a GLB mesh using the custom Atlas GLBImporter.
    /// This creates mesh, textures, material, and prefab without external dependencies.
    /// Uses job-based folder structure: {BasePath}/{WorkflowName}/{Date}_{ShortJobId}/
    /// </summary>
    /// <param name="onImported">Optional callback invoked with the imported prefab on success.</param>
    private void ImportMeshWithGLBImporter(string sourcePath, string paramId, ObjectField assetField, Action<GameObject> onImported = null)
    {
        // Use job-based folder path if available, otherwise fall back to settings
        string folder = GetImportFolderPath();
        if (string.IsNullOrEmpty(folder))
        {
            folder = SettingsManager.GetSavePath();
            if (string.IsNullOrEmpty(folder))
                folder = "Assets/Atlas/Imported";
        }

        try
        {
            // Use our custom GLB importer
            var result = GLBImporter.Import(sourcePath, folder, paramId);

            if (result.Success && result.Prefab != null)
            {
                Debug.Log($"[Atlas] Successfully imported mesh: {result.PrefabPath}");
                
                if (assetField != null)
                {
                    assetField.value = result.Prefab;
                }
                
                EditorGUIUtility.PingObject(result.Prefab);
                
                // Invoke callback if provided
                onImported?.Invoke(result.Prefab);
            }
            else
            {
                Debug.LogError($"[Atlas] GLB import failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Atlas] Error importing mesh: {ex.Message}\n{ex.StackTrace}");
        }
    }

    #region DISABLED - Old GLTFast Import Code (kept for reference)
    /*
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
    private async void ImportMeshWithGltfastRuntime_DISABLED(string sourcePath, string paramId, ObjectField assetField)
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
    */
    #endregion
      
    private void SetupLabel(VisualElement row, string text)
    {
        var label = row.Q<Label>("label");
        if (label != null) label.text = text;
    }

    private void RegisterImageFileDropTarget(VisualElement dropTarget, Action<string> onImageDropped)
    {
        if (dropTarget == null || onImageDropped == null)
            return;

        void ClearDropState()
        {
            dropTarget.RemoveFromClassList("file-drop-target--active");
        }

        dropTarget.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            if (TryGetDraggedImagePath(out _))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                dropTarget.AddToClassList("file-drop-target--active");
                evt.StopPropagation();
            }
        });

        dropTarget.RegisterCallback<DragPerformEvent>(evt =>
        {
            if (TryGetDraggedImagePath(out var imagePath))
            {
                DragAndDrop.AcceptDrag();
                onImageDropped(imagePath);
                evt.StopPropagation();
            }

            ClearDropState();
        });

        dropTarget.RegisterCallback<DragLeaveEvent>(_ => ClearDropState());
        dropTarget.RegisterCallback<DragExitedEvent>(_ => ClearDropState());
    }

    private static bool TryGetDraggedImagePath(out string imagePath)
    {
        imagePath = null;

        if (DragAndDrop.paths == null)
            return false;

        foreach (var path in DragAndDrop.paths)
        {
            if (IsSupportedImageFile(path))
            {
                imagePath = path;
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedImageFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
    }

    private void ConfigureReadOnlyTextField(TextField field, string value)
    {
        if (field == null)
            return;

        field.verticalScrollerVisibility = ScrollerVisibility.Auto;
        field.style.maxHeight = ReadOnlyTextMaxHeight;

        var textScrollView = field.Q<ScrollView>();
        if (textScrollView != null)
        {
            textScrollView.mode = ScrollViewMode.Vertical;
            textScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            textScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        }

        void UpdateHeight()
        {
            float width = field.resolvedStyle.width;
            int estimatedLines = EstimateWrappedLineCount(value, width);
            float contentHeight = Mathf.Max(24f, estimatedLines * ReadOnlyTextLineHeight + ReadOnlyTextVerticalPadding);

            field.style.height = Mathf.Min(ReadOnlyTextMaxHeight, contentHeight);
        }

        field.RegisterCallback<GeometryChangedEvent>(_ => UpdateHeight());
        EditorApplication.delayCall += UpdateHeight;
    }

    private static int EstimateWrappedLineCount(string value, float fieldWidth)
    {
        if (string.IsNullOrEmpty(value))
            return 1;

        int charsPerLine = Mathf.Max(20, Mathf.FloorToInt(Mathf.Max(fieldWidth, 120f) / 7f));
        int lines = 0;
        var logicalLines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (var line in logicalLines)
        {
            int length = string.IsNullOrEmpty(line) ? 1 : line.Length;
            lines += Mathf.Max(1, Mathf.CeilToInt(length / (float)charsPerLine));
        }

        return lines;
    }

    private Action SetupSourceToggle(VisualElement root, AtlasWorkflowParamState pState, bool isEditable)
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

        void UpdateVisibility(bool inferSourceFromExistingValues = false)
        {
            if (inferSourceFromExistingValues)
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

        UpdateVisibility(inferSourceFromExistingValues: true);
        return () => UpdateVisibility();
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