using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoWire.VisualStudio.Commands;
using AutoWire.VisualStudio.ToolWindow;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace AutoWire.VisualStudio
{
    /// <summary>
    /// Root package for the AutoWire Visual Studio extension. Registers the "AutoWire" top-level menu,
    /// the "Show Dependency Graph" command, and the <see cref="DependencyGraphToolWindow"/>.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.AutoWirePackageGuidString)]
    [InstalledProductRegistration("AutoWire Tools for Visual Studio", "Inline AutoWire registration hints and a Mermaid dependency-graph viewer.", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(DependencyGraphToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    public sealed class AutoWireVsPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // Switch to the UI thread since the menu command service and tool window both require it.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await ShowDependencyGraphCommand.InitializeAsync(this);
        }
    }
}
