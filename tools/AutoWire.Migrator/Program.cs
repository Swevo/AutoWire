using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
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

var analyses = new List<ScanAnalysis>();
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
        if (!LooksLikeScanInvocation(inv)) continue;

        var analysis = AnalyzeScanChain(file, stmt, inv);
        analyses.Add(analysis);

        if (!options.Apply) continue;
        if (analysis.Confidence is MigrationConfidence.Manual) continue;

        var scaffold = BuildScaffold(analysis);
        replacements.Add(new Replacement(stmt.FullSpan.Start, stmt.FullSpan.Length, scaffold, stmt.ToFullString()));
    }

    if (replacements.Count == 0) continue;

    var updated = ApplyReplacements(source, replacements);
    File.WriteAllText(file, updated);

    var changed = new ChangedFile(file, source, updated);
    changedFiles.Add(changed);
}

Directory.CreateDirectory(Path.GetDirectoryName(options.ReportJson)!);
Directory.CreateDirectory(Path.GetDirectoryName(options.ReportMd)!);
Directory.CreateDirectory(options.PatchDir);

WriteJsonReport(options.ReportJson, analyses, changedFiles, options);
WriteMarkdownReport(options.ReportMd, analyses, changedFiles, options);
WritePatchBundle(options.PatchDir, changedFiles);

Console.WriteLine($"Analyzed Scan chains: {analyses.Count}");
Console.WriteLine($"Changed files: {changedFiles.Count}");
Console.WriteLine($"JSON report: {options.ReportJson}");
Console.WriteLine($"Markdown report: {options.ReportMd}");
Console.WriteLine($"Patch bundle: {options.PatchDir}");

if (!string.IsNullOrWhiteSpace(options.BaselineManifest) && !string.IsNullOrWhiteSpace(options.CandidateManifest))
{
    Console.WriteLine("Manifest validation hint:");
    Console.WriteLine($@"pwsh .\tools\compare-autowire-manifests.ps1 -Baseline ""{options.BaselineManifest}"" -Candidate ""{options.CandidateManifest}"" -ReportPath ""{options.ManifestDiffReport}""");
}

return 0;

static bool LooksLikeScanInvocation(InvocationExpressionSyntax invocation)
{
    if (invocation.Expression is not MemberAccessExpressionSyntax ma) return false;
    return string.Equals(ma.Name.Identifier.Text, "Scan", StringComparison.Ordinal);
}

static ScanAnalysis AnalyzeScanChain(string file, ExpressionStatementSyntax statement, InvocationExpressionSyntax invocation)
{
    var text = statement.ToString();

    var sourceSelector = text.Contains(".FromAssemblyOf<", StringComparison.Ordinal) ? "FromAssemblyOf"
        : text.Contains(".FromAssembliesOf<", StringComparison.Ordinal) ? "FromAssembliesOf"
        : text.Contains(".FromCallingAssembly(", StringComparison.Ordinal) ? "FromCallingAssembly"
        : "Unknown";

    var addClassCount = Count(text, ".AddClasses(");
    var asImplemented = text.Contains(".AsImplementedInterfaces(", StringComparison.Ordinal);
    var asSelf = text.Contains(".AsSelf(", StringComparison.Ordinal);
    var asGeneric = text.Contains(".As<", StringComparison.Ordinal) || text.Contains(".As(typeof(", StringComparison.Ordinal);
    var hasScoped = text.Contains(".WithScopedLifetime(", StringComparison.Ordinal);
    var hasSingleton = text.Contains(".WithSingletonLifetime(", StringComparison.Ordinal);
    var hasTransient = text.Contains(".WithTransientLifetime(", StringComparison.Ordinal);
    var hasAnyLifetime = hasScoped || hasSingleton || hasTransient;
    var hasAdvancedFilter =
        text.Contains(".WithAttribute<", StringComparison.Ordinal) ||
        text.Contains(".InNamespaces(", StringComparison.Ordinal) ||
        text.Contains(".AssignableToAny(", StringComparison.Ordinal) ||
        text.Contains(".AssignableTo(typeof(", StringComparison.Ordinal);

    var hasRegistrationStrategy = text.Contains(".UsingRegistrationStrategy(", StringComparison.Ordinal);
    var hasUnknownAs = !asImplemented && !asSelf && !asGeneric;

    var confidence = MigrationConfidence.AutoConverted;
    var reasons = new List<string>();

    if (addClassCount == 0)
    {
        confidence = MigrationConfidence.Manual;
        reasons.Add("No AddClasses() chain found.");
    }

    if (!hasAnyLifetime)
    {
        confidence = MigrationConfidence.Manual;
        reasons.Add("No With*Lifetime() call found.");
    }

    if (hasUnknownAs)
    {
        confidence = MigrationConfidence.Manual;
        reasons.Add("No supported As*() mapping found.");
    }

    if (confidence != MigrationConfidence.Manual && (hasAdvancedFilter || hasRegistrationStrategy || sourceSelector == "Unknown"))
    {
        confidence = MigrationConfidence.NeedsReview;
        if (hasAdvancedFilter) reasons.Add("Advanced filter chain requires manual parity verification.");
        if (hasRegistrationStrategy) reasons.Add("Registration strategy should be mapped to DuplicateStrategy.");
        if (sourceSelector == "Unknown") reasons.Add("Source selector could not be identified.");
    }

    var line = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    return new ScanAnalysis(
        file,
        line,
        sourceSelector,
        addClassCount,
        hasAnyLifetime,
        asImplemented,
        asSelf,
        asGeneric,
        hasAdvancedFilter,
        hasRegistrationStrategy,
        confidence,
        reasons,
        text);
}

static string BuildScaffold(ScanAnalysis analysis)
{
    var confidenceLabel = analysis.Confidence switch
    {
        MigrationConfidence.AutoConverted => "auto-converted",
        MigrationConfidence.NeedsReview => "needs-review",
        _ => "manual"
    };

    var reasons = analysis.Reasons.Count == 0
        ? "// - Notes: none."
        : string.Join(Environment.NewLine, analysis.Reasons.Select(static r => $"// - Note: {r}"));

    return $@"// AutoWire migration scaffold ({confidenceLabel}):
// - Source selector: {analysis.SourceSelector}
// - AddClasses chains: {analysis.AddClassChainCount}
// - As-mode flags: implemented={analysis.HasAsImplementedInterfaces}, self={analysis.HasAsSelf}, specific={analysis.HasAsGenericOrType}
// - Lifetime mapped: {analysis.HasLifetime}
{reasons}
// - Suggested next step: add explicit [Scoped]/[Singleton]/[Transient] attributes and remove Scrutor.
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

static void WriteJsonReport(string path, List<ScanAnalysis> analyses, List<ChangedFile> changedFiles, Options options)
{
    var report = new
    {
        generatedAtUtc = DateTime.UtcNow,
        mode = options.Apply ? "apply" : "analyze",
        summary = new
        {
            total = analyses.Count,
            autoConverted = analyses.Count(a => a.Confidence == MigrationConfidence.AutoConverted),
            needsReview = analyses.Count(a => a.Confidence == MigrationConfidence.NeedsReview),
            manual = analyses.Count(a => a.Confidence == MigrationConfidence.Manual),
            changedFiles = changedFiles.Count
        },
        analyses
    };

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

static void WriteMarkdownReport(string path, List<ScanAnalysis> analyses, List<ChangedFile> changedFiles, Options options)
{
    var sb = new StringBuilder();
    sb.AppendLine("# AutoWire Semantic Scrutor Migration Report");
    sb.AppendLine();
    sb.AppendLine($"**Mode:** {(options.Apply ? "apply" : "analyze")}  ");
    sb.AppendLine($"**Total Scan chains:** {analyses.Count}  ");
    sb.AppendLine($"**Auto-converted:** {analyses.Count(a => a.Confidence == MigrationConfidence.AutoConverted)}  ");
    sb.AppendLine($"**Needs review:** {analyses.Count(a => a.Confidence == MigrationConfidence.NeedsReview)}  ");
    sb.AppendLine($"**Manual:** {analyses.Count(a => a.Confidence == MigrationConfidence.Manual)}  ");
    sb.AppendLine($"**Changed files:** {changedFiles.Count}  ");
    sb.AppendLine();
    sb.AppendLine("| File | Line | Confidence | Source | AddClasses | Notes |");
    sb.AppendLine("|---|---:|---|---|---:|---|");
    foreach (var a in analyses.OrderBy(static x => x.FilePath).ThenBy(static x => x.Line))
    {
        var notes = a.Reasons.Count == 0 ? "None" : string.Join("; ", a.Reasons);
        sb.AppendLine($"| `{a.FilePath}` | {a.Line} | {a.Confidence} | {a.SourceSelector} | {a.AddClassChainCount} | {EscapePipe(notes)} |");
    }
    File.WriteAllText(path, sb.ToString());
}

static string EscapePipe(string value) => value.Replace("|", "\\|");

static void WritePatchBundle(string patchDir, List<ChangedFile> changedFiles)
{
    foreach (var changed in changedFiles)
    {
        var safe = string.Concat(changed.FilePath.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        File.WriteAllText(Path.Combine(patchDir, $"{safe}.before.cs"), changed.Before);
        File.WriteAllText(Path.Combine(patchDir, $"{safe}.after.cs"), changed.After);
    }
}

static int Count(string text, string needle)
{
    var start = 0;
    var count = 0;
    while (true)
    {
        var idx = text.IndexOf(needle, start, StringComparison.Ordinal);
        if (idx < 0) break;
        count++;
        start = idx + needle.Length;
    }
    return count;
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
}

file sealed record ScanAnalysis(
    string FilePath,
    int Line,
    string SourceSelector,
    int AddClassChainCount,
    bool HasLifetime,
    bool HasAsImplementedInterfaces,
    bool HasAsSelf,
    bool HasAsGenericOrType,
    bool HasAdvancedFilter,
    bool HasRegistrationStrategy,
    MigrationConfidence Confidence,
    List<string> Reasons,
    string OriginalSnippet);

file sealed record ChangedFile(string FilePath, string Before, string After);
file sealed record Replacement(int Start, int Length, string NewText, string OldText);

enum MigrationConfidence
{
    AutoConverted,
    NeedsReview,
    Manual
}
