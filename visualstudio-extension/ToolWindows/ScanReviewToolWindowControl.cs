using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.ToolWindows;

public sealed class ScanReviewToolWindowControl : UserControl
{
    private readonly TextBlock _summary;
    private readonly DataGrid _findingsGrid;
    private readonly TextBox _detailsBox;

    public ScanReviewToolWindowControl()
    {
        var root = new DockPanel { LastChildFill = true };

        _summary = new TextBlock
        {
            Margin = new Thickness(8),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        var tabs = new TabControl();
        _findingsGrid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = true,
            Margin = new Thickness(8),
        };
        _detailsBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8),
        };

        tabs.Items.Add(new TabItem { Header = "Findings", Content = _findingsGrid });
        tabs.Items.Add(new TabItem { Header = "Details", Content = _detailsBox });
        root.Children.Add(tabs);

        Content = root;
    }

    internal void ShowScan(ScanCiOutputDocument document)
    {
        _summary.Text =
            $"scan {document.ScanMode}: {document.TotalFindings} finding(s), "
            + $"{document.FilesScanned} file(s), {document.ProjectsScanned} project(s). "
            + $"Saved: {document.SavedPath}";

        _findingsGrid.ItemsSource = document.Findings;
        _detailsBox.Text = FormatDetails(document);
    }

    private static string FormatDetails(ScanCiOutputDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mode: {document.ScanMode}");
        builder.AppendLine($"Saved path: {document.SavedPath}");
        builder.AppendLine($"Findings: {document.TotalFindings}");
        builder.AppendLine($"Severity: {document.CriticalCount} critical, {document.ErrorCount} error, {document.WarningCount} warning, {document.InfoCount} info");

        if (document.QuerySitesVisited is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Deep scan");
            builder.AppendLine($"Query sites: {document.QuerySitesVisited}");
            builder.AppendLine($"SQL translated: {document.SqlTranslatedCount}");
            builder.AppendLine($"SQL failed: {document.SqlFailedCount}");
            builder.AppendLine($"Plans: {document.QueryPlanCount}");
            builder.AppendLine($"Plans failed: {document.QueryPlanFailedCount}");
        }

        foreach (var finding in document.Findings?.Take(50) ?? Enumerable.Empty<ScanFinding>())
        {
            builder.AppendLine();
            builder.AppendLine($"{finding.Severity} {finding.RuleId}: {finding.Message}");
            builder.AppendLine($"{finding.FilePath}:{finding.Line}");
            builder.AppendLine(finding.Code);

            if (!string.IsNullOrWhiteSpace(finding.Recommendation))
                builder.AppendLine("Recommendation: " + finding.Recommendation);

            if (!string.IsNullOrWhiteSpace(finding.SqlTranslationNote))
                builder.AppendLine("SQL note: " + finding.SqlTranslationNote);

            if (!string.IsNullOrWhiteSpace(finding.QueryPlanNote))
                builder.AppendLine("Plan note: " + finding.QueryPlanNote);
        }

        return builder.ToString();
    }
}
