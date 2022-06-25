using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ExtensionGen;

[Generator]
public class ExtensionMethodGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var extensionMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsTargetForGeneration(s),
                transform: static (ctx, _) => GetTargetForGeneration(ctx));

        context.RegisterSourceOutput(
            extensionMethods.Collect(),
            static (spc, source) => ExecuteAddExtensionMethods(source, spc));
    }

    private static void ExecuteAddExtensionMethods(ImmutableArray<(ClassDeclarationSyntax ClassDeclarationSyntax, MethodDeclarationSyntax MethodDeclarationSyntax, IMethodSymbol MethodModel)> source, SourceProductionContext spc)
    {
        var items = source.GroupBy(
                c => c.ClassDeclarationSyntax,
                i => (i.MethodDeclarationSyntax, i.MethodModel)
            );

        var sb = new StringBuilder();

        foreach (var item in items)
        {
            var ns = GetNamespace(item.Key);
            var originalClassName = item.Key.Identifier.Text;
            var extensionClassName = $"{originalClassName}Extensions";

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }

            if (item.Key.Modifiers.Count > 0)
            {
                sb.Append($"{item.Key.Modifiers.ToFullString().Trim()} ");
            }

            sb.Append($"static class {extensionClassName}");
            sb.AppendLine("{");

            foreach (var (methodSyntax, methodSymbol) in item)
            {
                var originalMethodName = methodSymbol.Name;
                var newMethodName = originalMethodName.Substring(0, originalMethodName.LastIndexOf("Extension", StringComparison.Ordinal));

                var parameters = string.Join(",", methodSymbol.Parameters.Select(p => $"{p.Type} {p.Name}"));
                var arguments = string.Join(",", methodSymbol.Parameters.Select(p => p.Name));

                sb.Append("    ");
                if (methodSyntax.Modifiers.Count > 0)
                {
                    sb.Append($"{methodSyntax.Modifiers.ToFullString()} ");
                }
                sb.Append($"{methodSymbol.ReturnType} {newMethodName}(this {parameters})");
                sb.AppendLine($" => {methodSymbol.ContainingType}.{methodSymbol.Name} ({arguments});");
            }

            sb.AppendLine("}");

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("}");
            }
        }

        spc.AddSource("gen.c.cs", sb.ToString());
    }


    private static (ClassDeclarationSyntax classDeclarationSyntax, MethodDeclarationSyntax methodDeclarationSyntax, IMethodSymbol methodModel) GetTargetForGeneration(GeneratorSyntaxContext ctx)
    {
        var methodModel = (IMethodSymbol) ctx.SemanticModel.GetDeclaredSymbol(ctx.Node)!;
        var methodDeclarationSyntax = (MethodDeclarationSyntax)ctx.Node;
        var classDeclarationSyntax = (ClassDeclarationSyntax) ctx.Node.Parent!;

        return (classDeclarationSyntax, methodDeclarationSyntax,  methodModel);
    }

    private static bool IsTargetForGeneration(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not MethodDeclarationSyntax methodDeclarationSyntax) return false;

        var isStatic = false;
        foreach (var i in methodDeclarationSyntax.Modifiers)
        {
            if (i.IsKind(SyntaxKind.StaticKeyword))
            {
                isStatic = true;
                break;
            }
        }

        return isStatic
               && methodDeclarationSyntax.Identifier.ToString().EndsWith("Extension", StringComparison.Ordinal)
               && methodDeclarationSyntax.Parent is ClassDeclarationSyntax;
    }

    static string GetNamespace(BaseTypeDeclarationSyntax syntax)
    {
        // If we don't have a namespace at all we'll return an empty string
        // This accounts for the "default namespace" case
        var nameSpace = string.Empty;

        // Get the containing syntax node for the type declaration
        // (could be a nested type, for example)
        var potentialNamespaceParent = syntax.Parent;

        // Keep moving "out" of nested classes etc until we get to a namespace
        // or until we run out of parents
        while (potentialNamespaceParent != null &&
               potentialNamespaceParent is not NamespaceDeclarationSyntax
               && potentialNamespaceParent is not FileScopedNamespaceDeclarationSyntax)
        {
            potentialNamespaceParent = potentialNamespaceParent.Parent;
        }

        // Build up the final namespace by looping until we no longer have a namespace declaration
        if (potentialNamespaceParent is not BaseNamespaceDeclarationSyntax namespaceParent)
        {
            return nameSpace;
        }

        // We have a namespace. Use that as the type
        nameSpace = namespaceParent.Name.ToString();

        // Keep moving "out" of the namespace declarations until we
        // run out of nested namespace declarations
        while (true)
        {
            if (namespaceParent.Parent is not NamespaceDeclarationSyntax parent)
            {
                break;
            }

            // Add the outer namespace as a prefix to the final namespace
            nameSpace = $"{namespaceParent.Name}.{nameSpace}";
            namespaceParent = parent;
        }

        // return the final namespace
        return nameSpace;
    }
}

