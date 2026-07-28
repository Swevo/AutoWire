# AutoWire Tools for Visual Studio (VSIX)

This is a **separate, Windows-only, locally-built Visual Studio extension** that complements the
AutoWire source generator with IDE tooling. It is **not** part of the CI/NuGet pipeline:

- It targets `net472` and requires Visual Studio SDK assemblies (`Microsoft.VisualStudio.SDK`), which
  only build on Windows.
- It is **not** referenced by `AutoWire.slnx` (the solution built by `.github/workflows/build.yml` on
  `ubuntu-latest`). It has its own solution file, `src/AutoWire.VisualStudio/AutoWire.VisualStudio.slnx`,
  kept inside this folder (not at the repo root) so that a bare `dotnet restore`/`dotnet build` at the
  repo root (as CI and most local workflows use) keeps resolving unambiguously to `AutoWire.slnx` — having
  two `.slnx` files at the root causes `MSB1011: Specify which project or solution file to use`.
- Nothing here is published to NuGet (`IsPackable=false`).

## Features

1. **Registration adornment.** While editing a C# file, any class decorated with an AutoWire
   registration attribute (`[Scoped]`, `[Singleton]`, `[Transient]`, `[TryScoped]`, `[TrySingleton]`,
   `[TryTransient]`, `[HostedService]`, `[Factory]`, `[Validate]`, `[Options]`, `[HttpClient]`) gets a
   small gray inline hint right after its opening brace, e.g. `AutoWire: Scoped → IFoo, IBar (self)`.
   Implemented as a MEF-exported `IWpfTextViewCreationListener` + `IViewTaggerProvider` producing
   `IntraTextAdornmentTag`s (see `Adornment/`). Parsing is deliberately regex/text-based (not a full
   Roslyn semantic analysis) so it stays cheap to re-run on every buffer edit, and it never throws into
   the editor pipeline — a parse failure just means no hint is shown for that occurrence.

2. **Mermaid dependency graph tool window.** `AutoWire` menu (top-level menu bar) → **Show Dependency
   Graph...** opens a tool window (see `ToolWindow/`) where you can:
   - **Browse...** a generated `AutoWireDependencyGraph.g.cs` file (emitted by the AutoWire source
     generator, typically under a project's `obj\Debug\<tfm>\generated\AutoWire\AutoWire.AutoWireGenerator\`
     or wherever your build emits generator output) — the tool extracts the
     `public const string Mermaid = """ ... """;` raw string literal via regex.
   - **Paste** Mermaid text directly into the text box instead.
   - Click **Render** (or Browse auto-renders) to view the graph in an embedded WebView2 control, which
     navigates to an HTML document that loads `mermaid.js` from
     `https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js` and renders a `<pre class="mermaid">`
     block with the graph text.
   - **Requires internet access at runtime** to fetch the mermaid.js CDN script. There is no offline
     fallback bundled.
   - **Requires the WebView2 Runtime** to be installed on the machine running Visual Studio. If it is
     missing, initialization is caught and a plain-text message is shown instead of crashing the tool
     window (see `DependencyGraphControl.xaml.cs`, `EnsureWebViewInitializedAsync`). Install it from
     <https://developer.microsoft.com/microsoft-edge/webview2/> if you see that message.

## Building

### What worked in this environment

This machine has Visual Studio "18" Enterprise installed **without** the "Visual Studio extension
development" workload (no `Microsoft.VsSDK.targets` under the VS install's `MSBuild\Microsoft\VisualStudio`
tree). The project still builds and produces a `.vsix` purely from NuGet packages, using:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
    src\AutoWire.VisualStudio\AutoWire.VisualStudio.csproj /restore /p:Configuration=Debug /nologo
```

This succeeded with **0 errors** and produced:

```
src\AutoWire.VisualStudio\bin\Debug\net472\AutoWire.VisualStudio.vsix   (~1.73 MB)
```

Two non-obvious things were required to make an SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`-less, see
below) `net472` csproj produce a real VSIX **without** the VS SDK workload installed:

1. **`Microsoft.VSSDK.BuildTools` bundles its own copy of `Microsoft.VsSDK.targets`** (the classic,
   non-SDK-style VSIX packaging logic — `CreateVsixContainer`, `VSCTCompile`, `GeneratePkgDef`, etc.)
   inside the NuGet package itself, under `tools\vssdk\Microsoft.VsSDK.targets`. A normal VS-generated
   VSIX `.csproj` imports this via `<Import Project="$(VSToolsPath)\VSSDK\Microsoft.VsSDK.targets" />`,
   but that import is **not** added automatically for SDK-style projects — it has to be added explicitly
   (see the bottom of `AutoWire.VisualStudio.csproj`).
2. **Import ordering matters.** `Microsoft.VsSDK.targets` *appends* itself to MSBuild `...DependsOn`
   property chains (e.g. `PrepareForRunDependsOn`) that `Microsoft.Common.CurrentVersion.targets` (part of
   the .NET SDK's own `Sdk.targets`) also defines. If `Microsoft.VsSDK.targets` is imported *before*
   `Sdk.targets` — which is what happens by default with the `<Project Sdk="Microsoft.NET.Sdk">`
   shorthand, since that form always imports the SDK's targets last, right before `</Project>` — the SDK's
   own property assignment silently overwrites the VSSDK append and `CreateVsixContainer` never runs (the
   project builds with 0 errors, but no `.vsix` is produced). The fix used here is the documented
   "explicit Sdk import" pattern: `<Project>` (no `Sdk=` attribute) + explicit
   `<Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />` at the top and
   `<Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />` **before** the `Microsoft.VsSDK.targets`
   import at the bottom, so VSSDK's append happens after the SDK's own assignment.

Package versions used (all resolved from nuget.org, no local/offline feed needed):

| Package | Version |
|---|---|
| `Microsoft.VisualStudio.SDK` | `17.14.40265` |
| `Microsoft.VSSDK.BuildTools` | `17.14.2142` |
| `Microsoft.Web.WebView2` | `1.0.3124.44` |

> Only `17.x` versions of `Microsoft.VisualStudio.SDK` exist on nuget.org as of this writing (no `18.x`
> meta-package yet), even though `Microsoft.VSSDK.BuildTools` does have `18.x` releases. `17.14.40265`
> built and ran fine against the installed VS "18" Enterprise for this extension's needs (editor MEF
> interfaces + shell package/tool-window/menu-command APIs, which have been stable across VS versions).

### Known limitation / remaining gap

Because the "Visual Studio extension development" workload is not installed on this machine, the above
build could not be verified by actually **launching** the extension in a VS Experimental Instance (F5) —
only that the `.vsix` is produced correctly and its contents look right (manifest, MEF DLL, pkgdef,
WebView2 assemblies, icon, license — verified by unzipping the `.vsix` and listing its entries). If you
hit issues when first debugging with F5 (e.g. VS refusing to load the experimental instance or MEF
composition errors), install the **"Visual Studio extension development"** workload via the Visual Studio
Installer once, then retry — this workload installs the local `Microsoft.VsSDK.targets`/design-time tools
that VS itself uses for the F5 debug-launch experience and gives you IntelliSense/design-time support for
`.vsct`/`.vsixmanifest` files in the IDE (the NuGet-only build above does not need it, but VS's own tooling
around these file types benefits from it).

### Alternative: build via the standalone solution

```powershell
cd src\AutoWire.VisualStudio
& "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
    AutoWire.VisualStudio.slnx /restore /p:Configuration=Debug /nologo
```

this also succeeds and produces the same `.vsix`. Plain `dotnet build src\AutoWire.VisualStudio\AutoWire.VisualStudio.csproj`
(or `dotnet build src\AutoWire.VisualStudio\AutoWire.VisualStudio.slnx`) also works and produces the same
output — either build entry point is fine.

## Installing / debugging

- **Debug (Experimental Instance):** open `src/AutoWire.VisualStudio/AutoWire.VisualStudio.slnx` (or the
  `.csproj` directly) in
  Visual Studio 2022+/18 with the **"Visual Studio extension development"** workload installed, and press
  **F5**. This launches a separate "Experimental Instance" of Visual Studio with the extension loaded, so
  you can try it against real C# projects without affecting your main VS installation.
- **Install into a real VS instance:** double-click the built `.vsix`, or run:
  ```powershell
  & "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe" `
      src\AutoWire.VisualStudio\bin\Debug\net472\AutoWire.VisualStudio.vsix
  ```

## Project layout

```
src/AutoWire.VisualStudio/
  AutoWire.VisualStudio.csproj        SDK-style net472 VSIX project
  AutoWireVsPackage.cs                AsyncPackage: registers the menu command + tool window
  source.extension.vsixmanifest       VSIX v2/v3 manifest
  Commands/
    AutoWirePackage.vsct              Command table: "AutoWire" menu + "Show Dependency Graph..." command
    PackageGuids.cs                   Shared GUIDs/ids between the package, .vsct, and tool window
    ShowDependencyGraphCommand.cs     Menu command handler
  Adornment/
    AutoWireRegistrationParser.cs     Regex-based attribute/class scanner (no Roslyn semantic model)
    RegistrationAdornmentTagger.cs    ITagger<IntraTextAdornmentTag> implementation
    RegistrationAdornmentTaggerProvider.cs           MEF IViewTaggerProvider export
    RegistrationAdornmentTextViewCreationListener.cs MEF IWpfTextViewCreationListener export
  ToolWindow/
    DependencyGraphToolWindow.cs      ToolWindowPane hosting the WPF control
    DependencyGraphControl.xaml(.cs) Browse/Paste/Render UI + WebView2 Mermaid rendering
  Resources/
    Icon.png, CommandIcon.png         VSIX gallery icon / menu command icon
```
