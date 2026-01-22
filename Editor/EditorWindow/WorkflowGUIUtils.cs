using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class WorkflowGUIUtils
{
    public static Color GetParamColor(ParamType type)
    {
        //Debug.Log("Finding color for ");
        //Debug.Log(type);
        switch (type)
        {
            case ParamType.boolean: return new Color32(0xAE, 0x81, 0xFF, 0xFF);
            case ParamType.number: return new Color32(0x52, 0x94, 0xE2, 0xFF);
            case ParamType.@string: return new Color32(0x79, 0xCC, 0x63, 0xFF);
            case ParamType.image: return new Color32(0xE6, 0xDB, 0x74, 0xFF);
            case ParamType.mesh: return new Color32(0xF9, 0x26, 0x72, 0xFF);
            default: return Color.grey;
        }
    }

    // Apply style to an existing element (Fixes the invisible UXML circles) ---
    public static void StyleTypeIndicator(VisualElement root, ParamType type)
    {
        // We find the 'type-indicator' element in the UXML
        var indicator = root.Q<VisualElement>("type-indicator");
        if (indicator == null) return;

        // Apply Color
        Color color = GetParamColor(type);
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