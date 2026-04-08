using UnityEngine;
using UnityEditor;

#if UNITY_6000_3_OR_NEWER
// Unity 6.3+: main toolbar extensions use MainToolbarElement + MainToolbarDropdown (see Unity Manual, Unity 6.3 LTS).
// Earlier 6.x editors use the #else branch (ToolbarOverlay), which may not appear on the main strip in 6.3+.

using UnityEditor.Toolbars;

/// <summary>
/// Registers an "Atlas" dropdown on the main editor toolbar (Unity 6.3+).
/// Toggle visibility: right-click the main toolbar → context menu → Atlas section → Menu.
/// </summary>
public static class AtlasMainToolbarBootstrap
{
    public const string ToolbarElementPath = "Atlas/Menu";

    // Left dock: Unity VCS (collab-proxy) uses defaultDockIndex 13 — use a higher index so Atlas sits to the right of VCS.
    // (Among elements in the same dock, lower index = further left.)
    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 14)]
    public static MainToolbarElement CreateAtlasDropdown()
    {
        var content = new MainToolbarContent(
            "Atlas",
            AtlasToolbarBranding.ToolbarIcon,
            "Atlas Workflow: run, job history, and settings.");

        return new MainToolbarDropdown(content, ShowDropdownMenu) { displayed = true };
    }

    static void ShowDropdownMenu(Rect dropDownRect)
    {
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("Single Run"), false, () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Workflow"));
        menu.AddItem(new GUIContent("Batch Run"), false, () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Batch"));

        menu.AddSeparator("");

        menu.AddItem(new GUIContent("Job History"), false, () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Job History"));

        menu.AddSeparator("");

        menu.AddItem(new GUIContent("Atlas Workflow Settings"), false,
            () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Workflow Settings"));

        menu.DropDown(dropDownRect);
    }
}

/// <summary>
/// Rebuild the Atlas main-toolbar control after script reload so the factory runs with a fresh <c>displayed</c> state.
/// </summary>
[InitializeOnLoad]
static class AtlasMainToolbarRefreshOnScriptsReload
{
    static AtlasMainToolbarRefreshOnScriptsReload()
    {
        EditorApplication.delayCall += () => MainToolbar.Refresh(AtlasMainToolbarBootstrap.ToolbarElementPath);
    }
}

#else

using UnityEditor.Overlays;
using UnityEditor.Toolbars;

/// <summary>
/// Unity 6.0–6.2: main toolbar via Overlays API. In 6.3+, prefer <see cref="AtlasMainToolbarBootstrap"/> (MainToolbarElement).
/// </summary>
[Overlay(typeof(EditorWindow), IdOverlay, "Atlas", true)]
public sealed class AtlasMainToolbarOverlay : ToolbarOverlay
{
    internal const string IdOverlay = "com.atlas.workflow/AtlasMainToolbar";

    public AtlasMainToolbarOverlay()
        : base(AtlasToolbarDropdown.Id) { }
}

[EditorToolbarElement(AtlasToolbarDropdown.Id, typeof(EditorWindow))]
public sealed class AtlasToolbarDropdown : EditorToolbarDropdown
{
    public const string Id = "com.atlas.workflow/AtlasToolbarDropdown";

    public AtlasToolbarDropdown()
        : base("Atlas", ShowMenu)
    {
        tooltip = "Atlas Workflow: run, job history, and settings.";
        icon = AtlasToolbarBranding.ToolbarIcon;
    }

    static void ShowMenu()
    {
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("Single Run"), false, () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Workflow"));
        menu.AddItem(new GUIContent("Batch Run"), false, () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Batch"));

        menu.AddSeparator("");

        menu.AddItem(new GUIContent("Job History"), false, () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Job History"));

        menu.AddSeparator("");

        menu.AddItem(new GUIContent("Atlas Workflow Settings"), false,
            () => EditorApplication.ExecuteMenuItem("Atlas/Atlas Workflow Settings"));

        menu.ShowAsContext();
    }
}

#endif

static class AtlasToolbarBranding
{
    static Texture2D s_ToolbarIcon;

    internal static Texture2D ToolbarIcon =>
        s_ToolbarIcon ??= Resources.Load<Texture2D>("AtlasLogo");
}
