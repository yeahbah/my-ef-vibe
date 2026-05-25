using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.ToolWindows;

[Guid(PackageGuids.ScanReviewToolWindowString)]
public sealed class ScanReviewToolWindow : ToolWindowPane
{
    private readonly ScanReviewToolWindowControl _control;

    public ScanReviewToolWindow() : base(null)
    {
        Caption = "My EF Vibe Scan Review";
        _control = new ScanReviewToolWindowControl();
        Content = _control;
    }

    internal void ShowScan(ScanCiOutputDocument document) =>
        _control.ShowScan(document);
}
