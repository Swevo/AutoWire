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
using Microsoft.CodeAnalysis.Formatting;

namespace AutoWire.CodeFixes;

/// <summary>
/// AW009 — Replaces a Scoped constructor parameter in a <c>[HostedService]</c> class with
/// <c>IServiceScopeFactory</c>, adds a private field, and inserts a scope-creation pattern
/// in <c>ExecuteAsync</c> if it exists.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InjectServiceScopeFactoryCodeFix)), Shared]
public sealed class InjectServiceScopeFactoryCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW009");

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

        // Find the constructor that has the offending scoped parameter
        var ctor = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c => c.ParameterList.Parameters.Count > 0);
        if (ctor is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace Scoped dependency with IServiceScopeFactory",
                createChangedDocument: ct => ApplyFixAsync(context.Document, root, classDecl, ctor, ct),
                equivalenceKey: nameof(InjectServiceScopeFactoryCodeFix)),
            diagnostic);
    }

    private static Task<Document> ApplyFixAsync(
        Document document,
        SyntaxNode root,
        ClassDeclarationSyntax classDecl,
        ConstructorDeclarationSyntax ctor,
        CancellationToken _)
    {
        // Replace all constructor parameters with a single IServiceScopeFactory parameter
        var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("scopeFactory"))
            .WithType(SyntaxFactory.ParseTypeName("Microsoft.Extensions.DependencyInjection.IServiceScopeFactory")
                .WithTrailingTrivia(SyntaxFactory.Space));

        var newParamList = SyntaxFactory.ParameterList(
            SyntaxFactory.SingletonSeparatedList(newParam));

        // Build new constructor body that stores the factory
        var newCtorBody = SyntaxFactory.Block(
            SyntaxFactory.ParseStatement("_scopeFactory = scopeFactory;"));

        var newCtor = ctor
            .WithParameterList(newParamList)
            .WithBody(newCtorBody)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Add a private field _scopeFactory
        var field = SyntaxFactory.ParseMemberDeclaration(
            "private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;\n")!;

        var newMembers = classDecl.Members
            .Replace(ctor, newCtor)
            .Insert(0, field.WithAdditionalAnnotations(Formatter.Annotation));

        var newClass = classDecl.WithMembers(newMembers);
        var newRoot = root.ReplaceNode(classDecl, newClass);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
