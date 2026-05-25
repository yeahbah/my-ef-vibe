using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace MyEfVibe.VisualStudio.Commands;

internal static class EfvibeCommands
{
    private static readonly Guid CommandSet = new(PackageGuids.CommandSetString);

    internal static async Task InitializeAsync(MyEfVibePackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceInternalAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
            return;

        var controller = new EfvibeCommandController(package);

        Add(package, commandService, CommandIds.StartRepl, () => controller.StartReplAsync());
        Add(package, commandService, CommandIds.RunSelection, () => controller.RunSelectionAsync(withPlan: false));
        Add(package, commandService, CommandIds.ShowDbInfo, () => controller.ShowDbInfoAsync());
        Add(package, commandService, CommandIds.ShowTables, () => controller.ShowTablesAsync());
        Add(package, commandService, CommandIds.DescribeEntity, () => controller.DescribeEntityAsync());
        Add(package, commandService, CommandIds.ScanLite, () => controller.ScanAsync("lite"));
        Add(package, commandService, CommandIds.ScanDeep, () => controller.ScanAsync("deep"));
        Add(package, commandService, CommandIds.CheckPrerequisites, () => controller.CheckPrerequisitesAsync());
    }

    private static void Add(
        MyEfVibePackage package,
        OleMenuCommandService commandService,
        int commandId,
        Func<Task> handler)
    {
        var menuCommandId = new CommandID(CommandSet, commandId);
        var command = new OleMenuCommand(
            (_, _) => package.JoinableTaskFactory.RunAsync(handler).FileAndForget("MyEfVibe/Command"),
            menuCommandId);
        commandService.AddCommand(command);
    }
}
