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
            //Debug.LogError("AtlasSettingsProvider: Could not find a VisualTreeAsset named 'AtlasSettings'. " + "Make sure 'AtlasSettings.uxml' exists in the project.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

        if (visualTree == null)
        {
            //Debug.LogError($"AtlasSettingsProvider: Asset at '{path}' is not a VisualTreeAsset.");
            return;
        }

        // 2. Build the UI
        rootElement.Clear();
        visualTree.CloneTree(rootElement);

        // 3. Query elements by *name* (not class)
        var savePathField = rootElement.Q<TextField>("save-path-field");
        var browseButton = rootElement.Q<Button>("browse-save-path-button");

        if (savePathField == null)
        {
            //Debug.LogError("AtlasSettingsProvider: Could not find TextField with name 'save-path-field' in AtlasSettings.uxml.");
            return;
        }

        if (browseButton == null)
        {
            //Debug.LogError("AtlasSettingsProvider: Could not find Button with name 'browse-save-path-button' in AtlasSettings.uxml.");
            return;
        }

        // 4. Initialize field
        savePathField.isReadOnly = true; // ensure it's not editable
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
    }
}
