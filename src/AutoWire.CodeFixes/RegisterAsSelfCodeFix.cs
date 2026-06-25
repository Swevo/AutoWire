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
/// AW007 — Adds <c>ServiceType = typeof(ClassName)</c> as a constructor argument to the
/// registration attribute, making the concrete self-registration explicit and suppressing
/// the "no interface abstraction" warning.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RegisterAsSelfCodeFix)), Shared]
public sealed class RegisterAsSelfCodeFix : CodeFixProvider
{
    private static readonly ImmutableArray<string> _fixableDiagnosticIds =
        ImmutableArray.Create("AW007");

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

        var className = classDecl.Identifier.Text;

        // Find the first registration attribute without an existing ServiceType argument
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (!IsRegistrationAttribute(attr.Name.ToString())) continue;

                // Skip if already has a ServiceType constructor or named argument
                if (attr.ArgumentList?.Arguments.Any(
                        a => a.NameEquals?.Name.Identifier.Text == "ServiceType") == true)
                    continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Register as self: [Scoped(typeof({className}))] — suppresses AW007",
                        createChangedDocument: ct => AddServiceTypeAsync(context.Document, root, attr, className, ct),
                        equivalenceKey: nameof(RegisterAsSelfCodeFix)),
                    diagnostic);
                return;
            }
        }
    }

    private static Task<Document> AddServiceTypeAsync(
        Document document,
        SyntaxNode root,
        AttributeSyntax attr,
        string className,
        CancellationToken _)
    {
        // Build: typeof(ClassName) as a positional argument
        var typeofExpr = SyntaxFactory.TypeOfExpression(
            SyntaxFactory.IdentifierName(className));
        var newArg = SyntaxFactory.AttributeArgument(typeofExpr);

        AttributeSyntax newAttr;
        if (attr.ArgumentList is null || attr.ArgumentList.Arguments.Count == 0)
        {
            var newArgList = SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(newArg));
            newAttr = attr.WithArgumentList(newArgList);
        }
        else
        {
            // Prepend to existing arguments
            var separator = SyntaxFactory.Token(SyntaxKind.CommaToken)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var newArgs = attr.ArgumentList.Arguments.Insert(0, newArg);
            var separators = Enumerable.Repeat(separator, newArgs.Count - 1);
            var newSeparatedList = SyntaxFactory.SeparatedList(newArgs, separators);
            var newArgList = attr.ArgumentList.WithArguments(newSeparatedList);
            newAttr = attr.WithArgumentList(newArgList);
        }

        var newRoot = root.ReplaceNode(attr, newAttr);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
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
