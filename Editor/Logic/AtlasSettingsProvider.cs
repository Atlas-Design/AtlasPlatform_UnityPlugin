// In Packages/com.atlas.workflow/Editor/Logic/AtlasSettingsProvider.cs

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class AtlasSettingsProvider : SettingsProvider
{
    private VisualElement _rootElement;

    public AtlasSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
        : base(path, scope) { }

    [SettingsProvider]
    public static SettingsProvider CreateAtlasSettingsProvider()
    {
        // Path in the Project Settings tree
        return new AtlasSettingsProvider("Project/Atlas Workflow", SettingsScope.Project);
    }

    public override void OnActivate(string searchContext, VisualElement rootElement)
    {
        _rootElement = rootElement;

        // 1. Find the UXML asset by name
        var guids = AssetDatabase.FindAssets("t:VisualTreeAsset AtlasSettings");
        if (guids == null || guids.Length == 0)
        {
            AtlasLogger.LogError("AtlasSettingsProvider: Could not find a VisualTreeAsset named 'AtlasSettings'. Make sure 'AtlasSettings.uxml' exists in the project.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

        if (visualTree == null)
        {
            AtlasLogger.LogError($"AtlasSettingsProvider: Asset at '{path}' is not a VisualTreeAsset.");
            return;
        }

        // 2. Build the UI
        rootElement.Clear();
        visualTree.CloneTree(rootElement);

        // 3. Query elements by *name* (not class)
        var savePathField = rootElement.Q<TextField>("save-path-field");
        var browseButton = rootElement.Q<Button>("browse-save-path-button");
        var verboseLoggingToggle = rootElement.Q<Toggle>("verbose-logging-toggle");

        if (savePathField == null)
        {
            AtlasLogger.LogError("AtlasSettingsProvider: Could not find TextField with name 'save-path-field' in AtlasSettings.uxml.");
            return;
        }

        if (browseButton == null)
        {
            AtlasLogger.LogError("AtlasSettingsProvider: Could not find Button with name 'browse-save-path-button' in AtlasSettings.uxml.");
            return;
        }

        // 4. Initialize save path field
        savePathField.isReadOnly = true;
        savePathField.SetValueWithoutNotify(SettingsManager.GetSavePath());

        // 5. Wire browse button
        browseButton.clicked += () =>
        {
            string currentPath = SettingsManager.GetSavePath();
            string newPath = EditorUtility.OpenFolderPanel(
                "Select Asset Save Folder",
                string.IsNullOrEmpty(currentPath) ? Application.dataPath : currentPath,
                ""
            );

            if (string.IsNullOrEmpty(newPath))
                return;

            if (newPath.StartsWith(Application.dataPath))
            {
                string relativePath = "Assets" + newPath.Substring(Application.dataPath.Length);
                SettingsManager.SetSavePath(relativePath);
                savePathField.SetValueWithoutNotify(relativePath);
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Invalid Path",
                    "The selected folder must be inside the project's Assets folder.",
                    "OK"
                );
            }
        };

        // 6. Initialize and wire verbose logging toggle
        if (verboseLoggingToggle != null)
        {
            verboseLoggingToggle.SetValueWithoutNotify(SettingsManager.GetVerboseLogging());
            verboseLoggingToggle.RegisterValueChangedCallback(evt =>
            {
                SettingsManager.SetVerboseLogging(evt.newValue);
                if (evt.newValue)
                {
                    AtlasLogger.Log("Verbose logging enabled.");
                }
            });
        }

        // 7. Initialize and wire API timeout settings
        var timeoutSlider = rootElement.Q<SliderInt>("timeout-slider");
        var timeoutValueLabel = rootElement.Q<Label>("timeout-value-label");
        var noTimeoutToggle = rootElement.Q<Toggle>("no-timeout-toggle");

        // Helper to update the timeout display
        void UpdateTimeoutDisplay()
        {
            if (timeoutValueLabel == null) return;

            int currentTimeout = SettingsManager.GetApiTimeoutMinutes();
            bool isNoLimit = currentTimeout == SettingsManager.NoTimeoutValue;

            timeoutValueLabel.text = SettingsManager.FormatTimeoutDisplay(currentTimeout);

            // Sync UI state
            if (noTimeoutToggle != null)
                noTimeoutToggle.SetValueWithoutNotify(isNoLimit);

            if (timeoutSlider != null)
            {
                timeoutSlider.SetEnabled(!isNoLimit);
                if (!isNoLimit)
                    timeoutSlider.SetValueWithoutNotify(currentTimeout);
            }
        }

        // Initialize timeout UI
        UpdateTimeoutDisplay();

        // Wire timeout slider
        if (timeoutSlider != null)
        {
            timeoutSlider.RegisterValueChangedCallback(evt =>
            {
                // Only save if not in "no limit" mode
                if (noTimeoutToggle == null || !noTimeoutToggle.value)
                {
                    SettingsManager.SetApiTimeoutMinutes(evt.newValue);
                    UpdateTimeoutDisplay();
                }
            });
        }

        // Wire no-timeout toggle
        if (noTimeoutToggle != null)
        {
            noTimeoutToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    // Enable no-limit mode
                    SettingsManager.SetApiTimeoutMinutes(SettingsManager.NoTimeoutValue);
                }
                else
                {
                    // Restore to slider value or default
                    int sliderValue = timeoutSlider?.value ?? SettingsManager.DefaultApiTimeoutMinutes;
                    SettingsManager.SetApiTimeoutMinutes(sliderValue);
                }
                UpdateTimeoutDisplay();
            });
        }

        // 8. Initialize and wire temp file management
        var tempFilesInfoLabel = rootElement.Q<Label>("temp-files-info-label");
        var cleanupTempButton = rootElement.Q<Button>("cleanup-temp-button");
        var clearAllTempButton = rootElement.Q<Button>("clear-all-temp-button");

        // Helper to refresh the temp files info display
        void RefreshTempFilesInfo()
        {
            if (tempFilesInfoLabel == null) return;

            var (fileCount, totalBytes) = AssetExporter.GetTempDirectoryInfo();
            if (fileCount == 0)
            {
                tempFilesInfoLabel.text = "No temp files";
            }
            else
            {
                tempFilesInfoLabel.text = $"{fileCount} file(s), {AssetExporter.FormatBytes(totalBytes)}";
            }
        }

        // Initial display
        RefreshTempFilesInfo();

        // Wire cleanup button (removes files older than 7 days)
        if (cleanupTempButton != null)
        {
            cleanupTempButton.clicked += () =>
            {
                int deleted = AssetExporter.CleanupTempFiles();
                RefreshTempFilesInfo();

                EditorUtility.DisplayDialog(
                    "Cleanup Complete",
                    deleted > 0
                        ? $"Deleted {deleted} temp file(s) older than 7 days."
                        : "No old temp files to clean up.",
                    "OK"
                );
            };
        }

        // Wire clear all button (removes everything)
        if (clearAllTempButton != null)
        {
            clearAllTempButton.clicked += () =>
            {
                var (fileCount, totalBytes) = AssetExporter.GetTempDirectoryInfo();

                if (fileCount == 0)
                {
                    EditorUtility.DisplayDialog("Clear Temp Files", "No temp files to clear.", "OK");
                    return;
                }

                bool confirm = EditorUtility.DisplayDialog(
                    "Clear All Temp Files",
                    $"This will delete ALL {fileCount} temp file(s) ({AssetExporter.FormatBytes(totalBytes)}).\n\n" +
                    "This cannot be undone. Continue?",
                    "Clear All",
                    "Cancel"
                );

                if (confirm)
                {
                    AssetExporter.ClearAllTempFiles();
                    RefreshTempFilesInfo();
                }
            };
        }
    }
}
