using System;

namespace AutoWire.VisualStudio.Commands
{
    /// <summary>Well-known GUIDs shared between the package, the .vsct command table, and the tool window.</summary>
    internal static class PackageGuids
    {
        /// <summary>Guid for <see cref="AutoWireVsPackage"/> itself (must match <c>source.extension.vsixmanifest</c>/.pkgdef registration and the .vsct package node).</summary>
        public const string AutoWirePackageGuidString = "4584d75c-82f0-42d9-98c2-cbc6a65a183e";

        /// <summary>Guid of the command set (menu group + commands) defined in AutoWirePackage.vsct.</summary>
        public const string AutoWireCommandSetGuidString = "382281e8-030b-45bd-a5e4-875fea424d0c";
        public static readonly Guid AutoWireCommandSet = new Guid(AutoWireCommandSetGuidString);

        /// <summary>Persistence guid for the Mermaid dependency graph tool window.</summary>
        public const string DependencyGraphToolWindowPersistenceGuidString = "f26976ef-838d-466a-9cf7-4ebae00a9bcd";
    }

    /// <summary>Command ids defined in AutoWirePackage.vsct (must match the numeric ids there).</summary>
    internal static class PackageIds
    {
        public const int AutoWireMenuGroup = 0x1020;
        public const int AutoWireMenu = 0x1021;
        public const int ShowDependencyGraphCommand = 0x0100;
    }
}
