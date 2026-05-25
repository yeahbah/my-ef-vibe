using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.ToolWindows;

public sealed class ResultToolWindowControl : UserControl
{
    private readonly TextBox _expressionBox;
    private readonly DataGrid _grid;
    private readonly TextBox _detailsBox;

    internal Func<string, bool, Task>? RunRequested { get; set; }

    public ResultToolWindowControl()
    {
        var root = new DockPanel { LastChildFill = true };

        var top = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
        _expressionBox = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 48,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var runButton = new Button { Content = "Run", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var planButton = new Button { Content = "Run :plan", MinWidth = 90 };
        runButton.Click += (_, _) => _ = RunAsync(withPlan: false);
        planButton.Click += (_, _) => _ = RunAsync(withPlan: true);
        buttons.Children.Add(runButton);
        buttons.Children.Add(planButton);

        top.Children.Add(_expressionBox);
        top.Children.Add(buttons);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var tabs = new TabControl();
        _grid = new DataGrid
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

        tabs.Items.Add(new TabItem { Header = "Rows", Content = _grid });
        tabs.Items.Add(new TabItem { Header = "Details", Content = _detailsBox });
        root.Children.Add(tabs);

        Content = root;
    }

    internal void ShowEvaluation(string expression, EvaluationJsonPayload payload)
    {
        _expressionBox.Text = expression;
        _grid.ItemsSource = payload.Rows;
        _detailsBox.Text = FormatEvaluation(payload);
    }

    internal void ShowText(string title, string content)
    {
        _expressionBox.Text = title;
        _grid.ItemsSource = null;
        _detailsBox.Text = content;
    }

    private async Task RunAsync(bool withPlan)
    {
        if (RunRequested is null)
            return;

        var expression = _expressionBox.Text.Trim();

        if (expression.Length == 0)
            return;

        await RunRequested(expression, withPlan);
    }

    private static string FormatEvaluation(EvaluationJsonPayload payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine(payload.Success ? "Success" : "Failed");

        if (!string.IsNullOrWhiteSpace(payload.Error))
            builder.AppendLine("Error: " + payload.Error);

        if (!string.IsNullOrWhiteSpace(payload.Value))
            builder.AppendLine("Value: " + payload.Value);

        if (payload.Metrics is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Metrics");
            builder.AppendLine($"Total: {payload.Metrics.TotalMs} ms");
            builder.AppendLine($"Database: {payload.Metrics.DatabaseMs?.ToString() ?? "n/a"} ms");
            builder.AppendLine($"Rows: {payload.Metrics.RowCount?.ToString() ?? "n/a"}");
            builder.AppendLine($"SQL commands: {payload.Metrics.SqlCommandCount}");
        }

        if (payload.Warnings?.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings");
            foreach (var warning in payload.Warnings)
                builder.AppendLine("- " + warning);
        }

        if (payload.Sql?.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("SQL");
            foreach (var sql in payload.Sql)
            {
                builder.AppendLine(sql);
                builder.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.TranslatedSql)
            && payload.Sql?.Contains(payload.TranslatedSql!) != true)
        {
            builder.AppendLine();
            builder.AppendLine("Translated SQL");
            builder.AppendLine(payload.TranslatedSql);
        }

        if (!string.IsNullOrWhiteSpace(payload.QueryPlan))
        {
            builder.AppendLine();
            builder.AppendLine("Query plan");
            builder.AppendLine(payload.QueryPlan);
        }
        else if (!string.IsNullOrWhiteSpace(payload.QueryPlanNote))
        {
            builder.AppendLine();
            builder.AppendLine("Query plan note");
            builder.AppendLine(payload.QueryPlanNote);
        }

        return builder.ToString();
    }
}
