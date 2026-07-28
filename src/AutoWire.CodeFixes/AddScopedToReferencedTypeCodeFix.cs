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
/// AW013 — Best-effort fix that adds <c>[Scoped]</c> to the class declaration of the constructor
/// parameter type that AutoWire could not find a registration for, when that type is a concrete,
/// non-abstract class declared in the same project. Skipped for interfaces, abstract classes, or
/// types declared in a referenced assembly (no syntax to edit).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddScopedToReferencedTypeCodeFix)), Shared]
public sealed class AddScopedToReferencedTypeCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW013");

    public override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnosticIds;

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var paramNode = node as ParameterSyntax ?? node.FirstAncestorOrSelf<ParameterSyntax>();
        if (paramNode is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        if (semanticModel.GetDeclaredSymbol(paramNode, context.CancellationToken) is not IParameterSymbol paramSymbol) return;
        if (paramSymbol.Type is not INamedTypeSymbol paramType) return;

        // Best-effort only: skip interfaces, abstract classes, generics, and anything not from source.
        if (paramType.TypeKind != TypeKind.Class) return;
        if (paramType.IsAbstract || paramType.IsGenericType) return;

        var declaringRef = paramType.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaringRef is null) return; // declared in a referenced assembly — nothing to edit

        var targetDocument = context.Document.Project.Solution.GetDocument(declaringRef.SyntaxTree);
        if (targetDocument is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [Scoped] to '{paramType.Name}' — suppresses AW013",
                createChangedSolution: ct => AddScopedAttributeAsync(targetDocument, declaringRef, ct),
                equivalenceKey: nameof(AddScopedToReferencedTypeCodeFix)),
            diagnostic);
    }

    private static async Task<Solution> AddScopedAttributeAsync(
        Document document,
        SyntaxReference declaringRef,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document.Project.Solution;

        if (declaringRef.GetSyntax(cancellationToken) is not ClassDeclarationSyntax classDecl)
            return document.Project.Solution;

        // Already has a registration attribute (or was fixed already) — nothing to do.
        if (classDecl.AttributeLists.Any(al => al.Attributes.Any(a => IsRegistrationAttributeName(a.Name.ToString()))))
            return document.Project.Solution;

        var newAttrList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName("AutoWire.Scoped"))))
            .WithLeadingTrivia(classDecl.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        var newClassDecl = classDecl
            .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
            .WithAttributeLists(classDecl.AttributeLists.Insert(0, newAttrList));

        var newRoot = root.ReplaceNode(classDecl, newClassDecl);
        return document.WithSyntaxRoot(newRoot).Project.Solution;
    }

    private static bool IsRegistrationAttributeName(string name)
    {
        var last = name.LastIndexOf('.');
        var simple = last >= 0 ? name.Substring(last + 1) : name;
        if (simple.EndsWith("Attribute")) simple = simple.Substring(0, simple.Length - 9);
        return simple is "Scoped" or "Singleton" or "Transient"
            or "TryScoped" or "TrySingleton" or "TryTransient";
    }
}
