using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.ToolWindows;

[Guid(PackageGuids.ResultToolWindowString)]
public sealed class ResultToolWindow : ToolWindowPane
{
    private readonly ResultToolWindowControl _control;

    public ResultToolWindow() : base(null)
    {
        Caption = "My EF Vibe Result";
        _control = new ResultToolWindowControl();
        Content = _control;
    }

    internal void SetRunner(Func<string, bool, Task> runner) =>
        _control.RunRequested = runner;

    internal void ShowEvaluation(string expression, EvaluationJsonPayload payload) =>
        _control.ShowEvaluation(expression, payload);

    internal void ShowText(string title, string content) =>
        _control.ShowText(title, content);
}
