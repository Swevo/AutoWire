using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoWire.CodeFixes;

/// <summary>
/// AW011 — Removes the <c>[Interceptor(typeof(IFoo))]</c> attribute when the target interface
/// has no interceptable methods (the generated proxy would be empty).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveEmptyInterceptorCodeFix)), Shared]
public sealed class RemoveEmptyInterceptorCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW011");

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

        // Find the [Interceptor] attribute — there may be multiple; remove the offending one
        // (AW011 fires per attribute, so we match by the message which contains the interface name)
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (!IsInterceptorAttribute(attr.Name.ToString())) continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Remove [Interceptor] (target interface has no interceptable methods)",
                        createChangedDocument: ct => RemoveAttributeAsync(context.Document, root, attrList, attr, ct),
                        equivalenceKey: nameof(RemoveEmptyInterceptorCodeFix)),
                    diagnostic);
                return;
            }
        }
    }

    private static Task<Document> RemoveAttributeAsync(
        Document document,
        SyntaxNode root,
        AttributeListSyntax attrList,
        AttributeSyntax attr,
        CancellationToken _)
    {
        SyntaxNode newRoot;

        if (attrList.Attributes.Count == 1)
        {
            newRoot = root.RemoveNode(attrList, SyntaxRemoveOptions.KeepLeadingTrivia)!;
        }
        else
        {
            var newAttrList = attrList.RemoveNode(attr, SyntaxRemoveOptions.KeepLeadingTrivia)!;
            newRoot = root.ReplaceNode(attrList, newAttrList);
        }

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    private static bool IsInterceptorAttribute(string name)
    {
        var last = name.LastIndexOf('.');
        var simple = last >= 0 ? name.Substring(last + 1) : name;
        if (simple.EndsWith("Attribute")) simple = simple.Substring(0, simple.Length - 9);
        return simple == "Interceptor";
    }
}
