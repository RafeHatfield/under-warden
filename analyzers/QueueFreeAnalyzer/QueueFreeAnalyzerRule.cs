using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnderWarden.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class QueueFreeAnalyzerRule : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "YARL001";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            id: DiagnosticId,
            title: "Use SafeFree() instead of QueueFree()",
            messageFormat: "Bare QueueFree() leaves the node in the tree until end-of-frame, causing ghost node bugs in layout containers. Use SafeFree() which calls RemoveChild first.",
            category: "Reliability",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "SafeFree() calls RemoveChild before QueueFree, preventing ghost nodes in layout containers."
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            // Do not report diagnostics in generated code.
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            // Required for correctness in multi-threaded analysis.
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            // Extract the rightmost identifier from the invocation expression.
            // Handles both bare `QueueFree()` and member access `node.QueueFree()`.
            string methodName = GetMethodName(invocation.Expression);
            if (methodName != "QueueFree")
                return;

            // Walk up the syntax tree to find the containing method declaration.
            // If we are inside a method named "SafeFree", skip — that is the
            // one intentional bare call that implements the safe wrapper itself.
            SyntaxNode parent = invocation.Parent;
            while (parent != null)
            {
                if (parent is MethodDeclarationSyntax methodDecl)
                {
                    if (methodDecl.Identifier.Text == "SafeFree")
                        return;
                    break;
                }
                parent = parent.Parent;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.GetLocation()));
        }

        private static string GetMethodName(ExpressionSyntax expression)
        {
            // node.QueueFree()  ->  MemberAccessExpression
            if (expression is MemberAccessExpressionSyntax memberAccess)
                return memberAccess.Name.Identifier.Text;

            // QueueFree()  ->  IdentifierNameSyntax
            if (expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.Text;

            return string.Empty;
        }
    }
}
