using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using AutoWire.VisualStudio.ToolWindow;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace AutoWire.VisualStudio.Commands
{
    /// <summary>Handles the "AutoWire &gt; Show Dependency Graph..." menu command by showing/creating <see cref="DependencyGraphToolWindow"/>.</summary>
    internal sealed class ShowDependencyGraphCommand
    {
        private readonly AsyncPackage _package;

        private ShowDependencyGraphCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var commandId = new CommandID(PackageGuids.AutoWireCommandSet, PackageIds.ShowDependencyGraphCommand);
            var menuItem = new MenuCommand(Execute, commandId);
            commandService.AddCommand(menuItem);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService is null) return;

            _ = new ShowDependencyGraphCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

                var window = await _package.ShowToolWindowAsync(
                    typeof(DependencyGraphToolWindow),
                    id: 0,
                    create: true,
                    cancellationToken: _package.DisposalToken);

                if (window?.Frame is null)
                {
                    throw new NotSupportedException("Could not create the AutoWire Dependency Graph tool window.");
                }
            }).FileAndForget("AutoWire/ShowDependencyGraph");
        }
    }
}
