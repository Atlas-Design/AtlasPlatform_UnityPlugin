using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sanity-check for <see cref="WorkflowBatchValidator"/> without a test assembly (Phase 3 verification).
/// </summary>
public static class WorkflowBatchDebugMenu
{
    /// <summary>Developer-only entry; not under the top-level <c>Atlas</c> menu so that menu stays clean for users.</summary>
    [MenuItem("Window/Atlas/Batch Validator Self-Test", false, 1000)]
    public static void RunSelfTest()
    {
        var state = ScriptableObject.CreateInstance<AtlasWorkflowState>();
        try
        {
            state.ActiveApiId = "api-test";
            state.Version = "1";

            state.Inputs.Add(new AtlasWorkflowParamState
            {
                ParamId = "s",
                ParamType = ParamType.@string,
                StringValue = "hello"
            });
            state.Inputs.Add(new AtlasWorkflowParamState
            {
                ParamId = "n",
                ParamType = ParamType.number,
                NumberValue = 3.14f
            });

            var goodRow = WorkflowBatchRow.FromWorkflowInputs(state.Inputs);
            var badRow = WorkflowBatchRow.FromWorkflowInputs(state.Inputs);
            badRow.InputsByParamId["n"].NumberValue = float.NaN;

            var fp = new WorkflowBatchDefinition { WorkflowActiveApiId = "wrong", Rows = new List<WorkflowBatchRow>() };
            WorkflowBatchValidator.CaptureWorkflowFingerprint(state, fp);
            // fp now matches; force mismatch to test fingerprint path
            fp.WorkflowActiveApiId = "wrong";

            var r0 = WorkflowBatchValidator.Validate(state, new List<WorkflowBatchRow> { goodRow }, fp);
            Debug.Log($"[Atlas Batch Self-Test] Expect fingerprint issue: valid rows? {r0.IsValid} (issues: {r0.Issues.Count})");

            fp.WorkflowActiveApiId = state.ActiveApiId;
            var r1 = WorkflowBatchValidator.Validate(state, new List<WorkflowBatchRow> { goodRow }, fp);
            Debug.Log($"[Atlas Batch Self-Test] Good row: {r1.IsValid} (expect True)");

            var r2 = WorkflowBatchValidator.Validate(state, new List<WorkflowBatchRow> { goodRow, badRow }, fp);
            Debug.Log($"[Atlas Batch Self-Test] Bad number row: {r2.IsValid} (expect False, issues: {r2.Issues.Count})");

            var r3 = WorkflowBatchValidator.Validate(state, new List<WorkflowBatchRow>(), fp);
            Debug.Log($"[Atlas Batch Self-Test] Empty rows: {r3.IsValid} (expect False)");
        }
        finally
        {
            Object.DestroyImmediate(state);
        }
    }
}
