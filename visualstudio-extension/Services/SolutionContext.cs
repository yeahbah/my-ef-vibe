using System;
using System.IO;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace MyEfVibe.VisualStudio.Services;

internal sealed class SolutionContext
{
    public SolutionContext(string solutionDirectory)
    {
        SolutionDirectory = solutionDirectory;
    }

    public string SolutionDirectory { get; }

    public static SolutionContext FromDte(DTE2? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var solutionPath = dte?.Solution?.FullName;

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var directory = Path.GetDirectoryName(solutionPath);

            if (!string.IsNullOrWhiteSpace(directory))
                return new SolutionContext(directory);
        }

        return new SolutionContext(Environment.CurrentDirectory);
    }
}
