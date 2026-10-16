using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var options = ParseArgs(args);
if (options.ShowHelp)
{
    PrintHelp();
    return 0;
}

var files = Directory
    .EnumerateFiles(options.Path, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"))
    .ToList();

var scanAnalyses = new List<ScanAnalysis>();
var decoratorAnalyses = new List<DecoratorAnalysis>();
var changedFiles = new List<ChangedFile>();

foreach (var file in files)
{
    var source = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(source);
    var root = tree.GetRoot();
    var statements = root
        .DescendantNodes()
        .OfType<ExpressionStatementSyntax>()
        .Where(static s => s.Expression is InvocationExpressionSyntax)
        .ToList();

    var replacements = new List<Replacement>();

    foreach (var stmt in statements)
    {
        if (stmt.Expression is not InvocationExpressionSyntax inv) continue;

        if (LooksLikeScanInvocation(inv))
        {
            var analysis = AnalyzeScanChain(file, stmt);
            scanAnalyses.Add(analysis);

            if (options.Apply && analysis.Confidence is not MigrationConfidence.Manual)
            {
                var scaffold = BuildScanScaffold(analysis);
                replacements.Add(new Replacement(stmt.FullSpan.Start, stmt.FullSpan.Length, scaffold));
            }
        }
        else if (LooksLikeDecorateInvocation(inv))
        {
            decoratorAnalyses.Add(AnalyzeDecorateCall(file, stmt));
        }
    }

    if (replacements.Count == 0) continue;

    var updated = ApplyReplacements(source, replacements);
    File.WriteAllText(file, updated);
    changedFiles.Add(new ChangedFile(file, source, updated));
}

Directory.CreateDirectory(Path.GetDirectoryName(options.ReportJson)!);
Directory.CreateDirectory(Path.GetDirectoryName(options.ReportMd)!);
Directory.CreateDirectory(options.PatchDir);

WriteJsonReport(options.ReportJson, scanAnalyses, decoratorAnalyses, changedFiles, options);
WriteMarkdownReport(options.ReportMd, scanAnalyses, decoratorAnalyses, changedFiles, options);
WritePatchBundle(options.PatchDir, changedFiles);

var summary = Summarize(scanAnalyses, decoratorAnalyses, changedFiles);
Console.WriteLine($"Analyzed Scan chains: {summary.ScanTotal}");
Console.WriteLine($"Analyzed Decorate chains: {summary.DecoratorTotal}");
Console.WriteLine($"Changed files: {summary.ChangedFiles}");
Console.WriteLine($"JSON report: {options.ReportJson}");
Console.WriteLine($"Markdown report: {options.ReportMd}");
Console.WriteLine($"Patch bundle: {options.PatchDir}");

if (!string.IsNullOrWhiteSpace(options.BaselineManifest) && !string.IsNullOrWhiteSpace(options.CandidateManifest))
{
    Console.WriteLine("Manifest validation command:");
    Console.WriteLine(
        $@"pwsh .\tools\compare-autowire-manifests.ps1 -Baseline ""{options.BaselineManifest}"" -Candidate ""{options.CandidateManifest}"" -ReportPath ""{options.ManifestDiffReport}""");
}

if (options.FailOnThreshold)
{
    var thresholdErrors = new List<string>();
    if (summary.Manual > options.MaxManual)
        thresholdErrors.Add($"manual={summary.Manual} exceeds max-manual={options.MaxManual}");
    if (summary.NeedsReview > options.MaxNeedsReview)
        thresholdErrors.Add($"needs-review={summary.NeedsReview} exceeds max-needs-review={options.MaxNeedsReview}");

    if (thresholdErrors.Count > 0)
    {
        Console.Error.WriteLine("Migration thresholds failed:");
        foreach (var err in thresholdErrors) Console.Error.WriteLine($" - {err}");
        return 2;
    }
}

return 0;

static bool LooksLikeScanInvocation(InvocationExpressionSyntax invocation) =>
    invocation.Expression is MemberAccessExpressionSyntax ma &&
    string.Equals(ma.Name.Identifier.Text, "Scan", StringComparison.Ordinal);

static bool LooksLikeDecorateInvocation(InvocationExpressionSyntax invocation) =>
    invocation.Expression is MemberAccessExpressionSyntax ma &&
    string.Equals(ma.Name.Identifier.Text, "Decorate", StringComparison.Ordinal);

static ScanAnalysis AnalyzeScanChain(string file, ExpressionStatementSyntax statement)
{
    var text = statement.ToString();
    var sourceSelector = DetectSourceSelector(text);
    var addClasses = AnalyzeAddClassesChains(text);
    var registrationStrategy = DetectRegistrationStrategy(text);

    var confidence = MigrationConfidence.AutoConverted;
    var reasons = new List<string>();

    if (addClasses.Count == 0)
    {
        confidence = MigrationConfidence.Manual;
        reasons.Add("No AddClasses() chain found.");
    }

    foreach (var chain in addClasses)
    {
        if (chain.Lifetime == LifetimeMode.Unknown)
        {
            confidence = MigrationConfidence.Manual;
            reasons.Add("One or more AddClasses() chains have no explicit With*Lifetime().");
        }

        if (chain.AsMode == AsMode.Unknown)
        {
            confidence = MigrationConfidence.Manual;
            reasons.Add("One or more AddClasses() chains use unsupported As*() mapping.");
        }
    }

    if (confidence != MigrationConfidence.Manual)
    {
        if (sourceSelector == SourceSelector.Unknown)
        {
            confidence = MigrationConfidence.NeedsReview;
            reasons.Add("Source selector could not be identified.");
        }

        if (registrationStrategy != RegistrationStrategy.None)
        {
            confidence = MigrationConfidence.NeedsReview;
            reasons.Add("UsingRegistrationStrategy should be mapped to DuplicateStrategy explicitly.");
        }

        if (addClasses.Any(static c => c.HasAdvancedFilter))
        {
            confidence = MigrationConfidence.NeedsReview;
            reasons.Add("Advanced filter chains require parity verification.");
        }
    }

    var line = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    return new ScanAnalysis(
        file,
        line,
        sourceSelector,
        registrationStrategy,
        addClasses,
        confidence,
        reasons,
        text);
}

static DecoratorAnalysis AnalyzeDecorateCall(string file, ExpressionStatementSyntax statement)
{
    var text = statement.ToString();
    var line = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    var style = text.Contains("=>", StringComparison.Ordinal)
        ? DecorateStyle.FactoryLambda
        : text.Contains("Decorate<", StringComparison.Ordinal)
            ? DecorateStyle.GenericDecorator
            : DecorateStyle.Unknown;

    var confidence = style == DecorateStyle.GenericDecorator
        ? MigrationConfidence.NeedsReview
        : MigrationConfidence.Manual;

    var reasons = new List<string>();
    if (style == DecorateStyle.GenericDecorator)
        reasons.Add("Map to [DecorateScoped]/[DecorateSingleton]/[DecorateTransient] with explicit order.");
    else if (style == DecorateStyle.FactoryLambda)
        reasons.Add("Lambda-based Decorate requires manual migration to attribute-based decorator chain.");
    else
        reasons.Add("Unknown Decorate() shape.");

    return new DecoratorAnalysis(file, line, style, confidence, reasons, text);
}

static SourceSelector DetectSourceSelector(string text)
{
    if (text.Contains(".FromAssemblyOf<", StringComparison.Ordinal)) return SourceSelector.FromAssemblyOf;
    if (text.Contains(".FromAssembliesOf<", StringComparison.Ordinal)) return SourceSelector.FromAssembliesOf;
    if (text.Contains(".FromCallingAssembly(", StringComparison.Ordinal)) return SourceSelector.FromCallingAssembly;
    if (text.Contains(".FromApplicationDependencies(", StringComparison.Ordinal)) return SourceSelector.FromApplicationDependencies;
    return SourceSelector.Unknown;
}

static RegistrationStrategy DetectRegistrationStrategy(string text)
{
    if (!text.Contains(".UsingRegistrationStrategy(", StringComparison.Ordinal))
        return RegistrationStrategy.None;
    if (text.Contains("RegistrationStrategy.Skip", StringComparison.Ordinal))
        return RegistrationStrategy.Skip;
    if (text.Contains("RegistrationStrategy.Replace", StringComparison.Ordinal))
        return RegistrationStrategy.Replace;
    if (text.Contains("RegistrationStrategy.Append", StringComparison.Ordinal))
        return RegistrationStrategy.Append;
    if (text.Contains("RegistrationStrategy.Throw", StringComparison.Ordinal))
        return RegistrationStrategy.Throw;
    return RegistrationStrategy.Unknown;
}

static List<AddClassesChain> AnalyzeAddClassesChains(string text)
{
    var matches = Regex.Matches(
        text,
        @"\.AddClasses\s*\((?<filter>.*?)\)\s*(?<body>.*?)(?=\.AddClasses\s*\(|\)\s*;|$)",
        RegexOptions.Singleline);

    var list = new List<AddClassesChain>();
    foreach (Match match in matches)
    {
        var filter = match.Groups["filter"].Value.Trim();
        var body = match.Groups["body"].Value;

        var asMode = AsMode.Unknown;
        var asType = string.Empty;
        if (body.Contains(".AsImplementedInterfaces(", StringComparison.Ordinal))
            asMode = AsMode.AsImplementedInterfaces;
        else if (body.Contains(".AsSelf(", StringComparison.Ordinal))
            asMode = AsMode.AsSelf;
        else
        {
            var asGeneric = Regex.Match(body, @"\.As<(?<asType>[^>]+)>\s*\(");
            if (asGeneric.Success)
            {
                asMode = AsMode.AsSpecificType;
                asType = asGeneric.Groups["asType"].Value.Trim();
            }
            else
            {
                var asTypeof = Regex.Match(body, @"\.As\s*\(\s*typeof\((?<asType>[^)]+)\)\s*\)");
                if (asTypeof.Success)
                {
                    asMode = AsMode.AsSpecificType;
                    asType = asTypeof.Groups["asType"].Value.Trim();
                }
            }
        }

        var lifetime = LifetimeMode.Unknown;
        if (body.Contains(".WithScopedLifetime(", StringComparison.Ordinal))
            lifetime = LifetimeMode.Scoped;
        else if (body.Contains(".WithSingletonLifetime(", StringComparison.Ordinal))
            lifetime = LifetimeMode.Singleton;
        else if (body.Contains(".WithTransientLifetime(", StringComparison.Ordinal))
            lifetime = LifetimeMode.Transient;

        var hasAdvancedFilter =
            body.Contains(".WithAttribute<", StringComparison.Ordinal) ||
            body.Contains(".InNamespaces(", StringComparison.Ordinal) ||
            body.Contains(".AssignableToAny(", StringComparison.Ordinal) ||
            body.Contains(".AssignableTo(typeof(", StringComparison.Ordinal) ||
            body.Contains(".AsMatchingInterface(", StringComparison.Ordinal);

        list.Add(new AddClassesChain(filter, asMode, asType, lifetime, hasAdvancedFilter, body.Trim()));
    }

    return list;
}

static string BuildScanScaffold(ScanAnalysis analysis)
{
    var confidence = analysis.Confidence switch
    {
        MigrationConfidence.AutoConverted => "auto-converted",
        MigrationConfidence.NeedsReview => "needs-review",
        _ => "manual"
    };

    var chainLines = analysis.AddClasses.Count == 0
        ? "// - Chains: none."
        : string.Join(
            Environment.NewLine,
            analysis.AddClasses.Select(static (c, i) =>
                $"//   {i + 1}) lifetime={c.Lifetime}, as={c.AsMode}{(string.IsNullOrWhiteSpace(c.AsType) ? "" : $"<{c.AsType}>")}, filter={c.Filter}"));

    var notes = analysis.Reasons.Count == 0
        ? "// - Notes: none."
        : string.Join(Environment.NewLine, analysis.Reasons.Select(static r => $"// - Note: {r}"));

    return $@"// AutoWire migration scaffold ({confidence}):
// - Source selector: {analysis.SourceSelector}
// - Registration strategy: {analysis.RegistrationStrategy}
{chainLines}
{notes}
// - Suggested next step: add explicit [Scoped]/[Singleton]/[Transient] attributes and map duplicates/order behavior intentionally.
services.AddAutoWireServices();
";
}

static string ApplyReplacements(string source, List<Replacement> replacements)
{
    var sb = new StringBuilder(source);
    foreach (var repl in replacements.OrderByDescending(static r => r.Start))
    {
        sb.Remove(repl.Start, repl.Length);
        sb.Insert(repl.Start, repl.NewText);
    }
    return sb.ToString();
}

static void WriteJsonReport(
    string path,
    List<ScanAnalysis> scanAnalyses,
    List<DecoratorAnalysis> decoratorAnalyses,
    List<ChangedFile> changedFiles,
    Options options)
{
    var summary = Summarize(scanAnalyses, decoratorAnalyses, changedFiles);
    var report = new
    {
        generatedAtUtc = DateTime.UtcNow,
        mode = options.Apply ? "apply" : "analyze",
        summary,
        scans = scanAnalyses,
        decorators = decoratorAnalyses
    };

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

static void WriteMarkdownReport(
    string path,
    List<ScanAnalysis> scanAnalyses,
    List<DecoratorAnalysis> decoratorAnalyses,
    List<ChangedFile> changedFiles,
    Options options)
{
    var summary = Summarize(scanAnalyses, decoratorAnalyses, changedFiles);
    var sb = new StringBuilder();
    sb.AppendLine("# AutoWire Semantic Scrutor Migration Report");
    sb.AppendLine();
    sb.AppendLine($"**Mode:** {(options.Apply ? "apply" : "analyze")}  ");
    sb.AppendLine($"**Scan chains:** {summary.ScanTotal}  ");
    sb.AppendLine($"**Decorator chains:** {summary.DecoratorTotal}  ");
    sb.AppendLine($"**Auto-converted:** {summary.AutoConverted}  ");
    sb.AppendLine($"**Needs review:** {summary.NeedsReview}  ");
    sb.AppendLine($"**Manual:** {summary.Manual}  ");
    sb.AppendLine($"**Changed files:** {summary.ChangedFiles}  ");
    sb.AppendLine();

    sb.AppendLine("## Scan chains");
    sb.AppendLine();
    sb.AppendLine("| File | Line | Confidence | Source | Strategy | AddClasses | Notes |");
    sb.AppendLine("|---|---:|---|---|---|---:|---|");
    foreach (var a in scanAnalyses.OrderBy(static x => x.FilePath).ThenBy(static x => x.Line))
    {
        var notes = a.Reasons.Count == 0 ? "None" : string.Join("; ", a.Reasons);
        sb.AppendLine($"| `{a.FilePath}` | {a.Line} | {a.Confidence} | {a.SourceSelector} | {a.RegistrationStrategy} | {a.AddClasses.Count} | {EscapePipe(notes)} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Decorator chains");
    sb.AppendLine();
    sb.AppendLine("| File | Line | Style | Confidence | Notes |");
    sb.AppendLine("|---|---:|---|---|---|");
    foreach (var d in decoratorAnalyses.OrderBy(static x => x.FilePath).ThenBy(static x => x.Line))
    {
        var notes = d.Reasons.Count == 0 ? "None" : string.Join("; ", d.Reasons);
        sb.AppendLine($"| `{d.FilePath}` | {d.Line} | {d.Style} | {d.Confidence} | {EscapePipe(notes)} |");
    }

    File.WriteAllText(path, sb.ToString());
}

static string EscapePipe(string value) => value.Replace("|", "\\|");

static Summary Summarize(List<ScanAnalysis> scans, List<DecoratorAnalysis> decorators, List<ChangedFile> changedFiles)
{
    var allConfidence = scans.Select(static s => s.Confidence).Concat(decorators.Select(static d => d.Confidence)).ToList();
    return new Summary(
        scans.Count,
        decorators.Count,
        allConfidence.Count(c => c == MigrationConfidence.AutoConverted),
        allConfidence.Count(c => c == MigrationConfidence.NeedsReview),
        allConfidence.Count(c => c == MigrationConfidence.Manual),
        changedFiles.Count);
}

static void WritePatchBundle(string patchDir, List<ChangedFile> changedFiles)
{
    foreach (var changed in changedFiles)
    {
        var safe = string.Concat(changed.FilePath.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        File.WriteAllText(Path.Combine(patchDir, $"{safe}.before.cs"), changed.Before);
        File.WriteAllText(Path.Combine(patchDir, $"{safe}.after.cs"), changed.After);
    }
}

static Options ParseArgs(string[] args)
{
    var options = new Options();
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "--help":
            case "-h":
                options.ShowHelp = true;
                break;
            case "--path":
                options.Path = args[++i];
                break;
            case "--apply":
                options.Apply = true;
                break;
            case "--report-json":
                options.ReportJson = args[++i];
                break;
            case "--report-md":
                options.ReportMd = args[++i];
                break;
            case "--patch-dir":
                options.PatchDir = args[++i];
                break;
            case "--baseline-manifest":
                options.BaselineManifest = args[++i];
                break;
            case "--candidate-manifest":
                options.CandidateManifest = args[++i];
                break;
            case "--manifest-diff-report":
                options.ManifestDiffReport = args[++i];
                break;
            case "--fail-on-threshold":
                options.FailOnThreshold = true;
                break;
            case "--max-manual":
                options.MaxManual = int.Parse(args[++i]);
                break;
            case "--max-needs-review":
                options.MaxNeedsReview = int.Parse(args[++i]);
                break;
        }
    }
    return options;
}

static void PrintHelp()
{
    Console.WriteLine("""
Usage:
  dotnet run --project tools/AutoWire.Migrator/AutoWire.Migrator.csproj -- [options]

Options:
  --path <dir>                  Root folder to analyze (default: .)
  --apply                       Apply scaffolding replacements for auto/needs-review chains
  --report-json <file>          JSON report path
  --report-md <file>            Markdown report path
  --patch-dir <dir>             Patch bundle output directory
  --baseline-manifest <file>    Optional baseline registration manifest
  --candidate-manifest <file>   Optional candidate registration manifest
  --manifest-diff-report <file> Optional manifest diff output path
  --fail-on-threshold           Return exit code 2 when confidence thresholds are exceeded
  --max-manual <n>              Maximum allowed manual items when fail-on-threshold is set
  --max-needs-review <n>        Maximum allowed needs-review items when fail-on-threshold is set
""");
}

file sealed class Options
{
    public bool ShowHelp { get; set; }
    public string Path { get; set; } = ".";
    public bool Apply { get; set; }
    public string ReportJson { get; set; } = @".\autowire-semantic-migration-report.json";
    public string ReportMd { get; set; } = @".\autowire-semantic-migration-report.md";
    public string PatchDir { get; set; } = @".\autowire-migration-patches";
    public string BaselineManifest { get; set; } = string.Empty;
    public string CandidateManifest { get; set; } = string.Empty;
    public string ManifestDiffReport { get; set; } = @".\autowire-manifest-diff.md";
    public bool FailOnThreshold { get; set; }
    public int MaxManual { get; set; }
    public int MaxNeedsReview { get; set; } = int.MaxValue;
}

file sealed record ScanAnalysis(
    string FilePath,
    int Line,
    SourceSelector SourceSelector,
    RegistrationStrategy RegistrationStrategy,
    List<AddClassesChain> AddClasses,
    MigrationConfidence Confidence,
    List<string> Reasons,
    string OriginalSnippet);

file sealed record AddClassesChain(
    string Filter,
    AsMode AsMode,
    string AsType,
    LifetimeMode Lifetime,
    bool HasAdvancedFilter,
    string BodySnippet);

file sealed record DecoratorAnalysis(
    string FilePath,
    int Line,
    DecorateStyle Style,
    MigrationConfidence Confidence,
    List<string> Reasons,
    string OriginalSnippet);

file sealed record Summary(
    int ScanTotal,
    int DecoratorTotal,
    int AutoConverted,
    int NeedsReview,
    int Manual,
    int ChangedFiles);

file sealed record ChangedFile(string FilePath, string Before, string After);
file sealed record Replacement(int Start, int Length, string NewText);

enum MigrationConfidence
{
    AutoConverted,
    NeedsReview,
    Manual
}

enum SourceSelector
{
    Unknown,
    FromAssemblyOf,
    FromAssembliesOf,
    FromCallingAssembly,
    FromApplicationDependencies
}

enum RegistrationStrategy
{
    None,
    Skip,
    Replace,
    Append,
    Throw,
    Unknown
}

enum AsMode
{
    Unknown,
    AsImplementedInterfaces,
    AsSelf,
    AsSpecificType
}

enum LifetimeMode
{
    Unknown,
    Scoped,
    Singleton,
    Transient
}

enum DecorateStyle
{
    Unknown,
    GenericDecorator,
    FactoryLambda
}
