using System.Text;

internal static class ManifestDiff
{
    public static int Run(string[] args)
    {
        string baseline = string.Empty, candidate = string.Empty, report = "autowire-manifest-diff.md";
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baseline": baseline = args[++i]; break;
                case "--candidate": candidate = args[++i]; break;
                case "--report": report = args[++i]; break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
            }
        }

        if (!File.Exists(baseline)) { Console.Error.WriteLine($"Baseline file not found: {baseline}"); return 1; }
        if (!File.Exists(candidate)) { Console.Error.WriteLine($"Candidate file not found: {candidate}"); return 1; }

        var baseSet = ReadSet(baseline);
        var candSet = ReadSet(candidate);
        var added = candSet.Where(l => !baseSet.Contains(l)).OrderBy(static l => l, StringComparer.Ordinal).ToList();
        var removed = baseSet.Where(l => !candSet.Contains(l)).OrderBy(static l => l, StringComparer.Ordinal).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# AutoWire Manifest Diff Report").AppendLine();
        sb.AppendLine($"**Baseline:** {baseline}  ");
        sb.AppendLine($"**Candidate:** {candidate}  ");
        sb.AppendLine($"**Baseline count:** {baseSet.Count}  ");
        sb.AppendLine($"**Candidate count:** {candSet.Count}  ");
        sb.AppendLine($"**Added:** {added.Count}  ");
        sb.AppendLine($"**Removed:** {removed.Count}  ").AppendLine();
        AppendSection(sb, "Added registrations", added);
        AppendSection(sb, "Removed registrations", removed);

        var dir = Path.GetDirectoryName(Path.GetFullPath(report));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(report, sb.ToString());
        Console.WriteLine($"Manifest diff report written to {report}");
        Console.WriteLine($"Added: {added.Count}, Removed: {removed.Count}");
        return 0;
    }

    static HashSet<string> ReadSet(string path) =>
        File.ReadAllLines(path).Where(static l => !string.IsNullOrWhiteSpace(l)).ToHashSet(StringComparer.Ordinal);

    static void AppendSection(StringBuilder sb, string title, List<string> lines)
    {
        sb.AppendLine($"## {title}").AppendLine();
        if (lines.Count == 0) sb.AppendLine("_None._");
        else
        {
            sb.AppendLine("```text");
            foreach (var l in lines) sb.AppendLine(l);
            sb.AppendLine("```");
        }
        sb.AppendLine();
    }

    public static void PrintHelp() => Console.WriteLine("""
Usage:
  autowire-migrate diff --baseline <file> --candidate <file> [--report <file>]

Compares two RegistrationSummary.RegistrationManifestEntries exports and writes a markdown report.
""");
}
