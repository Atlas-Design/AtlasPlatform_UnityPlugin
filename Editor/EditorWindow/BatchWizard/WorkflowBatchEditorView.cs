using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Single-screen batch editor: workflow library, rows area, settings, status. Bind once per window session.
/// </summary>
public sealed class WorkflowBatchEditorView
{
    private readonly VisualElement _root;
    private readonly WorkflowBatchEditorSession _session;

    private VisualElement _workflowPickerHost;
    private VisualElement _batchSelectedWorkflowHost;
    private VisualElement _batchActiveWorkflowStrip;
    private Label _batchStripName;
    private Label _batchStripSubtitle;
    private Label _batchStripStatus;
    private VisualElement _batchStripDot;
    private VisualElement _rowsHost;
    private VisualElement _rowsContent;
    private Button _addRowButton;
    private Button _btnSaveBatch;
    private Button _btnLoadBatch;
    private VisualElement _settingsHost;
    private Label _statusLabel;
    private Button _btnCancelBatch;
    private Button _btnRun;
    private Button _btnOpenJobs;

    private CancellationTokenSource _batchCts;
    private bool _batchRunning;

    private WorkflowLibraryPicker _picker;
    private EventCallback<ChangeEvent<string>> _dropdownHandler;
    private IntegerField _concurrency;
    private IntegerField _retries;
    private EventCallback<ChangeEvent<int>> _concHandler;
    private EventCallback<ChangeEvent<int>> _retHandler;

    private WorkflowParamRenderer _paramRenderer;
    private System.Action _paramMutatedHandler;

    public WorkflowBatchEditorView(VisualElement root, WorkflowBatchEditorSession session)
    {
        _root = root;
        _session = session;
    }

    /// <summary>
    /// Footer: Open Job History (left), spacer, Cancel, Run — survives stale UXML until Unity reimports.
    /// </summary>
    private void EnforceBatchFooterLayout()
    {
        if (_btnOpenJobs == null || _btnRun == null || _btnCancelBatch == null)
            return;

        var footer = _btnOpenJobs.parent;
        var spacer = footer?.Q<VisualElement>(className: "batch-editor-footer-spacer");
        if (footer == null || spacer == null)
            return;

        _btnOpenJobs.RemoveFromHierarchy();
        spacer.RemoveFromHierarchy();
        _btnCancelBatch.RemoveFromHierarchy();
        _btnRun.RemoveFromHierarchy();

        footer.Add(_btnOpenJobs);
        footer.Add(spacer);
        footer.Add(_btnCancelBatch);
        footer.Add(_btnRun);

        _btnOpenJobs.text = "Open Job History";
        _btnOpenJobs.tooltip = "Open Atlas Job History (running jobs and past runs)";
    }

    public void Bind()
    {
        _workflowPickerHost = _root.Q<VisualElement>("batch-workflow-picker-host");
        _batchSelectedWorkflowHost = _root.Q<VisualElement>("batch-selected-workflow-host");
        _batchActiveWorkflowStrip = _root.Q<VisualElement>("batch-active-workflow-strip");
        _batchStripName = _root.Q<Label>("batch-workflow-name-label");
        _batchStripSubtitle = _root.Q<Label>("batch-workflow-subtitle-label");
        _batchStripStatus = _root.Q<Label>("batch-workflow-strip-status");
        _batchStripDot = _root.Q<VisualElement>("batch-workflow-status-dot");
        _rowsHost = _root.Q<VisualElement>("batch-rows-host");
        _settingsHost = _root.Q<VisualElement>("batch-settings-host");
        _statusLabel = _root.Q<Label>("batch-status-label");
        _btnCancelBatch = _root.Q<Button>("batch-cancel-run-button");
        _btnRun = _root.Q<Button>("batch-run-button");
        _btnOpenJobs = _root.Q<Button>("batch-open-workflow-jobs-button");
        EnforceBatchFooterLayout();

        _paramRenderer = new WorkflowParamRenderer(_session.WorkflowState, markWorkflowAssetDirtyOnInputChange: false);
        _paramRenderer.ClearJobContext();
        _paramMutatedHandler = OnBatchParamMutated;
        _paramRenderer.InputValuesMutated += _paramMutatedHandler;

        BuildWorkflowSection();
        BuildRowsSection();
        BuildSettingsSection();
        RefreshWorkflowSummary();

        if (_btnOpenJobs != null)
            _btnOpenJobs.clicked += OnOpenWorkflowJobsClicked;
        if (_btnCancelBatch != null)
        {
            _btnCancelBatch.SetEnabled(false);
            _btnCancelBatch.style.display = DisplayStyle.None;
            _btnCancelBatch.clicked += OnCancelBatchClicked;
        }

        if (_btnRun != null)
        {
            _btnRun.clicked += OnRunClicked;
            UpdateRunButtonState();
            UpdateRunButtonTooltip();
        }

        if (_btnSaveBatch != null)
            _btnSaveBatch.clicked += OnSaveBatchClicked;
        if (_btnLoadBatch != null)
            _btnLoadBatch.clicked += OnLoadBatchClicked;
    }

    public void Unbind()
    {
        if (_paramRenderer != null && _paramMutatedHandler != null)
            _paramRenderer.InputValuesMutated -= _paramMutatedHandler;
        _paramRenderer = null;
        _paramMutatedHandler = null;

        _batchCts?.Cancel();
        _batchCts?.Dispose();
        _batchCts = null;
        _batchRunning = false;

        if (_btnOpenJobs != null)
            _btnOpenJobs.clicked -= OnOpenWorkflowJobsClicked;
        if (_btnCancelBatch != null)
            _btnCancelBatch.clicked -= OnCancelBatchClicked;
        if (_btnRun != null)
            _btnRun.clicked -= OnRunClicked;
        if (_btnSaveBatch != null)
            _btnSaveBatch.clicked -= OnSaveBatchClicked;
        if (_btnLoadBatch != null)
            _btnLoadBatch.clicked -= OnLoadBatchClicked;

        if (_picker?.ImportButton != null)
            _picker.ImportButton.clicked -= OnImportClicked;
        if (_picker?.DeleteButton != null)
            _picker.DeleteButton.clicked -= OnDeleteClicked;
        if (_picker?.Dropdown != null && _dropdownHandler != null)
            _picker.Dropdown.UnregisterValueChangedCallback(_dropdownHandler);

        if (_concurrency != null && _concHandler != null)
            _concurrency.UnregisterValueChangedCallback(_concHandler);
        if (_retries != null && _retHandler != null)
            _retries.UnregisterValueChangedCallback(_retHandler);

        _workflowPickerHost?.Clear();
        _rowsHost?.Clear();
        _settingsHost?.Clear();
        _rowsContent = null;
        _addRowButton = null;
        _btnSaveBatch = null;
        _btnLoadBatch = null;

        _picker = null;
        _dropdownHandler = null;
        _concurrency = null;
        _retries = null;
        _concHandler = null;
        _retHandler = null;
    }

    private void BuildWorkflowSection()
    {
        if (_workflowPickerHost == null)
            return;

        _picker = new WorkflowLibraryPicker();
        _workflowPickerHost.Add(_picker);

        if (_picker.ImportButton != null)
            _picker.ImportButton.clicked += OnImportClicked;
        if (_picker.DeleteButton != null)
            _picker.DeleteButton.clicked += OnDeleteClicked;

        _dropdownHandler = OnLibrarySelectionChanged;
        if (_picker.Dropdown != null)
            _picker.Dropdown.RegisterValueChangedCallback(_dropdownHandler);

        _picker.RefreshDropdownChoices();
    }

    private void BuildRowsSection()
    {
        if (_rowsHost == null)
            return;

        _rowsHost.Clear();

        _rowsContent = new VisualElement();
        _rowsContent.AddToClassList("batch-rows-content");
        _rowsHost.Add(_rowsContent);

        var toolbar = new VisualElement();
        toolbar.AddToClassList("batch-rows-toolbar");

        var primaryGroup = new VisualElement();
        primaryGroup.AddToClassList("batch-rows-toolbar-group");
        primaryGroup.AddToClassList("batch-rows-toolbar-group--primary");
        _addRowButton = new Button(OnAddRowClicked) { text = "+ Add row" };
        _addRowButton.AddToClassList("library-import-btn");
        primaryGroup.Add(_addRowButton);

        var fileGroup = new VisualElement();
        fileGroup.AddToClassList("batch-rows-toolbar-group");
        fileGroup.AddToClassList("batch-rows-toolbar-group--file");
        _btnSaveBatch = new Button { text = "Save batch…" };
        _btnSaveBatch.AddToClassList("batch-file-action-btn");
        _btnSaveBatch.tooltip = "Save this batch as a draft file (.atlasbatch.json) under the Atlas batches folder.";
        _btnLoadBatch = new Button { text = "Load batch…" };
        _btnLoadBatch.AddToClassList("batch-file-action-btn");
        _btnLoadBatch.AddToClassList("batch-file-action-btn--last");
        _btnLoadBatch.tooltip = "Load a saved batch draft. The workflow must exist in your workflow library.";
        fileGroup.Add(_btnSaveBatch);
        fileGroup.Add(_btnLoadBatch);

        toolbar.Add(primaryGroup);
        toolbar.Add(fileGroup);

        _rowsHost.Add(toolbar);
    }

    private void BuildSettingsSection()
    {
        if (_settingsHost == null)
            return;

        var help = new Label(
            "Limited parallelism and transient-only retries apply when the batch runner runs (Phase 6).")
        {
            style = { whiteSpace = WhiteSpace.Normal, marginBottom = 8 }
        };
        help.AddToClassList("hint-text");
        _settingsHost.Add(help);

        _concurrency = new IntegerField("Max concurrent runs");
        _concurrency.SetValueWithoutNotify(Mathf.Clamp(_session.MaxConcurrentRuns, 1, 16));
        _concHandler = evt =>
        {
            _session.MaxConcurrentRuns = Mathf.Clamp(evt.newValue, 1, 16);
            _concurrency.SetValueWithoutNotify(_session.MaxConcurrentRuns);
            RefreshStatusLabel();
            UpdateRunButtonState();
        };
        _concurrency.RegisterValueChangedCallback(_concHandler);
        _settingsHost.Add(_concurrency);

        _retries = new IntegerField("Max transient retries per instance");
        _retries.SetValueWithoutNotify(Mathf.Clamp(_session.MaxTransientRetriesPerInstance, 0, 10));
        _retHandler = evt =>
        {
            _session.MaxTransientRetriesPerInstance = Mathf.Clamp(evt.newValue, 0, 10);
            _retries.SetValueWithoutNotify(_session.MaxTransientRetriesPerInstance);
            RefreshStatusLabel();
            UpdateRunButtonState();
        };
        _retries.RegisterValueChangedCallback(_retHandler);
        _settingsHost.Add(_retries);
    }

    private void OnBatchParamMutated()
    {
        RefreshRowIssueLabels();
        RefreshStatusLabel();
        UpdateRunButtonState();
        UpdateRunButtonTooltip();
    }

    private void OnAddRowClicked()
    {
        if (!_session.HasWorkflowLoaded || _session.WorkflowState.Inputs == null ||
            _session.WorkflowState.Inputs.Count == 0)
            return;

        _session.BatchDefinition.Rows.Add(WorkflowBatchRow.FromWorkflowInputs(_session.WorkflowState.Inputs));
        RebuildRowsFromData();
        RefreshStatusLabel();
        UpdateRunButtonState();
        UpdateRunButtonTooltip();
    }

    private void DuplicateRow(int index)
    {
        if (index < 0 || index >= _session.BatchDefinition.Rows.Count)
            return;
        var copy = _session.BatchDefinition.Rows[index].Clone();
        _session.BatchDefinition.Rows.Insert(index + 1, copy);
        RebuildRowsFromData();
        RefreshStatusLabel();
        UpdateRunButtonState();
        UpdateRunButtonTooltip();
    }

    private void DeleteRow(int index)
    {
        if (index < 0 || index >= _session.BatchDefinition.Rows.Count)
            return;
        _session.BatchDefinition.Rows.RemoveAt(index);
        RebuildRowsFromData();
        RefreshStatusLabel();
        UpdateRunButtonState();
        UpdateRunButtonTooltip();
    }

    private void MoveRow(int index, int delta)
    {
        int n = _session.BatchDefinition.Rows.Count;
        int j = index + delta;
        if (index < 0 || index >= n || j < 0 || j >= n)
            return;

        var row = _session.BatchDefinition.Rows[index];
        _session.BatchDefinition.Rows.RemoveAt(index);
        _session.BatchDefinition.Rows.Insert(j, row);
        RebuildRowsFromData();
        RefreshStatusLabel();
        UpdateRunButtonState();
        UpdateRunButtonTooltip();
    }

    private void RebuildRowsFromData()
    {
        if (_rowsContent == null || _paramRenderer == null)
            return;

        _rowsContent.Clear();

        if (!_session.HasWorkflowLoaded)
        {
            var l = new Label("Load a workflow to add batch instances.");
            l.AddToClassList("hint-text");
            _rowsContent.Add(l);
            if (_addRowButton != null)
                _addRowButton.SetEnabled(false);
            return;
        }

        if (_session.WorkflowState.Inputs == null || _session.WorkflowState.Inputs.Count == 0)
        {
            var l = new Label("This workflow has no inputs — nothing to batch.");
            l.AddToClassList("hint-text");
            _rowsContent.Add(l);
            if (_addRowButton != null)
                _addRowButton.SetEnabled(false);
            return;
        }

        if (_addRowButton != null)
            _addRowButton.SetEnabled(true);

        if (_session.BatchDefinition.Rows.Count == 0)
        {
            var l = new Label("No instances yet. Click “Add row” for each run you want.");
            l.AddToClassList("hint-text");
            _rowsContent.Add(l);
            return;
        }

        int count = _session.BatchDefinition.Rows.Count;
        for (var i = 0; i < count; i++)
        {
            int idx = i;
            _rowsContent.Add(CreateRowCard(idx, count));
        }

        RefreshRowIssueLabels();
    }

    private VisualElement CreateRowCard(int index, int totalRows)
    {
        var rowState = _session.BatchDefinition.Rows[index];
        var card = new VisualElement();
        card.AddToClassList("batch-row-card");

        var header = new VisualElement();
        header.AddToClassList("batch-row-header");
        /* UIToolkit sometimes omits row flex on code-built headers; force horizontal toolbar */
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;

        var title = new Label($"Instance {index + 1}");
        title.AddToClassList("batch-row-title");
        header.Add(title);

        var spacer = new VisualElement();
        spacer.style.flexGrow = 1;
        header.Add(spacer);

        var up = new Button(() => MoveRow(index, -1)) { text = "↑" };
        up.AddToClassList("batch-row-tool-btn");
        up.SetEnabled(index > 0);
        up.tooltip = "Move up";
        header.Add(up);

        var down = new Button(() => MoveRow(index, 1)) { text = "↓" };
        down.AddToClassList("batch-row-tool-btn");
        down.SetEnabled(index < totalRows - 1);
        down.tooltip = "Move down";
        header.Add(down);

        var dup = new Button(() => DuplicateRow(index)) { text = "Duplicate" };
        dup.AddToClassList("batch-row-tool-btn");
        dup.AddToClassList("batch-row-tool-btn--accent");
        dup.tooltip = "Duplicate this row";
        header.Add(dup);

        var del = new Button(() => DeleteRow(index)) { text = "Remove" };
        del.AddToClassList("batch-row-tool-btn");
        del.AddToClassList("library-delete-btn");
        del.tooltip = "Remove this row";
        header.Add(del);

        card.Add(header);

        var issues = new Label { name = "batch-row-issues" };
        issues.AddToClassList("batch-row-issues");
        issues.style.display = DisplayStyle.None;
        card.Add(issues);

        var inputsWrap = new VisualElement();
        inputsWrap.AddToClassList("batch-row-inputs");
        foreach (var schema in _session.WorkflowState.Inputs)
        {
            if (schema == null || string.IsNullOrEmpty(schema.ParamId))
                continue;

            if (!rowState.InputsByParamId.TryGetValue(schema.ParamId, out var cell) || cell == null)
            {
                cell = WorkflowBatchRow.CloneParamCell(schema);
                rowState.InputsByParamId[schema.ParamId] = cell;
            }

            var el = _paramRenderer.RenderInput(cell, isEditable: true);
            if (el != null)
                inputsWrap.Add(el);
        }

        card.Add(inputsWrap);
        return card;
    }

    private void RefreshRowIssueLabels()
    {
        if (_rowsContent == null || _session?.WorkflowState == null)
            return;

        var result = WorkflowBatchValidator.Validate(
            _session.WorkflowState,
            _session.BatchDefinition.Rows,
            _session.BatchDefinition);

        var byRow = new Dictionary<int, List<BatchValidationIssue>>();
        foreach (var issue in result.Issues)
        {
            if (issue.RowIndex < 0)
                continue;
            if (!byRow.TryGetValue(issue.RowIndex, out var list))
            {
                list = new List<BatchValidationIssue>();
                byRow[issue.RowIndex] = list;
            }

            list.Add(issue);
        }

        for (var r = 0; r < _rowsContent.childCount; r++)
        {
            var child = _rowsContent[r];
            var label = child.Q<Label>("batch-row-issues");
            if (label == null)
                continue;

            if (!byRow.TryGetValue(r, out var list) || list.Count == 0)
            {
                label.text = string.Empty;
                label.style.display = DisplayStyle.None;
            }
            else
            {
                var sb = new StringBuilder();
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                        sb.AppendLine();
                    var iss = list[i];
                    if (!string.IsNullOrEmpty(iss.ParamId))
                        sb.Append(iss.ParamId).Append(": ");
                    sb.Append(iss.Message);
                }

                label.text = sb.ToString();
                label.style.display = DisplayStyle.Flex;
            }
        }
    }

    private void RefreshWorkflowSummary()
    {
        if (_session?.WorkflowState == null)
            return;

        if (!_session.HasWorkflowLoaded)
        {
            _picker?.SetActiveWorkflowLoaded(false);
            _picker?.SetDeleteEnabled(false);
            _picker?.RefreshPlaceholderForEmptyState(true);
        }
        else
        {
            _picker?.SetActiveWorkflowLoaded(true);
            _picker?.SetDeleteEnabled(true);
            _picker?.RefreshPlaceholderForEmptyState(false);
        }

        RebuildRowsFromData();
        RefreshStatusLabel();
        RefreshBatchWorkflowStrip();
        UpdateRunButtonState();
        UpdateRunButtonTooltip();
    }

    /// <summary>
    /// Header strip under “Selected Workflow” — matches single editor workflow summary header.
    /// </summary>
    private void RefreshBatchWorkflowStrip()
    {
        if (_batchActiveWorkflowStrip == null)
            return;

        if (_session?.WorkflowState == null || !_session.HasWorkflowLoaded)
        {
            if (_batchSelectedWorkflowHost != null)
                _batchSelectedWorkflowHost.style.display = DisplayStyle.None;
            _batchActiveWorkflowStrip.style.display = DisplayStyle.None;
            return;
        }

        if (_batchSelectedWorkflowHost != null)
            _batchSelectedWorkflowHost.style.display = DisplayStyle.Flex;
        _batchActiveWorkflowStrip.style.display = DisplayStyle.Flex;

        var s = _session.WorkflowState;

        if (_batchStripName != null)
            _batchStripName.text = string.IsNullOrEmpty(s.ActiveName) ? "Workflow" : s.ActiveName;

        if (_batchStripSubtitle != null)
        {
            string domain = ExtractDomain(s.BaseUrl);
            string version = string.IsNullOrEmpty(s.Version) ? "" : $"v{s.Version}";

            if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(domain))
                _batchStripSubtitle.text = $"{version} \u2022 {domain}";
            else if (!string.IsNullOrEmpty(domain))
                _batchStripSubtitle.text = domain;
            else if (!string.IsNullOrEmpty(version))
                _batchStripSubtitle.text = version;
            else
                _batchStripSubtitle.text = "";

            _batchStripSubtitle.tooltip = $"API ID: {s.ActiveApiId}\nBase URL: {s.BaseUrl}";
        }

        if (_batchStripStatus != null)
            _batchStripStatus.text = _batchRunning ? "Running batch…" : "Ready";

        if (_batchStripDot != null)
        {
            var ready = new Color(0.4f, 0.8f, 0.4f);
            var running = new Color(0.95f, 0.85f, 0.35f);
            _batchStripDot.style.backgroundColor = new StyleColor(_batchRunning ? running : ready);
        }
    }

    private static string ExtractDomain(string url)
    {
        if (string.IsNullOrEmpty(url))
            return "";
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return url;
        }
    }

    private void RefreshStatusLabel()
    {
        if (_statusLabel == null || _session == null)
            return;

        if (_batchRunning)
            return;

        var name = _session.HasWorkflowLoaded ? _session.WorkflowState.ActiveName : "(none)";
        var validation = WorkflowBatchValidator.Validate(
            _session.WorkflowState,
            _session.BatchDefinition.Rows,
            _session.BatchDefinition);

        string validationSummary = validation.IsValid
            ? "Validation OK."
            : $"{validation.Issues.Count} validation issue(s).";

        if (!validation.IsValid)
        {
            foreach (var issue in validation.Issues)
            {
                if (issue.RowIndex >= 0 || string.IsNullOrEmpty(issue.Message))
                    continue;
                validationSummary += " — " + issue.Message;
                break;
            }
        }

        _statusLabel.text =
            $"Batch: {_session.BatchDefinition.Rows.Count} row(s)  ·  " +
            $"Concurrency {_session.MaxConcurrentRuns}  ·  " +
            $"Retries {_session.MaxTransientRetriesPerInstance}  ·  " +
            $"Workflow: {name}  ·  {validationSummary}";
    }

    private void UpdateRunButtonState()
    {
        if (_btnRun == null || _session == null)
            return;

        if (_batchRunning)
        {
            _btnRun.SetEnabled(false);
            return;
        }

        var validation = WorkflowBatchValidator.Validate(
            _session.WorkflowState,
            _session.BatchDefinition.Rows,
            _session.BatchDefinition);

        _btnRun.SetEnabled(validation.IsValid && _session.BatchDefinition.Rows.Count > 0);
    }

    private void UpdateRunButtonTooltip()
    {
        if (_btnRun == null)
            return;

        var validation = WorkflowBatchValidator.Validate(
            _session.WorkflowState,
            _session.BatchDefinition.Rows,
            _session.BatchDefinition);

        if (_batchRunning)
            _btnRun.tooltip = "Batch is running.";
        else if (!validation.IsValid)
            _btnRun.tooltip = "Fix validation issues in batch rows before running.";
        else if (_session.BatchDefinition.Rows.Count == 0)
            _btnRun.tooltip = "Add at least one batch row.";
        else
            _btnRun.tooltip = "Run all batch instances (see Run settings for concurrency and retries).";
    }

    private void ApplyBatchRunningUi(bool running)
    {
        _batchRunning = running;

        if (_btnCancelBatch != null)
        {
            _btnCancelBatch.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            _btnCancelBatch.SetEnabled(running);
        }

        _workflowPickerHost?.SetEnabled(!running);
        if (_addRowButton != null)
        {
            bool canAdd = !running && _session.HasWorkflowLoaded &&
                          _session.WorkflowState?.Inputs != null &&
                          _session.WorkflowState.Inputs.Count > 0;
            _addRowButton.SetEnabled(canAdd);
        }

        _btnSaveBatch?.SetEnabled(!running);
        _btnLoadBatch?.SetEnabled(!running);

        _concurrency?.SetEnabled(!running);
        _retries?.SetEnabled(!running);
        _rowsContent?.SetEnabled(!running);

        UpdateRunButtonState();
        UpdateRunButtonTooltip();
        RefreshBatchWorkflowStrip();
    }

    private void OnCancelBatchClicked()
    {
        _batchCts?.Cancel();
    }

    private async void OnRunClicked()
    {
        if (_batchRunning)
            return;

        if (!_session.HasWorkflowLoaded)
        {
            EditorUtility.DisplayDialog("Batch Editor", "Load a workflow first.", "OK");
            return;
        }

        var validation = WorkflowBatchValidator.Validate(
            _session.WorkflowState,
            _session.BatchDefinition.Rows,
            _session.BatchDefinition);

        if (!validation.IsValid)
        {
            EditorUtility.DisplayDialog(
                "Batch Editor",
                "Fix validation issues in the batch rows before running.",
                "OK");
            return;
        }

        if (_session.BatchDefinition.Rows.Count == 0)
        {
            EditorUtility.DisplayDialog("Batch Editor", "Add at least one row to run.", "OK");
            return;
        }

        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();
        ApplyBatchRunningUi(true);

        try
        {
            var progress = new Progress<WorkflowBatchProgress>(p =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (_statusLabel == null)
                        return;
                    _statusLabel.text =
                        $"Running batch: {p.CompletedCount}/{p.TotalCount} finished — {p.Message}";
                };
            });

            await WorkflowBatchRunOrchestrator.RunAsync(
                _session.WorkflowState,
                _session.BatchDefinition,
                _session.MaxConcurrentRuns,
                _session.MaxTransientRetriesPerInstance,
                _batchCts.Token,
                progress);
        }
        catch (OperationCanceledException)
        {
            // Batch was cancelled via Cancel batch or window closed.
        }
        catch (Exception ex)
        {
            AtlasLogger.LogException(ex, "Batch run failed");
            EditorUtility.DisplayDialog("Batch Editor", $"Batch error: {ex.Message}", "OK");
        }
        finally
        {
            _batchCts?.Dispose();
            _batchCts = null;
            ApplyBatchRunningUi(false);
            RefreshStatusLabel();
            RefreshRowIssueLabels();
        }
    }

    private void OnSaveBatchClicked()
    {
        if (_batchRunning)
            return;

        if (!_session.HasWorkflowLoaded)
        {
            EditorUtility.DisplayDialog("Batch Editor", "Load a workflow first.", "OK");
            return;
        }

        string baseName = SanitizeDraftFileName(_session.WorkflowState.ActiveName);
        if (string.IsNullOrEmpty(baseName))
            baseName = "batch";
        string defaultFile = $"{baseName}-{DateTime.Now:yyyyMMdd-HHmm}.atlasbatch.json";

        string path = EditorUtility.SaveFilePanel(
            "Save batch draft",
            WorkflowBatchPersistence.GetDraftsDirectory(),
            defaultFile,
            "atlasbatch.json");

        if (string.IsNullOrEmpty(path))
            return;

        if (!path.EndsWith(".atlasbatch.json", StringComparison.OrdinalIgnoreCase))
            path += ".atlasbatch.json";

        if (!WorkflowBatchPersistence.SaveDraftToPath(
                path,
                _session.BatchDefinition,
                _session.WorkflowState,
                _session.MaxConcurrentRuns,
                _session.MaxTransientRetriesPerInstance,
                out var err))
        {
            EditorUtility.DisplayDialog("Save batch draft", err ?? "Save failed.", "OK");
            return;
        }

        AtlasLogger.LogFile($"Batch draft saved: {path}");
    }

    private void OnLoadBatchClicked()
    {
        if (_batchRunning)
            return;

        string path = EditorUtility.OpenFilePanel(
            "Load batch draft",
            WorkflowBatchPersistence.GetDraftsDirectory(),
            "json");

        if (string.IsNullOrEmpty(path))
            return;

        if (!WorkflowBatchPersistence.LoadDraft(
                path,
                _session.WorkflowState,
                _session.StateController,
                _session.BatchDefinition,
                out var maxConc,
                out var maxRetries,
                out var err))
        {
            EditorUtility.DisplayDialog("Load batch draft", err ?? "Load failed.", "OK");
            return;
        }

        _session.MaxConcurrentRuns = Mathf.Clamp(maxConc, 1, 16);
        _session.MaxTransientRetriesPerInstance = Mathf.Clamp(maxRetries, 0, 10);
        if (_concurrency != null)
        {
            _concurrency.SetValueWithoutNotify(_session.MaxConcurrentRuns);
        }

        if (_retries != null)
        {
            _retries.SetValueWithoutNotify(_session.MaxTransientRetriesPerInstance);
        }

        WorkflowBatchValidator.CaptureWorkflowFingerprint(_session.WorkflowState, _session.BatchDefinition);

        _picker?.RefreshDropdownChoices();
        if (_picker?.Dropdown != null && !string.IsNullOrEmpty(_session.BatchDefinition.WorkflowLibraryFileName))
            _picker.Dropdown.SetValueWithoutNotify(_session.BatchDefinition.WorkflowLibraryFileName);

        RefreshWorkflowSummary();
        AtlasLogger.LogFile($"Batch draft loaded: {path}");
    }

    private static string SanitizeDraftFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private void OnImportClicked()
    {
        string path = EditorUtility.OpenFilePanel("Import Workflow", "", "json");
        if (string.IsNullOrEmpty(path))
            return;

        string savedPath = WorkflowManager.SaveWorkflowToLibrary(path);
        if (savedPath == null)
        {
            EditorUtility.DisplayDialog("Error", "Failed to save workflow to library.", "OK");
            return;
        }

        _session.StateController.LoadWorkflowFromFile(savedPath);
        _picker?.RefreshDropdownChoices();
        _picker?.Dropdown?.SetValueWithoutNotify(Path.GetFileName(savedPath));
        _session.ResetBatchAfterWorkflowChanged();
        _session.BatchDefinition.WorkflowLibraryFileName = Path.GetFileName(savedPath);
        RefreshWorkflowSummary();
    }

    private void OnDeleteClicked()
    {
        if (_picker?.Dropdown == null)
            return;

        string selected = _picker.Dropdown.value;
        if (string.IsNullOrEmpty(selected) ||
            selected == "Select a workflow..." ||
            selected == "No workflows - click Import")
        {
            EditorUtility.DisplayDialog("No Workflow Selected", "Please select a workflow to delete.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Delete Workflow",
                $"Are you sure you want to delete '{selected}'?\n\nThis action cannot be undone.",
                "Delete",
                "Cancel"))
            return;

        if (!WorkflowManager.DeleteWorkflowFromLibrary(selected))
        {
            EditorUtility.DisplayDialog("Error", $"Failed to delete workflow '{selected}'.", "OK");
            return;
        }

        _session.StateController.ClearState();
        _session.ResetBatchAfterWorkflowChanged();
        _picker.RefreshDropdownChoices();
        RefreshWorkflowSummary();
    }

    private void OnLibrarySelectionChanged(ChangeEvent<string> evt)
    {
        if (string.IsNullOrEmpty(evt.newValue) ||
            evt.newValue == "Select a workflow..." ||
            evt.newValue == "No workflows - click Import")
            return;

        string filePath = Path.Combine(WorkflowManager.GetLibraryDirectory(), evt.newValue);
        _session.StateController.LoadWorkflowFromFile(filePath);
        _session.ResetBatchAfterWorkflowChanged();
        _session.BatchDefinition.WorkflowLibraryFileName = evt.newValue;
        RefreshWorkflowSummary();
    }

    private void OnOpenWorkflowJobsClicked()
    {
        AtlasWorkflowJobsWindow.ShowWindow();
    }
}
