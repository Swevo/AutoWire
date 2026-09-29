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
/// AW018 — Adds the corresponding AutoWire registration attribute to the implementation class
/// for manual IServiceCollection registrations like AddScoped&lt;TService, TImpl&gt;.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MigrateManualRegistrationToAutoWireCodeFix)), Shared]
public sealed class MigrateManualRegistrationToAutoWireCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW018");

    public override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnosticIds;

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var invocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method) return;
        if (method.TypeArguments.Length != 2) return;
        if (method.TypeArguments[1] is not INamedTypeSymbol implType) return;

        var attrName = method.Name switch
        {
            "AddScoped" => "Scoped",
            "AddSingleton" => "Singleton",
            "AddTransient" => "Transient",
            "TryAddScoped" => "TryScoped",
            "TryAddSingleton" => "TrySingleton",
            "TryAddTransient" => "TryTransient",
            _ => null
        };
        if (attrName is null) return;

        var declaringRef = implType.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaringRef is null) return;

        var targetDocument = context.Document.Project.Solution.GetDocument(declaringRef.SyntaxTree);
        if (targetDocument is null) return;

        var serviceTypeDisplay = method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [AutoWire.{attrName}(typeof({serviceTypeDisplay}))] to '{implType.Name}'",
                createChangedSolution: ct => AddAutoWireAttributeAsync(
                    targetDocument,
                    declaringRef,
                    attrName,
                    method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ct),
                equivalenceKey: nameof(MigrateManualRegistrationToAutoWireCodeFix)),
            diagnostic);
    }

    private static async Task<Solution> AddAutoWireAttributeAsync(
        Document document,
        SyntaxReference declaringRef,
        string attributeName,
        string fullyQualifiedServiceType,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document.Project.Solution;

        if (declaringRef.GetSyntax(cancellationToken) is not ClassDeclarationSyntax classDecl)
            return document.Project.Solution;

        if (classDecl.AttributeLists.Any(al => al.Attributes.Any(a => IsRegistrationAttributeName(a.Name.ToString()))))
            return document.Project.Solution;

        var attrText = $"AutoWire.{attributeName}(typeof({fullyQualifiedServiceType}))";
        var newAttrList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName(attrText))))
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
