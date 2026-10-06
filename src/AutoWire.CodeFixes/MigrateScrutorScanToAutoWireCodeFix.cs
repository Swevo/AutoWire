using System;
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
/// AW026 — Replaces a Scrutor Scan() call with AddAutoWireServices() as a starting point
/// and leaves an inline migration note for parity verification.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MigrateScrutorScanToAutoWireCodeFix)), Shared]
public sealed class MigrateScrutorScanToAutoWireCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW026");

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

        if (invocation.Parent is not ExpressionStatementSyntax exprStmt) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var receiver = memberAccess.Expression.ToString();
        if (string.IsNullOrWhiteSpace(receiver)) return;

        var invocationText = invocation.ToString();
        var sourceSelector = GetSourceSelector(invocationText);
        var notes = GetChainNotes(invocationText);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace Scrutor Scan() with AddAutoWireServices() migration scaffold",
                createChangedDocument: ct => ReplaceWithAutoWireScaffoldAsync(
                    context.Document,
                    root,
                    exprStmt,
                    receiver,
                    sourceSelector,
                    notes,
                    ct),
                equivalenceKey: nameof(MigrateScrutorScanToAutoWireCodeFix)),
            diagnostic);
    }

    private static Task<Document> ReplaceWithAutoWireScaffoldAsync(
        Document document,
        SyntaxNode root,
        ExpressionStatementSyntax expressionStatement,
        string receiverExpression,
        string sourceSelector,
        string chainNotes,
        CancellationToken _)
    {
        var scaffold = $@"// AutoWire migration scaffold (AW026):
// - Replace this with attribute-driven registrations or [assembly: AutoWireScan(...)].
// - Source selector: {sourceSelector}
// - Chain hints: {chainNotes}
// - Verify parity with RegistrationSummary manifest diff in CI.
{receiverExpression}.AddAutoWireServices();";

        var replacement = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(scaffold)
            .WithLeadingTrivia(expressionStatement.GetLeadingTrivia())
            .WithTrailingTrivia(expressionStatement.GetTrailingTrivia());

        var newRoot = root.ReplaceNode(expressionStatement, replacement);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    private static string GetSourceSelector(string invocationText)
    {
        if (invocationText.Contains(".FromAssemblyOf<", StringComparison.Ordinal))
            return "FromAssemblyOf";
        if (invocationText.Contains(".FromAssembliesOf<", StringComparison.Ordinal))
            return "FromAssembliesOf";
        if (invocationText.Contains(".FromCallingAssembly(", StringComparison.Ordinal))
            return "FromCallingAssembly";
        return "Unknown";
    }

    private static string GetChainNotes(string invocationText)
    {
        var asMode = invocationText.Contains(".AsImplementedInterfaces(", StringComparison.Ordinal) ? "AsImplementedInterfaces"
            : invocationText.Contains(".AsSelf(", StringComparison.Ordinal) ? "AsSelf"
            : (invocationText.Contains(".As<", StringComparison.Ordinal) || invocationText.Contains(".As(typeof(", StringComparison.Ordinal)) ? "As<T>/As(typeof)"
            : "UnknownAs";

        var lifetime = invocationText.Contains(".WithScopedLifetime(", StringComparison.Ordinal) ? "Scoped"
            : invocationText.Contains(".WithSingletonLifetime(", StringComparison.Ordinal) ? "Singleton"
            : invocationText.Contains(".WithTransientLifetime(", StringComparison.Ordinal) ? "Transient"
            : "UnknownLifetime";

        return $"as={asMode}, lifetime={lifetime}";
    }
}
