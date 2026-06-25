using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoWire.CodeFixes;

/// <summary>
/// AW003 — Removes the explicit <c>ServiceType</c> named argument from a registration attribute
/// when the implementation type does not implement the specified service type.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveServiceTypeArgCodeFix)), Shared]
public sealed class RemoveServiceTypeArgCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW003");

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

        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (attr.ArgumentList is null) continue;

                // Find the ServiceType named argument
                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    if (arg.NameEquals?.Name.Identifier.Text == "ServiceType")
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Remove explicit ServiceType (class does not implement it)",
                                createChangedDocument: ct => RemoveNamedArgAsync(context.Document, root, attr, arg, ct),
                                equivalenceKey: nameof(RemoveServiceTypeArgCodeFix)),
                            diagnostic);
                        return;
                    }
                }
            }
        }
    }

    private static Task<Document> RemoveNamedArgAsync(
        Document document,
        SyntaxNode root,
        AttributeSyntax attr,
        AttributeArgumentSyntax argToRemove,
        CancellationToken _)
    {
        SyntaxNode newRoot;

        if (attr.ArgumentList!.Arguments.Count == 1)
        {
            // Remove the entire argument list, leaving [Scoped] or similar
            var newAttr = attr.RemoveNode(attr.ArgumentList, SyntaxRemoveOptions.KeepLeadingTrivia)!;
            newRoot = root.ReplaceNode(attr, newAttr);
        }
        else
        {
            var newArgList = attr.ArgumentList.RemoveNode(argToRemove, SyntaxRemoveOptions.KeepLeadingTrivia)!;
            newRoot = root.ReplaceNode(attr.ArgumentList, newArgList);
        }

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
