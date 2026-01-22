using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class WorkflowGUIUtils
{
    public static Color GetParamColor(ParamType type)
    {
        switch (type)
        {
            case ParamType.boolean: return new Color32(0xE7, 0x4C, 0x3C, 0xFF); // Red
            case ParamType.number: return new Color32(0xE6, 0xA0, 0x3C, 0xFF);  // Orange
            case ParamType.@string: return new Color32(0x52, 0x94, 0xE2, 0xFF); // Blue
            case ParamType.image: return new Color32(0xE6, 0xDB, 0x74, 0xFF);   // Yellow
            case ParamType.mesh: return new Color32(0xAE, 0x81, 0xFF, 0xFF);    // Purple
            default: return Color.grey;
        }
    }

    // Apply style to an existing element
    public static void StyleTypeIndicator(VisualElement root, ParamType type)
    {
        var indicator = root.Q<VisualElement>("type-indicator");
        if (indicator == null) return;

        Color color = GetParamColor(type);
        // Set background color for filled dot style
        indicator.style.backgroundColor = new StyleColor(color);
        // Also set borders for legacy support
        indicator.style.borderRightColor = color;
        indicator.style.borderTopColor = color;
        indicator.style.borderLeftColor = color;
        indicator.style.borderBottomColor = color;
    }

    public static string FormatParamValue(AtlasWorkflowParamState p)
    {
        switch (p.ParamType)
        {
            case ParamType.boolean: return p.BoolValue.ToString();
            case ParamType.number: return p.NumberValue.ToString("0.###");
            case ParamType.@string: return string.IsNullOrEmpty(p.StringValue) ? "(empty)" : p.StringValue;
            case ParamType.image:
            case ParamType.mesh: return string.IsNullOrEmpty(p.FilePath) ? "(no file)" : Path.GetFileName(p.FilePath);
            default: return "(unknown)";
        }
    }
}