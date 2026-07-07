using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using MyEfVibe.VisualStudio.Models;
using MyEfVibe.VisualStudio.Services;

namespace MyEfVibe.VisualStudio.ToolWindows;

internal sealed class EfvibeToolWindowControl : UserControl
{
    private readonly MyEfVibePackage _package;
    private readonly TextBox _expressionBox;
    private readonly TextBlock _status;
    private readonly TabControl _tabs;
    private readonly DataGrid _resultGrid;
    private readonly TextBox _sqlBox;
    private readonly TextBox _planBox;
    private readonly TextBox _messagesBox;
    private readonly TextBox _sessionBox;
    private readonly DataGrid _modelGrid;
    private readonly TextBox _modelBox;
    private readonly TextBox _scanBox;
    private readonly TextBox _historyBox;
    private readonly TextBox _notebookCells;
    private readonly TextBox _notebookOutput;

    private EvaluationJsonPayload? _lastPayload;
    private ScanCiOutputDocument? _lastScan;
    private int _scanIndex;

    internal EfvibeToolWindowControl(MyEfVibePackage package)
    {
        _package = package;

        var root = new DockPanel { LastChildFill = true, Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)) };

        _expressionBox = CreateEditorBox("db.Set<YourEntity>().Take(10)");
        _status = new TextBlock
        {
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(236, 253, 245)),
            Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87)),
            FontWeight = FontWeights.SemiBold,
            Text = "Ready",
        };

        var topToolbar = CreateToolbar(
            ("Run", () => _ = RunCurrentExpressionAsync(withPlan: false)),
            ("Run Plan", () => _ = RunCurrentExpressionAsync(withPlan: true)),
            ("Scan Lite", () => _ = RunScanAsync("lite")),
            ("Scan Deep", () => _ = RunScanAsync("deep")),
            ("Copy Tab", CopyActiveTab));

        var top = new DockPanel();
        DockPanel.SetDock(topToolbar, Dock.Top);
        top.Children.Add(topToolbar);
        top.Children.Add(_expressionBox);
        DockPanel.SetDock(top, Dock.Top);

        _tabs = new TabControl { Margin = new Thickness(0, 4, 0, 0) };
        _resultGrid = CreateGrid();
        _sqlBox = CreateReadOnlyBox();
        _planBox = CreateReadOnlyBox();
        _messagesBox = CreateReadOnlyBox();
        _sessionBox = CreateReadOnlyBox();
        _modelGrid = CreateGrid();
        _modelBox = CreateReadOnlyBox();
        _scanBox = CreateReadOnlyBox();
        _historyBox = CreateReadOnlyBox();
        _notebookCells = CreateEditorBox("db.Products.Take(10)\n\n---\n:dbinfo");
        _notebookOutput = CreateReadOnlyBox();

        _tabs.Items.Add(CreateTab("Result", CreateResultPanel()));
        _tabs.Items.Add(CreateTab("SQL", _sqlBox));
        _tabs.Items.Add(CreateTab("Plan", _planBox));
        _tabs.Items.Add(CreateTab("Messages", _messagesBox));
        _tabs.Items.Add(CreateTab("Session", _sessionBox));
        _tabs.Items.Add(CreateTab("Model", CreateModelPanel()));
        _tabs.Items.Add(CreateTab("Scan Review", CreateScanPanel()));
        _tabs.Items.Add(CreateTab("History", _historyBox));
        _tabs.Items.Add(CreateTab("Notebook", CreateNotebookPanel()));

        var split = new Grid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(top, 0);
        Grid.SetRow(_tabs, 1);
        split.Children.Add(top);
        split.Children.Add(_tabs);

        root.Children.Add(split);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        Content = root;
        RenderSession();
    }

    internal void SetExpression(string expression) => _expressionBox.Text = expression;

    internal void AppendOutput(string title, string text)
    {
        _messagesBox.Text = AppendBlock(_messagesBox.Text, title, text);
        SelectTab("Messages");
    }

    internal async Task EvaluateExpressionAsync(string expression, bool withPlan)
    {
        if (!ExpressionGuard.IsReadOnly(expression, out var reason))
        {
            AppendOutput("Blocked", reason);
            return;
        }

        try
        {
            var workspace = await EfvibeWorkspace.ResolveAsync(_package);
            var daemon = EfvibeDaemonClient.GetOrCreate(workspace);
            SetBusy(withPlan
                ? daemon.IsReady() ? "Running query plan (daemon)..." : "Starting efvibe daemon..."
                : daemon.IsReady() ? "Running query (daemon)..." : "Starting efvibe daemon...");

            var run = await workspace.Runner.RunExpressionPayloadAsync(
                workspace,
                expression,
                withPlan,
                preferDaemon: true,
                _package.DisposalToken);

            if (!run.UsedDaemon && !string.IsNullOrWhiteSpace(run.DaemonError))
            {
                AppendOutput(
                    "Daemon unavailable",
                    "efvibe serve was not used; fell back to one-shot CLI." + Environment.NewLine + run.DaemonError);
            }

            if (run.Payload is not null)
            {
                workspace.Session.RecordEvaluation(expression, run.Payload);
                RenderEvaluation(expression, run.Payload, withPlan);
                SetReady(run.UsedDaemon ? "Ready (daemon)" : "Ready (CLI)");
            }
            else
            {
                AppendOutput(
                    "Evaluation failed",
                    run.Result.Stderr.IfBlank(run.Result.Stdout.IfBlank("No JSON payload returned.")));
                SetReady();
            }
        }
        catch (Exception ex)
        {
            AppendOutput("Error", ex.Message);
            SetReady();
        }
    }

    internal async Task RunDbInfoAsync() => await RunPayloadAsync(
        "Loading DbInfo...",
        async workspace =>
        {
            var result = await workspace.Runner.RunDbInfoJsonAsync(workspace.Settings, _package.DisposalToken);
            return (result, JsonLineParser.ParseFirstJsonLine<DbInfoJsonPayload>(result.Stdout), "DbInfo");
        },
        (payload) =>
        {
            _modelBox.Text = FormatDbInfo(payload);
            SelectTab("Model");
        });

    internal async Task RunTablesAsync() => await RunPayloadAsync(
        "Loading tables...",
        async workspace =>
        {
            var result = await workspace.Runner.RunTablesJsonAsync(workspace.Settings, _package.DisposalToken);
            return (result, JsonLineParser.ParseFirstJsonLine<TablesJsonPayload>(result.Stdout), "Tables");
        },
        RenderTables);

    internal async Task RunScanAsync(string mode) => await RunPayloadAsync(
        $"Running scan {mode}...",
        async workspace =>
        {
            var result = await workspace.Runner.RunScanAsync(workspace.Settings, mode, _package.DisposalToken);
            return (result, JsonLineParser.ParseFirstJsonLine<ScanCiOutputDocument>(result.Stdout), $"Scan {mode}");
        },
        document => RenderScan(document));

    internal void RefreshConnection()
    {
        EfvibeDaemonClient.InvalidateAll();
        RenderSession();
        AppendOutput("Refresh Connection", "Connection refreshed. The next query will start a new efvibe session.");
    }

    private FrameworkElement CreateResultPanel()
    {
        var panel = new DockPanel();
        var toolbar = CreateToolbar(
            ("Export CSV", () => ExportLast("csv")),
            ("Export JSON", () => ExportLast("json")));
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(_resultGrid);
        return panel;
    }

    private FrameworkElement CreateModelPanel()
    {
        var panel = new DockPanel();
        var toolbar = CreateToolbar(
            ("Db Info", () => _ = RunDbInfoAsync()),
            ("Tables", () => _ = RunTablesAsync()),
            ("Run Count", () => SelectedDbSet(expression => _ = EvaluateExpressionAsync($"db.{expression}.Count()", false))),
            ("Run Sample", () => SelectedDbSet(expression => _ = EvaluateExpressionAsync($"db.{expression}.Take(10)", false))),
            ("Describe", () => SelectedDbSet(expression => _ = RunDescribeAsync(expression))));
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);

        var split = new Grid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_modelGrid, 0);
        Grid.SetRow(_modelBox, 1);
        split.Children.Add(_modelGrid);
        split.Children.Add(_modelBox);
        panel.Children.Add(split);
        _modelGrid.SelectionChanged += (_, _) => RenderSelectedModelRow();
        return panel;
    }

    private FrameworkElement CreateScanPanel()
    {
        var panel = new DockPanel();
        var toolbar = CreateToolbar(
            ("Previous", () => MoveScan(-1)),
            ("Next", () => MoveScan(1)),
            ("Go to code", OpenSelectedScanSource),
            ("Note", () => _ = SaveScanNoteAsync()),
            ("Dismiss", () => _ = DismissScanFindingAsync()),
            ("Copy Finding", () => Clipboard.SetText(_scanBox.Text)));
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(_scanBox);
        return panel;
    }

    private FrameworkElement CreateNotebookPanel()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = CreateToolbar(
            ("Open", OpenNotebook),
            ("Save", SaveNotebook),
            ("Run All", () => _ = RunNotebookAsync()));
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_notebookCells, 1);
        Grid.SetRow(_notebookOutput, 2);
        panel.Children.Add(toolbar);
        panel.Children.Add(_notebookCells);
        panel.Children.Add(_notebookOutput);
        return panel;
    }

    private async Task RunCurrentExpressionAsync(bool withPlan)
    {
        var source = _expressionBox.Text.Trim();
        if (source.Length == 0)
        {
            AppendOutput("My EF Vibe", "Enter a LINQ expression first.");
            return;
        }

        await EvaluateExpressionAsync(source, withPlan);
    }

    private async Task RunPayloadAsync<T>(
        string busyText,
        Func<EfvibeWorkspace.WorkspaceContext, Task<(CliRunResult Result, T? Payload, string Title)>> action,
        Action<T> onSuccess)
        where T : class
    {
        try
        {
            SetBusy(busyText);
            var workspace = await EfvibeWorkspace.ResolveAsync(_package);
            var (result, payload, title) = await action(workspace);

            if (payload is not null)
                onSuccess(payload);
            else
                AppendOutput(title, result.Stderr.IfBlank(result.Stdout));

            SetReady();
        }
        catch (Exception ex)
        {
            AppendOutput("Error", ex.Message);
            SetReady();
        }
    }

    private void RenderEvaluation(string source, EvaluationJsonPayload payload, bool showPlan)
    {
        _lastPayload = payload;
        _expressionBox.Text = source;
        RenderRows(payload);
        _sqlBox.Text = payload.Sql?.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine, payload.Sql)
            : payload.TranslatedSql.IfBlank("No SQL captured for this run.");
        _planBox.Text = payload.QueryPlan.IfBlank(payload.QueryPlanNote.IfBlank("No query plan captured for this run."));
        _messagesBox.Text = FormatMessages(payload);
        RenderHistory();
        SelectTab(showPlan ? "Plan" : "Result");
    }

    private void RenderRows(EvaluationJsonPayload payload)
    {
        var rows = payload.Rows?.Count > 0
            ? payload.Rows
            : new List<Dictionary<string, string>> { new() { ["value"] = payload.Value ?? string.Empty } };

        var columns = rows.SelectMany(row => row.Keys).Distinct().ToList();
        if (columns.Count == 0)
            columns.Add("value");

        var table = new DataTable();
        foreach (var column in columns)
            table.Columns.Add(column);

        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            foreach (var column in columns)
                dataRow[column] = row.TryGetValue(column, out var value) ? value : string.Empty;
            table.Rows.Add(dataRow);
        }

        _resultGrid.ItemsSource = table.DefaultView;
    }

    private void RenderTables(TablesJsonPayload payload)
    {
        var table = new DataTable();
        table.Columns.Add("DbSet");
        table.Columns.Add("Entity");
        table.Columns.Add("Full Type");
        foreach (var entry in payload.Tables ?? Enumerable.Empty<TablesJsonEntry>())
            table.Rows.Add(entry.DbSet, entry.EntityType, entry.EntityTypeFullName);

        _modelGrid.ItemsSource = table.DefaultView;
        _modelBox.Text = payload.Tables?.Count > 0
            ? "Select a DbSet above, then use Run Count, Run Sample, or Describe."
            : $"No DbSets returned for {payload.DbContext}.";
        if (table.Rows.Count > 0)
            _modelGrid.SelectedIndex = 0;
        SelectTab("Model");
    }

    private void RenderSelectedModelRow()
    {
        if (_modelGrid.SelectedItem is not DataRowView row)
            return;

        var dbSet = row["DbSet"]?.ToString() ?? string.Empty;
        var entity = row["Entity"]?.ToString() ?? string.Empty;
        var fullType = row["Full Type"]?.ToString() ?? string.Empty;
        _modelBox.Text =
            $"DbSet: {dbSet}{Environment.NewLine}Entity: {entity}{Environment.NewLine}Full type: {fullType}{Environment.NewLine}{Environment.NewLine}" +
            $"Actions:{Environment.NewLine}- Run Count: db.{dbSet}.Count(){Environment.NewLine}- Run Sample: db.{dbSet}.Take(10)";
    }

    private async Task RunDescribeAsync(string dbSet)
    {
        await RunPayloadAsync(
            $"Describing {dbSet}...",
            async workspace =>
            {
                var result = await workspace.Runner.RunDescribeJsonAsync(
                    workspace.Settings,
                    dbSet,
                    _package.DisposalToken);
                return (result, JsonLineParser.ParseFirstJsonLine<DescribeJsonPayload>(result.Stdout), "Describe");
            },
            payload => _modelBox.Text = FormatDescribe(payload));
    }

    private void RenderScan(ScanCiOutputDocument payload, int selectedIndex = 0)
    {
        _lastScan = payload;
        _scanIndex = payload.Findings?.Count > 0
            ? Math.Max(0, Math.Min(selectedIndex, payload.Findings.Count - 1))
            : 0;
        RenderSelectedScanFinding();
        SelectTab("Scan Review");
    }

    private void RenderSelectedScanFinding()
    {
        var finding = _lastScan?.Findings?.ElementAtOrDefault(_scanIndex);
        _scanBox.Text = finding is null ? "No scan findings." : FormatFinding(finding, _scanIndex + 1, _lastScan!.Findings!.Count);
    }

    private void MoveScan(int delta)
    {
        var count = _lastScan?.Findings?.Count ?? 0;
        if (count == 0)
            return;

        _scanIndex = (_scanIndex + delta + count) % count;
        RenderSelectedScanFinding();
    }

    private async Task SaveScanNoteAsync()
    {
        var finding = SelectedFinding();
        if (finding is null)
            return;

        var note = InputDialog.Prompt("Save Finding Note", $"Note for {finding.RuleId}:");
        if (string.IsNullOrWhiteSpace(note))
            return;

        var workspace = await EfvibeWorkspace.ResolveAsync(_package);
        SetBusy("Saving note...");
        var result = await workspace.Runner.RunScanNoteAsync(workspace.Settings, finding, note.Trim(), _package.DisposalToken);
        if (result.Succeeded)
        {
            finding.SavedNote = note.Trim();
            RenderSelectedScanFinding();
            _status.Text = "Note saved.";
        }
        else
        {
            AppendOutput("Save note failed", result.Stderr.IfBlank(result.Stdout));
        }

        SetReady();
    }

    private async Task DismissScanFindingAsync()
    {
        var finding = SelectedFinding();
        if (finding is null)
            return;

        var note = InputDialog.Prompt("Dismiss Finding", "Optional dismissal note:");
        var workspace = await EfvibeWorkspace.ResolveAsync(_package);
        SetBusy("Dismissing finding...");
        var result = await workspace.Runner.RunScanDismissAsync(
            workspace.Settings,
            finding,
            note,
            _package.DisposalToken);

        if (result.Succeeded && _lastScan?.Findings is not null)
        {
            var updated = _lastScan.Findings.ToList();
            updated.RemoveAt(_scanIndex);
            _lastScan.Findings = updated;
            _lastScan.TotalFindings = updated.Count;
            RenderScan(_lastScan, Math.Min(_scanIndex, Math.Max(0, updated.Count - 1)));
            _status.Text = "Finding dismissed.";
        }
        else
        {
            AppendOutput("Dismiss failed", result.Stderr.IfBlank(result.Stdout));
        }

        SetReady();
    }

    private ScanFinding? SelectedFinding() =>
        _lastScan?.Findings?.ElementAtOrDefault(_scanIndex);

    private void OpenSelectedScanSource()
    {
        var finding = SelectedFinding();
        if (finding?.FilePath is null)
            return;

        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceInternalAsync(typeof(DTE)) as DTE2;
            if (dte?.ItemOperations?.OpenFile(finding.FilePath, "Text") is Document document)
            {
                if (document.Selection is TextSelection selection)
                    selection.GotoLine(finding.Line, false);
            }
        });
    }

    private void SelectedDbSet(Action<string> action)
    {
        if (_modelGrid.SelectedItem is not DataRowView row)
        {
            AppendOutput("Model", "Select a DbSet in the Model tab first.");
            return;
        }

        var dbSet = row["DbSet"]?.ToString();
        if (string.IsNullOrWhiteSpace(dbSet))
            return;

        action(dbSet);
    }

    private async Task RunNotebookAsync()
    {
        var cells = SplitNotebookCells(_notebookCells.Text);
        if (cells.Count == 0)
            return;

        SetBusy("Running notebook cells...");
        var workspace = await EfvibeWorkspace.ResolveAsync(_package);
        var output = new StringBuilder();
        foreach (var (cell, index) in cells.Select((cell, index) => (cell, index)))
        {
            output.AppendLine($"## Cell {index + 1}");
            if (string.Equals(cell, ":dbinfo", StringComparison.OrdinalIgnoreCase))
            {
                var result = await workspace.Runner.RunDbInfoJsonAsync(workspace.Settings, _package.DisposalToken);
                var payload = JsonLineParser.ParseFirstJsonLine<DbInfoJsonPayload>(result.Stdout);
                output.AppendLine(payload is null ? result.Stdout : FormatDbInfo(payload));
            }
            else if (string.Equals(cell, ":tables", StringComparison.OrdinalIgnoreCase))
            {
                var result = await workspace.Runner.RunTablesJsonAsync(workspace.Settings, _package.DisposalToken);
                var payload = JsonLineParser.ParseFirstJsonLine<TablesJsonPayload>(result.Stdout);
                output.AppendLine(payload is null ? result.Stdout : FormatTables(payload));
            }
            else
            {
                if (!ExpressionGuard.IsReadOnly(cell, out var reason))
                {
                    output.AppendLine($"Blocked: {reason}");
                    break;
                }

                var run = await workspace.Runner.RunExpressionPayloadAsync(
                    workspace,
                    cell,
                    withPlan: false,
                    preferDaemon: true,
                    _package.DisposalToken);
                output.AppendLine(run.Payload is null
                    ? run.Result.Stderr.IfBlank(run.Result.Stdout)
                    : FormatEvaluationSummary(run.Payload));
            }

            output.AppendLine();
        }

        _notebookOutput.Text = output.ToString();
        SelectTab("Notebook");
        SetReady();
    }

    private void OpenNotebook()
    {
        var dialog = new OpenFileDialog { Filter = "efvibe notebook|*.efvibe-notebook|All files|*.*" };
        if (dialog.ShowDialog() != true)
            return;

        _notebookCells.Text = File.ReadAllText(dialog.FileName);
        SelectTab("Notebook");
    }

    private void SaveNotebook()
    {
        var dialog = new SaveFileDialog
        {
            FileName = "myefvibe.efvibe-notebook",
            Filter = "efvibe notebook|*.efvibe-notebook|All files|*.*",
        };
        if (dialog.ShowDialog() != true)
            return;

        var cells = SplitNotebookCells(_notebookCells.Text);
        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"cells\": [");
        for (var index = 0; index < cells.Count; index++)
        {
            json.Append("    { \"kind\": \"code\", \"languageId\": \"csharp\", \"value\": \"");
            json.Append(JsonEscape(cells[index]));
            json.Append("\" }");
            if (index < cells.Count - 1)
                json.Append(',');
            json.AppendLine();
        }

        json.AppendLine("  ]");
        json.AppendLine("}");
        File.WriteAllText(dialog.FileName, json.ToString());
        _status.Text = $"Saved notebook {dialog.FileName}";
    }

    private void ExportLast(string format)
    {
        if (_lastPayload is null)
        {
            AppendOutput("Export", "Run a successful query before exporting.");
            return;
        }

        var content = format == "json" ? BuildExportJson(_lastPayload) : BuildExportCsv(_lastPayload);
        var dialog = new SaveFileDialog { FileName = $"myefvibe-export-{DateTime.Now:yyyyMMdd-HHmmss}.{format}" };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, content);
            _status.Text = $"Exported {dialog.FileName}";
        }
    }

    private void CopyActiveTab()
    {
        var title = (_tabs.SelectedItem as TabItem)?.Header?.ToString() ?? "Result";
        var text = title switch
        {
            "SQL" => _sqlBox.Text,
            "Plan" => _planBox.Text,
            "Messages" => _messagesBox.Text,
            "Session" => _sessionBox.Text,
            "Model" => _modelBox.Text,
            "Scan Review" => _scanBox.Text,
            "History" => _historyBox.Text,
            "Notebook" => _notebookOutput.Text,
            _ => GridToText(_resultGrid),
        };

        Clipboard.SetText(text);
        _status.Text = $"Copied {title} to clipboard.";
    }

    private void RenderHistory()
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            var workspace = await EfvibeWorkspace.ResolveAsync(_package);
            _historyBox.Text = string.Join(
                Environment.NewLine + Environment.NewLine,
                workspace.Session.History.Select(entry =>
                    $"{entry.Expression}{Environment.NewLine}{FormatEvaluationSummary(entry.Payload)}"));
        });
    }

    private void RenderSession()
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            try
            {
                var workspace = await EfvibeWorkspace.ResolveAsync(_package);
                var settings = workspace.Settings;
                var daemon = EfvibeDaemonClient.GetOrCreate(workspace);
                _sessionBox.Text =
                    $"EF project: {settings.Project}{Environment.NewLine}" +
                    $"Startup project: {settings.StartupProject.IfBlank("(auto)")}{Environment.NewLine}" +
                    $"DbContext: {settings.Context.IfBlank("(auto)")}{Environment.NewLine}" +
                    $"Workspace root: {settings.WorkspaceRoot.IfBlank("(default)")}{Environment.NewLine}" +
                    $"Tool path: {settings.ToolPath.IfBlank("(PATH/local tool)")}{Environment.NewLine}" +
                    $"Framework: {settings.DotnetFramework.IfBlank("(default)")}{Environment.NewLine}" +
                    Environment.NewLine +
                    "Resolved REPL command:" + Environment.NewLine +
                    workspace.Runner.BuildReplCommandLine(settings) + Environment.NewLine +
                    Environment.NewLine +
                    "Daemon:" + Environment.NewLine +
                    (daemon.IsReady()
                        ? "Running (efvibe serve)"
                        : "Not started — Run will start efvibe serve and reuse the loaded workspace.") +
                    Environment.NewLine +
                    "Serve command:" + Environment.NewLine +
                    FormatServeCommand(workspace.Runner.BuildServeSpec(settings));
            }
            catch (Exception ex)
            {
                _sessionBox.Text = ex.Message;
            }
        });
    }

    private static StackPanel CreateToolbar(params (string Label, Action Action)[] actions)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
        };

        foreach (var (label, action) in actions)
        {
            var button = new Button
            {
                Content = label,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 4, 10, 4),
            };
            button.Click += (_, _) => action();
            panel.Children.Add(button);
        }

        return panel;
    }

    private static TabItem CreateTab(string title, object content) =>
        new() { Header = title, Content = content };

    private static TextBox CreateEditorBox(string initialText) =>
        new()
        {
            Text = initialText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(8),
            MinHeight = 90,
        };

    private static TextBox CreateReadOnlyBox() =>
        new()
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(8),
        };

    private static DataGrid CreateGrid() =>
        new()
        {
            IsReadOnly = true,
            AutoGenerateColumns = true,
            Margin = new Thickness(8),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        };

    private void SelectTab(string title)
    {
        foreach (var item in _tabs.Items.OfType<TabItem>())
        {
            if (string.Equals(item.Header?.ToString(), title, StringComparison.Ordinal))
            {
                _tabs.SelectedItem = item;
                break;
            }
        }
    }

    private void SetBusy(string text) => _status.Text = text;
    private void SetReady(string message = "Ready") => _status.Text = message;

    private static string AppendBlock(string current, string title, string text)
    {
        if (string.IsNullOrWhiteSpace(current))
            return $"## {title}{Environment.NewLine}{text}";

        return $"{current}{Environment.NewLine}{Environment.NewLine}## {title}{Environment.NewLine}{text}";
    }

    private static string FormatMessages(EvaluationJsonPayload payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine(payload.Success ? "Success" : "Failed");
        builder.AppendLine($"Total: {payload.Metrics?.TotalMs ?? 0} ms");
        if (payload.Metrics?.DatabaseMs is not null)
            builder.AppendLine($"Database: {payload.Metrics.DatabaseMs} ms");
        if (payload.Metrics?.RowCount is not null)
            builder.AppendLine($"Rows: {payload.Metrics.RowCount}");
        builder.AppendLine($"SQL commands: {payload.Metrics?.SqlCommandCount ?? 0}");
        if (!string.IsNullOrWhiteSpace(payload.Metrics?.ResultKind))
            builder.AppendLine($"Result kind: {payload.Metrics.ResultKind}");
        if (!string.IsNullOrWhiteSpace(payload.Error))
        {
            builder.AppendLine();
            builder.AppendLine(payload.Error);
        }

        if (payload.Warnings?.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            foreach (var warning in payload.Warnings)
                builder.AppendLine("- " + warning);
        }

        return builder.ToString();
    }

    private static string FormatDbInfo(DbInfoJsonPayload payload) =>
        $"DbContext: {payload.DbContext}{Environment.NewLine}" +
        string.Join(Environment.NewLine, payload.Entries?.Select(entry => $"{entry.Key}: {entry.Value}") ?? Enumerable.Empty<string>());

    private static string FormatTables(TablesJsonPayload payload) =>
        $"DbContext: {payload.DbContext}{Environment.NewLine}" +
        string.Join(Environment.NewLine, payload.Tables?.Select(table => $"{table.DbSet} -> {table.EntityType}") ?? Enumerable.Empty<string>());

    private static string FormatDescribe(DescribeJsonPayload payload)
    {
        if (!payload.Success)
            return payload.Error ?? "Describe failed.";

        var builder = new StringBuilder();
        builder.AppendLine($"{payload.DbSet ?? payload.EntityType}: {payload.EntityTypeFullName}");
        foreach (var member in payload.Members ?? Enumerable.Empty<DescribeJsonMember>())
            builder.AppendLine($"{member.Name}: {member.Type} nullable={member.Nullable} {member.Notes}");
        return builder.ToString();
    }

    private static string FormatFinding(ScanFinding finding, int index, int total)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Finding {index} of {total}");
        builder.AppendLine($"{finding.Severity?.ToUpperInvariant()} {finding.RuleId}");
        builder.AppendLine($"{finding.FilePath}:{finding.Line}");
        builder.AppendLine();
        builder.AppendLine(finding.Message);
        if (!string.IsNullOrWhiteSpace(finding.Recommendation))
        {
            builder.AppendLine();
            builder.AppendLine("Recommendation:");
            builder.AppendLine(finding.Recommendation);
        }

        if (!string.IsNullOrWhiteSpace(finding.Code))
        {
            builder.AppendLine();
            builder.AppendLine("Code:");
            builder.AppendLine(finding.Code);
        }

        return builder.ToString();
    }

    private static string FormatEvaluationSummary(EvaluationJsonPayload payload)
    {
        var builder = new StringBuilder();
        builder.Append(payload.Success ? "Success" : "Failed");
        builder.Append($" · {payload.Metrics?.TotalMs ?? 0} ms");
        if (payload.Metrics?.RowCount is not null)
            builder.Append($" · {payload.Metrics.RowCount} row(s)");
        if (!string.IsNullOrWhiteSpace(payload.Error))
            builder.AppendLine().Append(payload.Error);
        return builder.ToString();
    }

    private static string FormatServeCommand(CliInvocationSpec spec) =>
        Quote(spec.Command) + (spec.Args.Length == 0 ? string.Empty : " " + CliRunner.BuildArguments(spec.Args));

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? value : "\"" + value.Replace("\"", "\\\"") + "\"";

    private static List<string> SplitNotebookCells(string text) =>
        Regex.Split(text, @"(?m)^\s*---\s*$")
            .Select(cell => cell.Trim())
            .Where(cell => cell.Length > 0)
            .ToList();

    private static string BuildExportCsv(EvaluationJsonPayload payload)
    {
        var rows = payload.Rows?.Count > 0
            ? payload.Rows
            : new List<Dictionary<string, string>> { new() { ["value"] = payload.Value ?? string.Empty } };
        var columns = rows.SelectMany(row => row.Keys).Distinct().ToList();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columns.Select(CsvEscape)));
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", columns.Select(column => CsvEscape(row.TryGetValue(column, out var value) ? value : string.Empty))));
        return builder.ToString();
    }

    private static string BuildExportJson(EvaluationJsonPayload payload)
    {
        var rows = payload.Rows?.Count > 0
            ? payload.Rows
            : new List<Dictionary<string, string>> { new() { ["value"] = payload.Value ?? string.Empty } };
        var builder = new StringBuilder();
        builder.AppendLine("[");
        for (var index = 0; index < rows.Count; index++)
        {
            builder.Append("  {");
            builder.Append(string.Join(", ", rows[index].Select(entry => $"\"{JsonEscape(entry.Key)}\": \"{JsonEscape(entry.Value)}\"")));
            builder.Append('}');
            if (index < rows.Count - 1)
                builder.Append(',');
            builder.AppendLine();
        }

        builder.AppendLine("]");
        return builder.ToString();
    }

    private static string GridToText(DataGrid grid)
    {
        if (grid.ItemsSource is not DataView view)
            return string.Empty;

        var builder = new StringBuilder();
        var columns = view.Table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToList();
        builder.AppendLine(string.Join("\t", columns));
        foreach (DataRowView row in view)
            builder.AppendLine(string.Join("\t", columns.Select(column => row[column]?.ToString() ?? string.Empty)));
        return builder.ToString();
    }

    private static string CsvEscape(string value) =>
        value.Any(character => character is '"' or ',' or '\n' or '\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}

internal static class StringExtensions
{
    internal static string IfBlank(this string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
