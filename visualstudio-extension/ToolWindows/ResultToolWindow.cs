using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace MyEfVibe.VisualStudio.ToolWindows;

[Guid(PackageGuids.ResultToolWindowString)]
public sealed class ResultToolWindow : ToolWindowPane
{
    private EfvibeToolWindowControl? _control;

    public ResultToolWindow() : base(null) => Caption = "My EF Vibe";

    internal EfvibeToolWindowControl Panel =>
        _control ??= new EfvibeToolWindowControl((MyEfVibePackage)Package);

    protected override void Initialize()
    {
        base.Initialize();
        Content = Panel;
    }

    internal void SetExpression(string expression) => Panel.SetExpression(expression);

    internal Task EvaluateExpressionAsync(string expression, bool withPlan) =>
        Panel.EvaluateExpressionAsync(expression, withPlan);

    internal Task RunDbInfoAsync() => Panel.RunDbInfoAsync();

    internal Task RunTablesAsync() => Panel.RunTablesAsync();

    internal Task RunScanAsync(string mode) => Panel.RunScanAsync(mode);

    internal void RefreshConnection() => Panel.RefreshConnection();

    internal void AppendOutput(string title, string text) => Panel.AppendOutput(title, text);
}
