using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoWire.VisualStudio.Adornment
{
    /// <summary>A single AutoWire registration hint found in an open document, ready to render as "AutoWire: ...".</summary>
    internal sealed class RegistrationHint
    {
        /// <summary>0-based offset, in the snapshot the hint was parsed from, of the end of the class declaration line (where the adornment should be inserted).</summary>
        public int InsertionPosition { get; }

        /// <summary>The text to display, e.g. "AutoWire: Scoped → IFoo, IBar (self)".</summary>
        public string Text { get; }

        public RegistrationHint(int insertionPosition, string text)
        {
            InsertionPosition = insertionPosition;
            Text = text;
        }
    }

    /// <summary>
    /// Best-effort regex-based scanner that finds AutoWire registration attributes and their associated class
    /// declarations in a C# document's text. Deliberately avoids full Roslyn semantic analysis so it stays cheap
    /// enough to re-run on every text-buffer change; never throws — any parse failure is skipped silently.
    /// </summary>
    internal static class AutoWireRegistrationParser
    {
        private static readonly string[] LifetimeAttributes =
        {
            "Scoped", "Singleton", "Transient", "TryScoped", "TrySingleton", "TryTransient",
        };

        private static readonly string[] AllAttributes =
            LifetimeAttributes.Concat(new[] { "HostedService", "Factory", "Validate", "Options", "HttpClient" }).ToArray();

        // Matches [AttrName] or [AttrName(...)], capturing the attribute name and its (unparsed) argument text.
        private static readonly Regex AttributeRegex = new Regex(
            @"\[\s*(?<name>" + string.Join("|", AllAttributes) + @")\s*(\((?<args>[^\]]*)\))?\s*\]",
            RegexOptions.Compiled);

        // Matches the head of a class declaration: modifiers/keywords are skipped by searching forward instead of
        // being matched explicitly, since they can appear in any order/combination.
        private static readonly Regex ClassHeadRegex = new Regex(
            @"\bclass\s+(?<name>[A-Za-z_]\w*)\s*(?<generic><[^>{;]*>)?\s*(:\s*(?<bases>[^{;]+))?\{",
            RegexOptions.Compiled);

        private static readonly Regex TypeofRegex = new Regex(@"typeof\s*\(\s*(?<type>[A-Za-z_][\w\.]*(?:<[^>()]*>)?)\s*\)", RegexOptions.Compiled);

        private static readonly Regex StringArgRegex = new Regex(@"^\s*""(?<value>[^""]*)""", RegexOptions.Compiled);

        /// <summary>Scans <paramref name="text"/> for AutoWire attribute usages and returns one hint per match. Never throws.</summary>
        public static IReadOnlyList<RegistrationHint> Parse(string text)
        {
            var hints = new List<RegistrationHint>();
            if (string.IsNullOrEmpty(text)) return hints;

            try
            {
                foreach (Match attrMatch in AttributeRegex.Matches(text))
                {
                    var hint = TryBuildHint(text, attrMatch);
                    if (hint != null) hints.Add(hint);
                }
            }
            catch
            {
                // Best-effort: never let a malformed/partial document crash the editor pipeline.
            }

            return hints;
        }

        private static RegistrationHint TryBuildHint(string text, Match attrMatch)
        {
            var searchStart = attrMatch.Index + attrMatch.Length;
            // Search only within a reasonable window so a stray attribute far from any class doesn't scan the whole file.
            var windowEnd = System.Math.Min(text.Length, searchStart + 2000);
            var window = text.Substring(searchStart, windowEnd - searchStart);

            var classMatch = ClassHeadRegex.Match(window);
            if (!classMatch.Success) return null;

            var className = classMatch.Groups["name"].Value;
            var attributeName = attrMatch.Groups["name"].Value;
            var args = attrMatch.Groups["args"].Success ? attrMatch.Groups["args"].Value : string.Empty;
            var bases = classMatch.Groups["bases"].Success ? SplitBaseList(classMatch.Groups["bases"].Value) : new List<string>();

            var insertionPosition = searchStart + classMatch.Index + classMatch.Length; // right after the class's opening '{'

            string hintText;
            if (LifetimeAttributes.Contains(attributeName))
            {
                hintText = BuildLifetimeHint(attributeName, args, bases);
            }
            else
            {
                hintText = attributeName switch
                {
                    "HostedService" => "AutoWire: HostedService",
                    "Factory" => BuildFactoryHint(args),
                    "Validate" => BuildValidateHint(bases),
                    "Options" => BuildOptionsHint(args, className),
                    "HttpClient" => BuildHttpClientHint(args),
                    _ => $"AutoWire: {attributeName}",
                };
            }

            return new RegistrationHint(insertionPosition, hintText);
        }

        private static string BuildLifetimeHint(string attributeName, string args, List<string> bases)
        {
            var explicitTypes = TypeofRegex.Matches(args).Cast<Match>().Select(m => m.Groups["type"].Value).ToList();
            var types = explicitTypes.Count > 0 ? explicitTypes : bases.Where(LooksLikeInterface).ToList();

            var includeSelf = Regex.IsMatch(args, @"\bIncludeSelf\s*=\s*true\b");

            var typesText = types.Count > 0 ? string.Join(", ", types) : null;
            var suffix = includeSelf ? (typesText != null ? " (self)" : "(self)") : string.Empty;

            return typesText != null
                ? $"AutoWire: {attributeName} → {typesText}{suffix}"
                : $"AutoWire: {attributeName}{(includeSelf ? " → (self)" : string.Empty)}";
        }

        private static string BuildFactoryHint(string args)
        {
            var match = TypeofRegex.Match(args);
            return match.Success ? $"AutoWire: Factory → {match.Groups["type"].Value}" : "AutoWire: Factory";
        }

        private static string BuildValidateHint(List<string> bases)
        {
            var abstractValidator = bases.FirstOrDefault(b => b.StartsWith("AbstractValidator<"));
            if (abstractValidator != null)
            {
                var inner = abstractValidator.Substring("AbstractValidator<".Length).TrimEnd('>');
                return $"AutoWire: Validate → IValidator<{inner}>";
            }

            return "AutoWire: Validate";
        }

        private static string BuildOptionsHint(string args, string className)
        {
            var match = StringArgRegex.Match(args);
            var section = match.Success ? match.Groups["value"].Value : StripOptionsSuffix(className);
            return $"AutoWire: Options → \"{section}\"";
        }

        private static string BuildHttpClientHint(string args)
        {
            var nameMatch = Regex.Match(args, @"Name\s*=\s*""(?<value>[^""]*)""");
            return nameMatch.Success ? $"AutoWire: HttpClient → \"{nameMatch.Groups["value"].Value}\"" : "AutoWire: HttpClient";
        }

        private static string StripOptionsSuffix(string className) =>
            className.EndsWith("Options") && className.Length > "Options".Length
                ? className.Substring(0, className.Length - "Options".Length)
                : className;

        private static bool LooksLikeInterface(string typeName) =>
            typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1]);

        private static List<string> SplitBaseList(string rawBaseList)
        {
            // Strip any `where T : ...` generic constraint clause before splitting on commas.
            var whereIndex = Regex.Match(rawBaseList, @"\bwhere\b");
            var baseListOnly = whereIndex.Success ? rawBaseList.Substring(0, whereIndex.Index) : rawBaseList;

            var result = new List<string>();
            var depth = 0;
            var current = new System.Text.StringBuilder();
            foreach (var ch in baseListOnly)
            {
                if (ch == '<') depth++;
                if (ch == '>') depth--;
                if (ch == ',' && depth <= 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            if (current.Length > 0) result.Add(current.ToString().Trim());
            return result.Where(s => s.Length > 0).ToList();
        }
    }
}
