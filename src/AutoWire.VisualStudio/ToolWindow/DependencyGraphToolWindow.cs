using System.Runtime.InteropServices;
using AutoWire.VisualStudio.Commands;
using Microsoft.VisualStudio.Shell;

namespace AutoWire.VisualStudio.ToolWindow
{
    /// <summary>
    /// Tool window pane hosting <see cref="DependencyGraphControl"/>, which renders the Mermaid dependency
    /// graph produced by the AutoWire source generator (<c>AutoWireDependencyGraph.g.cs</c>).
    /// </summary>
    [Guid(PackageGuids.DependencyGraphToolWindowPersistenceGuidString)]
    public sealed class DependencyGraphToolWindow : ToolWindowPane
    {
        public DependencyGraphToolWindow() : base(null)
        {
            Caption = "AutoWire Dependency Graph";
            Content = new DependencyGraphControl();
        }
    }
}
