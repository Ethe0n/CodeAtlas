using CodeAtlas.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace CodeAtlas.Roslyn;

public sealed class VbSyntaxStructureExtractor
{
    public IReadOnlyList<TypeStructure> Extract(SyntaxNode root, string? filePath)
    {
        return root
            .DescendantNodes()
            .OfType<ClassBlockSyntax>()
            .Select(classBlock => ExtractClass(classBlock, filePath))
            .ToArray();
    }

    private static TypeStructure ExtractClass(ClassBlockSyntax classBlock, string? filePath)
    {
        var className = classBlock.ClassStatement.Identifier.ValueText;
        var namespaceName = GetNamespaceName(classBlock);
        var fullName = string.IsNullOrWhiteSpace(namespaceName)
            ? className
            : $"{namespaceName}.{className}";

        var methods = classBlock.Members
            .OfType<MethodBlockSyntax>()
            .Select(methodBlock => ExtractMethod(methodBlock, filePath))
            .ToArray();

        return new TypeStructure(
            className,
            fullName,
            namespaceName,
            filePath,
            ToSpanInfo(classBlock.GetLocation().GetLineSpan()),
            methods);
    }

    private static MethodStructure ExtractMethod(MethodBlockSyntax methodBlock, string? filePath)
    {
        var statement = methodBlock.SubOrFunctionStatement;
        var kind = statement.DeclarationKeyword.IsKind(SyntaxKind.FunctionKeyword)
            ? VbMethodKind.Function
            : VbMethodKind.Sub;

        return new MethodStructure(
            statement.Identifier.ValueText,
            kind,
            GetAccessibility(statement.Modifiers),
            filePath,
            ToSpanInfo(methodBlock.GetLocation().GetLineSpan()));
    }

    private static string? GetNamespaceName(SyntaxNode node)
    {
        var namespaces = node
            .Ancestors()
            .OfType<NamespaceBlockSyntax>()
            .Reverse()
            .Select(namespaceBlock => namespaceBlock.NamespaceStatement.Name.ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        return namespaces.Length == 0
            ? null
            : string.Join(".", namespaces);
    }

    private static string GetAccessibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return "Public";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.FriendKeyword))
        {
            return "Protected Friend";
        }

        if (modifiers.Any(SyntaxKind.PrivateKeyword) && modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return "Private Protected";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return "Protected";
        }

        if (modifiers.Any(SyntaxKind.FriendKeyword))
        {
            return "Friend";
        }

        if (modifiers.Any(SyntaxKind.PrivateKeyword))
        {
            return "Private";
        }

        return "Unspecified";
    }

    private static TextSpanInfo ToSpanInfo(FileLinePositionSpan lineSpan)
    {
        return new TextSpanInfo(
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            lineSpan.EndLinePosition.Line + 1,
            lineSpan.EndLinePosition.Character + 1);
    }
}
