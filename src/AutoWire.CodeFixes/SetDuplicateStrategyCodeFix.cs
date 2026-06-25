using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoWire.CodeFixes;

/// <summary>
/// AW002 — Adds <c>Duplicate = DuplicateStrategy.Replace</c> or
/// <c>Duplicate = DuplicateStrategy.Skip</c> to a registration attribute so the duplicate
/// resolution intent is made explicit.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SetDuplicateStrategyCodeFix)), Shared]
public sealed class SetDuplicateStrategyCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW002");

    public override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnosticIds;

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        var classDecl = node as ClassDeclarationSyntax
            ?? node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl is null) return;

        // Find the first registration attribute without a Duplicate argument
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (!IsRegistrationAttribute(attr.Name.ToString())) continue;

                // Skip if already has a Duplicate argument
                if (attr.ArgumentList?.Arguments.Any(
                        a => a.NameEquals?.Name.Identifier.Text == "Duplicate") == true)
                    continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Add Duplicate = DuplicateStrategy.Replace (this registration wins)",
                        createChangedDocument: ct => AddDuplicateArgAsync(context.Document, root, attr, "Replace", ct),
                        equivalenceKey: $"{nameof(SetDuplicateStrategyCodeFix)}.Replace"),
                    diagnostic);

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Add Duplicate = DuplicateStrategy.Skip (keep existing registration)",
                        createChangedDocument: ct => AddDuplicateArgAsync(context.Document, root, attr, "Skip", ct),
                        equivalenceKey: $"{nameof(SetDuplicateStrategyCodeFix)}.Skip"),
                    diagnostic);

                return;
            }
        }
    }

    private static Task<Document> AddDuplicateArgAsync(
        Document document,
        SyntaxNode root,
        AttributeSyntax attr,
        string strategyValue,
        CancellationToken _)
    {
        var newArg = SyntaxFactory.AttributeArgument(
            SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName("Duplicate")),
            null,
            SyntaxFactory.ParseExpression($"AutoWire.DuplicateStrategy.{strategyValue}"));

        AttributeListSyntax newAttrList;
        if (attr.ArgumentList is null)
        {
            var newArgList = SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(newArg));
            newAttrList = attr.Parent as AttributeListSyntax ?? throw new System.InvalidOperationException();
            var newAttr = attr.WithArgumentList(newArgList);
            var newRoot = root.ReplaceNode(attr, newAttr);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }
        else
        {
            var separator = SyntaxFactory.Token(SyntaxKind.CommaToken)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var newArgs = attr.ArgumentList.Arguments.Add(newArg);
            var newSeparatedList = SyntaxFactory.SeparatedList(
                newArgs,
                Enumerable.Repeat(separator, newArgs.Count - 1));
            var newArgList = attr.ArgumentList.WithArguments(newSeparatedList);
            var newAttr = attr.WithArgumentList(newArgList);
            var newRoot = root.ReplaceNode(attr, newAttr);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }
    }

    private static bool IsRegistrationAttribute(string name)
    {
        var last = name.LastIndexOf('.');
        var simple = last >= 0 ? name.Substring(last + 1) : name;
        if (simple.EndsWith("Attribute")) simple = simple.Substring(0, simple.Length - 9);
        return simple is "Scoped" or "Singleton" or "Transient"
            or "TryScoped" or "TrySingleton" or "TryTransient";
    }
}
