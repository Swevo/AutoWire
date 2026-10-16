using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal enum RegistrationSource
{
    MicrosoftDi,
    Autofac
}

internal sealed class RegistrationAnalysis
{
    public RegistrationAnalysis(
        string filePath, int line, RegistrationSource source, MigrationConfidence confidence, string lifetime,
        string implementationType, List<string> serviceTypes, bool asImplementedInterfaces,
        List<string> reasons, string suggestedAttribute, string originalSnippet)
    {
        FilePath = filePath; Line = line; Source = source; Confidence = confidence; Lifetime = lifetime;
        ImplementationType = implementationType; ServiceTypes = serviceTypes; AsImplementedInterfaces = asImplementedInterfaces;
        Reasons = reasons; SuggestedAttribute = suggestedAttribute; OriginalSnippet = originalSnippet;
    }

    public string FilePath { get; }
    public int Line { get; }
    public RegistrationSource Source { get; }
    public MigrationConfidence Confidence { get; set; }
    public string Lifetime { get; }
    public string ImplementationType { get; }
    public List<string> ServiceTypes { get; }
    public bool AsImplementedInterfaces { get; }
    public List<string> Reasons { get; }
    public string SuggestedAttribute { get; }
    public string OriginalSnippet { get; }
}
internal sealed record RegistrationEdit(string FilePath, int Start, int Length, string NewText);

internal static class RegistrationMigration
{
    static readonly HashSet<string> MsDiMethods = new(StringComparer.Ordinal)
    {
        "AddScoped", "AddSingleton", "AddTransient"
    };

    static readonly HashSet<string> MsDiOtherMethods = new(StringComparer.Ordinal)
    {
        "TryAddScoped", "TryAddSingleton", "TryAddTransient",
        "AddKeyedScoped", "AddKeyedSingleton", "AddKeyedTransient",
        "TryAddKeyedScoped", "TryAddKeyedSingleton", "TryAddKeyedTransient",
        "TryAddEnumerable"
    };

    static readonly HashSet<string> AutofacRoots = new(StringComparer.Ordinal)
    {
        "RegisterType", "RegisterGeneric", "RegisterAssemblyTypes", "RegisterInstance",
        "Register", "RegisterDecorator", "RegisterModule", "RegisterAssemblyModules"
    };

    static readonly HashSet<string> AutofacSimpleModifiers = new(StringComparer.Ordinal)
    {
        "As", "AsSelf", "AsImplementedInterfaces",
        "InstancePerLifetimeScope", "InstancePerRequest", "SingleInstance", "InstancePerDependency"
    };

    public static List<RegistrationAnalysis> Analyze(
        string file,
        string source,
        SyntaxNode root,
        out List<(ExpressionStatementSyntax Stmt, RegistrationAnalysis Analysis)> convertible)
    {
        var results = new List<RegistrationAnalysis>();
        convertible = new List<(ExpressionStatementSyntax, RegistrationAnalysis)>();
        var hasAutofac = source.Contains("Autofac", StringComparison.Ordinal);

        foreach (var stmt in root.DescendantNodes().OfType<ExpressionStatementSyntax>())
        {
            if (stmt.Expression is not InvocationExpressionSyntax inv) continue;

            var chain = Flatten(inv);
            if (chain.Count == 0) continue;

            RegistrationAnalysis? analysis = null;
            var rootCall = chain[0];

            if (hasAutofac && AutofacRoots.Contains(rootCall.Name))
                analysis = AnalyzeAutofac(file, stmt, chain);
            else if (MsDiMethods.Contains(rootCall.Name) || MsDiOtherMethods.Contains(rootCall.Name)
                     || chain.Any(c => MsDiMethods.Contains(c.Name) || MsDiOtherMethods.Contains(c.Name)))
                analysis = AnalyzeMicrosoftDi(file, stmt, chain);

            if (analysis is null) continue;

            results.Add(analysis);
            if (analysis.Confidence == MigrationConfidence.AutoConverted)
                convertible.Add((stmt, analysis));
        }

        return results;
    }

    record Call(string Name, SeparatedSyntaxList<TypeSyntax> TypeArgs, SeparatedSyntaxList<ArgumentSyntax> Args);

    // Root call first, outermost call last.
    static List<Call> Flatten(InvocationExpressionSyntax inv)
    {
        var calls = new List<Call>();
        ExpressionSyntax? current = inv;
        while (current is InvocationExpressionSyntax i)
        {
            if (i.Expression is MemberAccessExpressionSyntax member)
            {
                var typeArgs = member.Name is GenericNameSyntax g ? g.TypeArgumentList.Arguments : default;
                calls.Add(new Call(member.Name.Identifier.ValueText, typeArgs, i.ArgumentList.Arguments));
                current = member.Expression;
            }
            else break;
        }

        calls.Reverse();
        return calls;
    }

    static RegistrationAnalysis? AnalyzeMicrosoftDi(string file, ExpressionStatementSyntax stmt, List<Call> chain)
    {
        var diCalls = chain.Where(c => MsDiMethods.Contains(c.Name) || MsDiOtherMethods.Contains(c.Name)).ToList();
        if (diCalls.Count == 0) return null;

        var snippet = stmt.ToString();
        var line = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var reasons = new List<string>();

        if (diCalls.Count > 1 || chain.Count != diCalls.Count)
        {
            reasons.Add("Chained registrations: split into separate statements to migrate");
            return Make(file, line, RegistrationSource.MicrosoftDi, MigrationConfidence.NeedsReview, "Unknown", "?", new(), false, reasons, "", snippet);
        }

        var call = diCalls[0];
        var name = call.Name;
        if (name.StartsWith("Try", StringComparison.Ordinal) || name.Contains("Keyed", StringComparison.Ordinal) || name == "TryAddEnumerable")
        {
            reasons.Add($"{name} has semantics that differ from [Scoped]/[Singleton]/[Transient]; review manually (use Key = ... for keyed services)");
            return Make(file, line, RegistrationSource.MicrosoftDi, MigrationConfidence.NeedsReview, "Unknown", "?", new(), false, reasons, "", snippet);
        }

        var lifetime = name.Substring(3);
        string? service = null;
        string? impl = null;

        if (call.TypeArgs.Count == 1 && call.Args.Count == 0)
        {
            impl = call.TypeArgs[0].ToString();
        }
        else if (call.TypeArgs.Count == 2 && call.Args.Count == 0)
        {
            service = call.TypeArgs[0].ToString();
            impl = call.TypeArgs[1].ToString();
        }
        else if (call.TypeArgs.Count == 0 && call.Args.Count is 1 or 2
                 && call.Args.All(static a => a.Expression is TypeOfExpressionSyntax))
        {
            var types = call.Args.Select(static a => ((TypeOfExpressionSyntax)a.Expression).Type.ToString()).ToList();
            if (types.Count == 1) impl = types[0];
            else { service = types[0]; impl = types[1]; }
        }
        else
        {
            reasons.Add("Factory lambda or instance registration: use [Factory] or keep the manual registration");
            return Make(file, line, RegistrationSource.MicrosoftDi, MigrationConfidence.Manual, lifetime, "?", new(), false, reasons, "", snippet);
        }

        var services = new List<string> { service ?? impl };
        return Make(file, line, RegistrationSource.MicrosoftDi, MigrationConfidence.AutoConverted, lifetime, impl, services, false, reasons,
            BuildAttribute(lifetime, services, false), snippet);
    }

    static RegistrationAnalysis? AnalyzeAutofac(string file, ExpressionStatementSyntax stmt, List<Call> chain)
    {
        var root = chain[0];
        var snippet = stmt.ToString();
        var line = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var reasons = new List<string>();
        const RegistrationSource src = RegistrationSource.Autofac;

        switch (root.Name)
        {
            case "RegisterAssemblyTypes":
            case "RegisterAssemblyModules":
                reasons.Add("Assembly scanning: replace with [assembly: AutoWire.AutoWireScan(\"Your.Namespace\")] and review the Where/Except filters");
                return Make(file, line, src, MigrationConfidence.NeedsReview, "Unknown", "?", new(), false, reasons, "[assembly: AutoWire.AutoWireScan(\"Your.Namespace\")]", snippet);
            case "RegisterModule":
                reasons.Add("Autofac module registration: migrate the module's Load() registrations individually");
                return Make(file, line, src, MigrationConfidence.NeedsReview, "Unknown", "?", new(), false, reasons, "", snippet);
            case "RegisterDecorator":
                reasons.Add("Autofac decorator: replace with [DecorateScoped/DecorateSingleton/DecorateTransient(typeof(IService))] on the decorator class");
                return Make(file, line, src, MigrationConfidence.NeedsReview, "Unknown", "?", new(), false, reasons, "", snippet);
            case "RegisterInstance":
            case "Register":
                reasons.Add("Lambda/instance registration: use [Factory] or keep the manual registration");
                return Make(file, line, src, MigrationConfidence.Manual, "Unknown", "?", new(), false, reasons, "", snippet);
        }

        // RegisterType<T>() / RegisterGeneric(typeof(T<>))
        string impl;
        if (root.Name == "RegisterType" && root.TypeArgs.Count == 1 && root.Args.Count == 0)
            impl = root.TypeArgs[0].ToString();
        else if (root is { Name: "RegisterType", TypeArgs.Count: 0, Args.Count: 1 } && root.Args[0].Expression is TypeOfExpressionSyntax rt)
            impl = rt.Type.ToString();
        else if (root is { Name: "RegisterGeneric", Args.Count: 1 } && root.Args[0].Expression is TypeOfExpressionSyntax rg)
            impl = rg.Type.ToString();
        else
        {
            reasons.Add("Unrecognised registration shape");
            return Make(file, line, src, MigrationConfidence.Manual, "Unknown", "?", new(), false, reasons, "", snippet);
        }

        var services = new List<string>();
        var asSelf = false;
        var asInterfaces = false;
        var lifetime = "Transient";
        var unsupported = new List<string>();

        foreach (var call in chain.Skip(1))
        {
            switch (call.Name)
            {
                case "As":
                    if (call.TypeArgs.Count > 0) services.AddRange(call.TypeArgs.Select(static t => t.ToString()));
                    foreach (var arg in call.Args)
                    {
                        if (arg.Expression is TypeOfExpressionSyntax t) services.Add(t.Type.ToString());
                        else unsupported.Add("As(<non-typeof>)");
                    }
                    break;
                case "AsSelf": asSelf = true; break;
                case "AsImplementedInterfaces": asInterfaces = true; break;
                case "InstancePerLifetimeScope":
                case "InstancePerRequest": lifetime = "Scoped"; break;
                case "SingleInstance": lifetime = "Singleton"; break;
                case "InstancePerDependency": lifetime = "Transient"; break;
                default: unsupported.Add(call.Name); break;
            }
        }

        if (unsupported.Count > 0)
        {
            reasons.Add($"Unsupported Autofac modifiers: {string.Join(", ", unsupported.Distinct())}. Migrate by hand (Named/Keyed → Key = ..., OnActivated/properties → factory)");
            return Make(file, line, src, MigrationConfidence.Manual, lifetime, impl, services, asInterfaces, reasons, "", snippet);
        }

        if (chain.Skip(1).All(static c => !c.Name.StartsWith("InstancePer") && c.Name != "SingleInstance"))
            reasons.Add("No lifetime specified: Autofac default (InstancePerDependency) mapped to Transient");

        if (asInterfaces && services.Count == 0 && !asSelf)
            return Make(file, line, src, MigrationConfidence.AutoConverted, lifetime, impl, services, true, reasons,
                BuildAttribute(lifetime, services, true), snippet);

        if (asInterfaces)
        {
            reasons.Add("AsImplementedInterfaces combined with As/AsSelf: AutoWire auto-discovery registers all interfaces; verify the result");
            return Make(file, line, src, MigrationConfidence.NeedsReview, lifetime, impl, services, true, reasons, "", snippet);
        }

        if (services.Count == 0 || asSelf) services.Insert(0, StripGenericArguments(impl, root.Name == "RegisterGeneric" || impl.Contains('<')));
        services = services.Distinct(StringComparer.Ordinal).ToList();

        return Make(file, line, src, MigrationConfidence.AutoConverted, lifetime, impl, services, false, reasons,
            BuildAttribute(lifetime, services, false), snippet);
    }

    static string StripGenericArguments(string type, bool open)
    {
        if (!open) return type;
        var lt = type.IndexOf('<');
        if (lt < 0) return type;
        var commas = type.Count(static c => c == ',');
        return type.Substring(0, lt) + "<" + new string(',', commas) + ">";
    }

    static string BuildAttribute(string lifetime, List<string> services, bool allInterfaces)
    {
        if (allInterfaces) return $"[AutoWire.{lifetime}]";
        return $"[AutoWire.{lifetime}({string.Join(", ", services.Select(static s => $"typeof({s})"))})]";
    }

    static RegistrationAnalysis Make(
        string file, int line, RegistrationSource source, MigrationConfidence confidence, string lifetime,
        string impl, List<string> services, bool asInterfaces, List<string> reasons, string attribute, string snippet) =>
        new(file, line, source, confidence, lifetime, impl, services, asInterfaces, reasons, attribute, snippet);

    // Builds edits that decorate implementation classes and remove the migrated registration statements.
    public static List<RegistrationEdit> BuildEdits(
        Dictionary<string, (string Source, SyntaxNode Root)> files,
        List<(string File, ExpressionStatementSyntax Stmt, RegistrationAnalysis Analysis)> convertible,
        List<RegistrationAnalysis> allAnalyses)
    {
        var edits = new List<RegistrationEdit>();

        var classes = files
            .SelectMany(f => f.Value.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => (File: f.Key, Class: c)))
            .GroupBy(static x => x.Class.Identifier.ValueText)
            .ToDictionary(static g => g.Key, static g => g.ToList());

        var pending = new Dictionary<(string File, int Start), List<string>>();
        var classInfo = new Dictionary<(string File, int Start), ClassDeclarationSyntax>();

        foreach (var (file, stmt, analysis) in convertible)
        {
            var implName = SimpleTypeName(analysis.ImplementationType);
            if (!classes.TryGetValue(implName, out var candidates) || candidates.Count != 1 && !candidates.All(static c => c.Class.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Downgrade(analysis, candidates is null
                    ? "Implementation class not found in the analyzed sources"
                    : "Multiple classes share this name; add the attribute manually");
                continue;
            }

            var target = candidates[0];
            var attrNames = target.Class.AttributeLists.SelectMany(static l => l.Attributes)
                .Select(static a => a.Name.ToString().Split('.').Last())
                .ToList();
            if (attrNames.Any(static n => n is "Scoped" or "Singleton" or "Transient" or "ScopedAttribute" or "SingletonAttribute" or "TransientAttribute"))
            {
                Downgrade(analysis, "Class already has an AutoWire lifetime attribute; merge manually");
                continue;
            }

            var key = (target.File, target.Class.SpanStart);
            if (!pending.TryGetValue(key, out var list)) pending[key] = list = new List<string>();
            list.Add(analysis.SuggestedAttribute);
            classInfo[key] = target.Class;

            var text = files[file].Source;
            var start = stmt.SpanStart;
            while (start > 0 && text[start - 1] is ' ' or '\t') start--;
            edits.Add(new RegistrationEdit(file, start, stmt.FullSpan.End - start, string.Empty));
        }

        foreach (var (key, attrs) in pending)
        {
            var text = files[key.File].Source;
            var cls = classInfo[key];
            var insertAt = cls.AttributeLists.Count > 0 ? cls.AttributeLists[0].SpanStart : cls.SpanStart;
            var lineStart = insertAt;
            while (lineStart > 0 && text[lineStart - 1] is not '\n') lineStart--;
            var indent = text.Substring(lineStart, insertAt - lineStart);
            if (indent.Trim().Length > 0) indent = string.Empty;
            var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var sb = new StringBuilder();
            foreach (var attr in attrs.Distinct(StringComparer.Ordinal)) sb.Append(attr).Append(eol).Append(indent);
            edits.Add(new RegistrationEdit(key.File, insertAt, 0, sb.ToString()));
        }

        return edits;
    }

    static void Downgrade(RegistrationAnalysis analysis, string reason)
    {
        analysis.Reasons.Add(reason);
        analysis.Confidence = MigrationConfidence.NeedsReview;
    }


    static string SimpleTypeName(string type)
    {
        var lt = type.IndexOf('<');
        var name = lt >= 0 ? type.Substring(0, lt) : type;
        return name.Split('.').Last().Trim();
    }

    public static string ApplyEdits(string source, IEnumerable<RegistrationEdit> edits)
    {
        var sb = new StringBuilder(source);
        foreach (var e in edits.OrderByDescending(static e => e.Start).ThenByDescending(static e => e.Length))
        {
            sb.Remove(e.Start, e.Length);
            sb.Insert(e.Start, e.NewText);
        }
        return sb.ToString();
    }
}
