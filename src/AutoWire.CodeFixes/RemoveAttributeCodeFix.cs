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
/// AW001 — Removes the AutoWire registration attribute from an abstract class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveAttributeCodeFix)), Shared]
public sealed class RemoveAttributeCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW001");

    public override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnosticIds;

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Walk up to the class declaration
        var classDecl = node as ClassDeclarationSyntax
            ?? node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl is null) return;

        // Find the attribute list containing the AutoWire attribute
        var attrName = ExtractAttributeName(diagnostic.Descriptor.MessageFormat.ToString());
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (IsAutoWireRegistrationAttribute(name))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: $"Remove [{TrimAttributeSuffix(name)}] (class is abstract)",
                            createChangedDocument: ct => RemoveAttributeAsync(context.Document, root, attrList, attr, ct),
                            equivalenceKey: nameof(RemoveAttributeCodeFix)),
                        diagnostic);
                    return;
                }
            }
        }
    }

    private static async Task<Document> RemoveAttributeAsync(
        Document document,
        SyntaxNode root,
        AttributeListSyntax attrList,
        AttributeSyntax attr,
        CancellationToken cancellationToken)
    {
        SyntaxNode newRoot;

        if (attrList.Attributes.Count == 1)
        {
            // Remove the entire attribute list
            newRoot = root.RemoveNode(attrList, SyntaxRemoveOptions.KeepLeadingTrivia)!;
        }
        else
        {
            // Remove just this attribute from the list
            var newAttrList = attrList.RemoveNode(attr, SyntaxRemoveOptions.KeepLeadingTrivia)!;
            newRoot = root.ReplaceNode(attrList, newAttrList);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsAutoWireRegistrationAttribute(string name)
    {
        var trimmed = TrimAttributeSuffix(name);
        return trimmed is "Scoped" or "Singleton" or "Transient"
            or "TryScoped" or "TrySingleton" or "TryTransient"
            or "HostedService"
            or "DecorateScoped" or "DecorateSingleton" or "DecorateTransient";
    }

    private static string TrimAttributeSuffix(string name)
    {
        var last = name.LastIndexOf('.');
        var simple = last >= 0 ? name.Substring(last + 1) : name;
        return simple.EndsWith("Attribute") ? simple.Substring(0, simple.Length - 9) : simple;
    }

    private static string ExtractAttributeName(string message) => message;
}
