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
/// AW010 — Removes a duplicate <c>[Interceptor]</c> attribute when two or more
/// <c>[Interceptor]</c> attributes on the same class target the same interface.
/// The fix removes the attribute at the diagnostic location (the duplicate).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveDuplicateInterceptorCodeFix)), Shared]
public sealed class RemoveDuplicateInterceptorCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW010");

    public override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnosticIds;

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Walk up to find the attribute or attribute list at the diagnostic location
        var attr = node as AttributeSyntax
            ?? node.FirstAncestorOrSelf<AttributeSyntax>();

        if (attr is null)
        {
            // Fallback: locate by class and find the duplicate Interceptor attribute
            var classDecl = node as ClassDeclarationSyntax
                ?? node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            if (classDecl is null) return;

            attr = FindDuplicateInterceptorAttribute(classDecl);
            if (attr is null) return;
        }

        var attrList = attr.Parent as AttributeListSyntax;
        if (attrList is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Remove duplicate [Interceptor] attribute",
                createChangedDocument: ct => RemoveAttributeAsync(context.Document, root, attrList, attr, ct),
                equivalenceKey: nameof(RemoveDuplicateInterceptorCodeFix)),
            diagnostic);
    }

    private static AttributeSyntax? FindDuplicateInterceptorAttribute(ClassDeclarationSyntax classDecl)
    {
        // Return the second [Interceptor] attribute found (the duplicate)
        int count = 0;
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (IsInterceptorAttribute(attr.Name.ToString()))
                {
                    count++;
                    if (count >= 2) return attr;
                }
            }
        }
        return null;
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
