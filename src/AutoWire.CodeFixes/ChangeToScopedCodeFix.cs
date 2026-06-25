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
/// AW006 — Changes a <c>[Transient]</c> or <c>[TryTransient]</c> attribute to
/// <c>[Scoped]</c> / <c>[TryScoped]</c> when the class implements IDisposable or IAsyncDisposable,
/// so the container disposes the instance at the end of each scope.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ChangeToScopedCodeFix)), Shared]
public sealed class ChangeToScopedCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW006");

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
                var (isTransient, isTry) = ClassifyAttribute(attr.Name.ToString());
                if (!isTransient) continue;

                var newAttrName = isTry ? "TryScoped" : "Scoped";
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Change to [{newAttrName}] (disposable services should be Scoped)",
                        createChangedDocument: ct => ReplaceAttributeNameAsync(context.Document, root, attr, newAttrName, ct),
                        equivalenceKey: nameof(ChangeToScopedCodeFix)),
                    diagnostic);
                return;
            }
        }
    }

    private static Task<Document> ReplaceAttributeNameAsync(
        Document document,
        SyntaxNode root,
        AttributeSyntax attr,
        string newName,
        CancellationToken _)
    {
        var newAttrName = SyntaxFactory.ParseName(newName)
            .WithTriviaFrom(attr.Name);
        var newAttr = attr.WithName(newAttrName);
        var newRoot = root.ReplaceNode(attr, newAttr);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    private static (bool isTransient, bool isTry) ClassifyAttribute(string name)
    {
        var last = name.LastIndexOf('.');
        var simple = last >= 0 ? name.Substring(last + 1) : name;
        if (simple.EndsWith("Attribute")) simple = simple.Substring(0, simple.Length - 9);

        return simple switch
        {
            "Transient"    => (true, false),
            "TryTransient" => (true, true),
            _              => (false, false)
        };
    }
}
