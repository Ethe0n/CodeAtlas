using CodeAtlas.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace CodeAtlas.Roslyn;

public sealed class VbSyntaxStructureExtractor
{
    private readonly ControlFlowAnalyzer _controlFlowAnalyzer = new();

    public IReadOnlyList<TypeStructure> ExtractTypes(
        SyntaxNode root,
        SemanticModel semanticModel,
        string? filePath,
        string? projectDirectory)
    {
        var isDesignerDocument = IsDesignerDocument(filePath);

        return root
            .DescendantNodes()
            .OfType<ClassBlockSyntax>()
            .Select(classBlock => ExtractClass(classBlock, semanticModel, filePath, projectDirectory, isDesignerDocument))
            .Where(type => type is not null)
            .Select(type => type!)
            .ToArray();
    }

    public IReadOnlyList<CallRelation> ExtractCalls(
        SyntaxNode root,
        SemanticModel semanticModel,
        Compilation compilation,
        string? filePath,
        string? projectDirectory)
    {
        var isDesignerDocument = IsDesignerDocument(filePath);

        return root
            .DescendantNodes()
            .OfType<MethodBlockSyntax>()
            .SelectMany(methodBlock => ExtractMethodCalls(methodBlock, semanticModel, compilation, filePath, projectDirectory, isDesignerDocument))
            .ToArray();
    }

    public IReadOnlyList<ControlFlowInfo> ExtractControlFlows(
        SyntaxNode root,
        SemanticModel semanticModel,
        string? filePath)
    {
        var isDesignerDocument = IsDesignerDocument(filePath);

        return root
            .DescendantNodes()
            .OfType<MethodBlockSyntax>()
            .Select(methodBlock =>
            {
                var statement = methodBlock.SubOrFunctionStatement;
                if (semanticModel.GetDeclaredSymbol(statement) is not IMethodSymbol methodSymbol)
                {
                    return null;
                }

                var methodSyntax = methodBlock.SyntaxTree.GetRoot().FindNode(methodBlock.GetLocation().SourceSpan);
                var isGenerated = isDesignerDocument || IsGenerated(methodSyntax, methodSymbol);
                return _controlFlowAnalyzer.Analyze(methodBlock, semanticModel, isGenerated);
            })
            .Where(controlFlow => controlFlow is not null)
            .Select(controlFlow => controlFlow!)
            .ToArray();
    }

    public IReadOnlyList<FieldUsageRelation> ExtractFieldUsages(
        SyntaxNode root,
        SemanticModel semanticModel,
        string? filePath,
        string? projectDirectory)
    {
        var isDesignerDocument = IsDesignerDocument(filePath);

        return root
            .DescendantNodes()
            .OfType<MethodBlockSyntax>()
            .SelectMany(methodBlock => ExtractMethodFieldUsages(methodBlock, semanticModel, filePath, projectDirectory, isDesignerDocument))
            .ToArray();
    }

    private static TypeStructure? ExtractClass(
        ClassBlockSyntax classBlock,
        SemanticModel semanticModel,
        string? filePath,
        string? projectDirectory,
        bool isDesignerDocument)
    {
        if (semanticModel.GetDeclaredSymbol(classBlock.ClassStatement) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        var extractedMethods = classBlock.Members
            .OfType<MethodBlockSyntax>()
            .Select(methodBlock => ExtractMethod(methodBlock, semanticModel, filePath, projectDirectory, isDesignerDocument))
            .Where(method => method is not null)
            .Select(method => method!)
            .ToArray();

        var methods = extractedMethods
            .Where(method => !method.IsGenerated)
            .ToArray();

        var generatedMethods = extractedMethods
            .Where(method => method.IsGenerated)
            .ToArray();

        var members = typeSymbol.GetMembers();

        var fields = isDesignerDocument
            ? Array.Empty<FieldStructure>()
            : members
                .SelectMany(member => ExtractFieldLikeMember(member, classBlock, filePath, projectDirectory))
                .Where(field => !field.IsGenerated)
                .ToArray();

        var properties = isDesignerDocument
            ? Array.Empty<PropertyStructure>()
            : members
                .OfType<IPropertySymbol>()
                .Select(property => ExtractProperty(property, classBlock, filePath, projectDirectory))
                .Where(property => property is not null && !property.IsGenerated)
                .Select(property => property!)
                .ToArray();

        var uiControls = isDesignerDocument
            ? ExtractUiControls(classBlock, semanticModel, filePath, projectDirectory).ToArray()
            : Array.Empty<UiControlInfo>();

        var uiEventHandlers = classBlock.Members
            .OfType<MethodBlockSyntax>()
            .SelectMany(methodBlock => ExtractUiEventHandlers(methodBlock, semanticModel, filePath, projectDirectory))
            .ToArray();

        return new TypeStructure(
            typeSymbol.Name,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            GetSymbolId(typeSymbol),
            typeSymbol.ContainingNamespace?.IsGlobalNamespace == false
                ? typeSymbol.ContainingNamespace.ToDisplayString()
                : null,
            typeSymbol.ContainingType is null ? null : GetSymbolId(typeSymbol.ContainingType),
            typeSymbol.TypeKind.ToString(),
            ToAccessibility(typeSymbol.DeclaredAccessibility),
            typeSymbol.BaseType is null ? null : ToBaseTypeDisplayName(typeSymbol.BaseType),
            filePath is null ? Array.Empty<string>() : new[] { ToProjectRelativePath(filePath, projectDirectory) },
            ToSpanInfo(classBlock.GetLocation().GetLineSpan()),
            fields,
            properties,
            methods,
            generatedMethods,
            uiControls,
            uiEventHandlers);
    }

    private static IEnumerable<FieldStructure> ExtractFieldLikeMember(
        ISymbol member,
        ClassBlockSyntax classBlock,
        string? filePath,
        string? projectDirectory)
    {
        if (member is IFieldSymbol { IsImplicitlyDeclared: false } fieldSymbol)
        {
            var location = GetSourceLocationInFile(fieldSymbol, filePath);
            if (location is null)
            {
                yield break;
            }

            var syntax = classBlock.SyntaxTree.GetRoot().FindNode(location.SourceSpan);
            var relativePath = ToProjectRelativePath(location.GetLineSpan().Path, projectDirectory);
            yield return new FieldStructure(
                GetSymbolId(fieldSymbol),
                fieldSymbol.Name,
                fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ToAccessibility(fieldSymbol.DeclaredAccessibility),
                fieldSymbol.IsStatic,
                fieldSymbol.IsReadOnly,
                fieldSymbol.IsConst,
                GetSymbolId(fieldSymbol.ContainingType),
                relativePath,
                IsGenerated(syntax, fieldSymbol),
                ToSpanInfo(location.GetLineSpan()),
                GetFieldInitializer(syntax));
        }
        else if (member is IEventSymbol { IsImplicitlyDeclared: false } eventSymbol)
        {
            var location = GetSourceLocationInFile(eventSymbol, filePath);
            if (location is null)
            {
                yield break;
            }

            var syntax = classBlock.SyntaxTree.GetRoot().FindNode(location.SourceSpan);
            var relativePath = ToProjectRelativePath(location.GetLineSpan().Path, projectDirectory);
            yield return new FieldStructure(
                GetSymbolId(eventSymbol),
                eventSymbol.Name,
                eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ToAccessibility(eventSymbol.DeclaredAccessibility),
                eventSymbol.IsStatic,
                false,
                false,
                GetSymbolId(eventSymbol.ContainingType),
                relativePath,
                IsGenerated(syntax, eventSymbol),
                ToSpanInfo(location.GetLineSpan()),
                null);
        }
    }

    private static PropertyStructure? ExtractProperty(
        IPropertySymbol propertySymbol,
        ClassBlockSyntax classBlock,
        string? filePath,
        string? projectDirectory)
    {
        if (propertySymbol.IsImplicitlyDeclared)
        {
            return null;
        }

        var location = GetSourceLocationInFile(propertySymbol, filePath);
        if (location is null)
        {
            return null;
        }

        var syntax = classBlock.SyntaxTree.GetRoot().FindNode(location.SourceSpan);
        var relativePath = ToProjectRelativePath(location.GetLineSpan().Path, projectDirectory);
        return new PropertyStructure(
            propertySymbol.Name,
            propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ToAccessibility(propertySymbol.DeclaredAccessibility),
            propertySymbol.IsStatic,
            propertySymbol.GetMethod is not null && propertySymbol.SetMethod is null,
            propertySymbol.SetMethod is not null && propertySymbol.GetMethod is null,
            relativePath,
            IsGenerated(syntax, propertySymbol),
            ToSpanInfo(location.GetLineSpan()));
    }

    private static MethodStructure? ExtractMethod(
        MethodBlockSyntax methodBlock,
        SemanticModel semanticModel,
        string? filePath,
        string? projectDirectory,
        bool isDesignerDocument)
    {
        var statement = methodBlock.SubOrFunctionStatement;
        if (semanticModel.GetDeclaredSymbol(statement) is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        var kind = statement.DeclarationKeyword.IsKind(SyntaxKind.FunctionKeyword)
            ? VbMethodKind.Function
            : VbMethodKind.Sub;

        var location = GetSourceLocationInFile(methodSymbol, filePath)
            ?? methodBlock.GetLocation();
        var syntax = methodBlock.SyntaxTree.GetRoot().FindNode(location.SourceSpan);

        return new MethodStructure(
            methodSymbol.Name,
            GetSymbolId(methodSymbol),
            GetSymbolId(methodSymbol.ContainingType),
            kind,
            ToAccessibility(methodSymbol.DeclaredAccessibility),
            ToProjectRelativePath(location.GetLineSpan().Path, projectDirectory),
            isDesignerDocument || IsGenerated(syntax, methodSymbol),
            ToSpanInfo(location.GetLineSpan()));
    }

    private static IEnumerable<CallRelation> ExtractMethodCalls(
        MethodBlockSyntax methodBlock,
        SemanticModel semanticModel,
        Compilation compilation,
        string? filePath,
        string? projectDirectory,
        bool isDesignerDocument)
    {
        var statement = methodBlock.SubOrFunctionStatement;
        if (semanticModel.GetDeclaredSymbol(statement) is not IMethodSymbol callerSymbol)
        {
            yield break;
        }

        var callerSyntax = methodBlock.SyntaxTree.GetRoot().FindNode(methodBlock.GetLocation().SourceSpan);
        if (isDesignerDocument || IsGenerated(callerSyntax, callerSymbol))
        {
            yield break;
        }

        foreach (var invocation in methodBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var calleeSymbol = ResolveMethodSymbol(invocation, semanticModel);
            if (calleeSymbol is null)
            {
                continue;
            }

            yield return new CallRelation(
                GetSymbolId(callerSymbol),
                callerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                GetSymbolId(calleeSymbol),
                calleeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                IsProjectInternal(calleeSymbol, compilation),
                calleeSymbol.ContainingAssembly?.Name,
                filePath is null ? null : ToProjectRelativePath(filePath, projectDirectory),
                ToSpanInfo(invocation.GetLocation().GetLineSpan()));
        }
    }

    private static IEnumerable<FieldUsageRelation> ExtractMethodFieldUsages(
        MethodBlockSyntax methodBlock,
        SemanticModel semanticModel,
        string? filePath,
        string? projectDirectory,
        bool isDesignerDocument)
    {
        var statement = methodBlock.SubOrFunctionStatement;
        if (semanticModel.GetDeclaredSymbol(statement) is not IMethodSymbol methodSymbol)
        {
            yield break;
        }

        var methodSyntax = methodBlock.SyntaxTree.GetRoot().FindNode(methodBlock.GetLocation().SourceSpan);
        if (isDesignerDocument || IsGenerated(methodSyntax, methodSymbol))
        {
            yield break;
        }

        if (semanticModel.GetOperation(methodBlock) is not IBlockOperation operation)
        {
            yield break;
        }

        var seenReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fieldReference in DescendantsAndSelf(operation).OfType<IFieldReferenceOperation>())
        {
            if (fieldReference.Field.IsImplicitlyDeclared)
            {
                continue;
            }

            var fieldSymbolId = GetSymbolId(fieldReference.Field);
            var usageKind = GetFieldUsageKind(fieldReference);
            var span = ToSpanInfo(fieldReference.Syntax.GetLocation().GetLineSpan());
            var key = string.Join(
                "|",
                fieldSymbolId,
                GetSymbolId(methodSymbol),
                usageKind,
                span.StartLine,
                span.StartColumn,
                span.EndLine,
                span.EndColumn);

            if (!seenReferences.Add(key))
            {
                continue;
            }

            yield return new FieldUsageRelation(
                fieldSymbolId,
                GetSymbolId(methodSymbol),
                usageKind,
                filePath is null ? null : ToProjectRelativePath(filePath, projectDirectory),
                span);
        }
    }

    private static IEnumerable<UiControlInfo> ExtractUiControls(
        ClassBlockSyntax classBlock,
        SemanticModel semanticModel,
        string? filePath,
        string? projectDirectory)
    {
        foreach (var fieldDeclaration in classBlock.Members.OfType<FieldDeclarationSyntax>())
        {
            foreach (var declarator in fieldDeclaration.Declarators)
            {
                if (declarator.AsClause is not SimpleAsClauseSyntax asClause)
                {
                    continue;
                }

                var type = semanticModel.GetTypeInfo(asClause.Type).Type;
                if (type is null || !IsWinFormsControlType(type))
                {
                    continue;
                }

                foreach (var name in declarator.Names)
                {
                    yield return new UiControlInfo(
                        name.Identifier.ValueText,
                        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        filePath is null ? null : ToProjectRelativePath(filePath, projectDirectory));
                }
            }
        }
    }

    private static string? GetFieldInitializer(SyntaxNode syntax)
    {
        return syntax.FirstAncestorOrSelf<VariableDeclaratorSyntax>()
            ?.Initializer
            ?.Value
            .ToString();
    }

    private static FieldUsageKind GetFieldUsageKind(IFieldReferenceOperation fieldReference)
    {
        for (var current = fieldReference.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case ISimpleAssignmentOperation assignment
                    when ContainsOperation(assignment.Target, fieldReference):
                    return FieldUsageKind.Write;

                case ICompoundAssignmentOperation assignment
                    when ContainsOperation(assignment.Target, fieldReference):
                    return FieldUsageKind.ReadWrite;

                case IIncrementOrDecrementOperation increment
                    when ContainsOperation(increment.Target, fieldReference):
                    return FieldUsageKind.ReadWrite;
            }
        }

        return FieldUsageKind.Read;
    }

    private static bool ContainsOperation(IOperation root, IOperation target)
    {
        return DescendantsAndSelf(root).Any(operation => ReferenceEquals(operation, target));
    }

    private static IEnumerable<IOperation> DescendantsAndSelf(IOperation operation)
    {
        yield return operation;

        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<UiEventHandlerInfo> ExtractUiEventHandlers(
        MethodBlockSyntax methodBlock,
        SemanticModel semanticModel,
        string? filePath,
        string? projectDirectory)
    {
        var statement = methodBlock.SubOrFunctionStatement;
        if (statement.HandlesClause is null ||
            semanticModel.GetDeclaredSymbol(statement) is not IMethodSymbol methodSymbol)
        {
            yield break;
        }

        foreach (var handledEvent in statement.HandlesClause.Events)
        {
            var eventPath = handledEvent.ToString();
            var separatorIndex = eventPath.LastIndexOf('.');
            if (separatorIndex <= 0 || separatorIndex == eventPath.Length - 1)
            {
                continue;
            }

            yield return new UiEventHandlerInfo(
                eventPath[..separatorIndex],
                eventPath[(separatorIndex + 1)..],
                methodSymbol.Name,
                GetSymbolId(methodSymbol),
                filePath is null ? null : ToProjectRelativePath(filePath, projectDirectory));
        }
    }

    private static bool IsWinFormsControlType(ITypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.Windows.Forms.Control")
            {
                return true;
            }
        }

        return false;
    }

    private static IMethodSymbol? ResolveMethodSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        return methodSymbol?.ReducedFrom ?? methodSymbol;
    }

    private static bool IsProjectInternal(IMethodSymbol methodSymbol, Compilation compilation)
    {
        return SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingAssembly, compilation.Assembly)
            && methodSymbol.Locations.Any(location => location.IsInSource);
    }

    private static string GetSymbolId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static Location? GetSourceLocationInFile(ISymbol symbol, string? filePath)
    {
        return symbol.Locations.FirstOrDefault(location =>
            location.IsInSource &&
            (string.IsNullOrWhiteSpace(filePath) ||
             string.Equals(location.GetLineSpan().Path, filePath, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ToProjectRelativePath(string filePath, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return filePath;
        }

        return Path.GetRelativePath(projectDirectory, filePath);
    }

    private static bool IsGenerated(SyntaxNode syntax, ISymbol symbol)
    {
        return HasGeneratedAttribute(symbol)
            || syntax.AncestorsAndSelf().Any(HasGeneratedAttribute);
    }

    private static bool IsDesignerDocument(string? filePath)
    {
        return filePath is not null &&
            filePath.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGeneratedAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute => IsGeneratedAttributeName(attribute.AttributeClass?.Name));
    }

    private static bool HasGeneratedAttribute(SyntaxNode syntax)
    {
        var attributeLists = syntax switch
        {
            ClassBlockSyntax classBlock => classBlock.ClassStatement.AttributeLists,
            FieldDeclarationSyntax field => field.AttributeLists,
            MethodBlockSyntax methodBlock => methodBlock.SubOrFunctionStatement.AttributeLists,
            MethodStatementSyntax method => method.AttributeLists,
            PropertyStatementSyntax property => property.AttributeLists,
            PropertyBlockSyntax propertyBlock => propertyBlock.PropertyStatement.AttributeLists,
            _ => default
        };

        return attributeLists
            .SelectMany(attributeList => attributeList.Attributes)
            .Any(attribute => IsGeneratedAttributeName(attribute.Name.ToString()));
    }

    private static bool IsGeneratedAttributeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.EndsWith("Generated", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("GeneratedAttribute", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("DesignerGenerated", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("DesignerGeneratedAttribute", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("CompilerGenerated", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("CompilerGeneratedAttribute", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "Public",
            Accessibility.Protected => "Protected",
            Accessibility.Internal => "Friend",
            Accessibility.ProtectedOrInternal => "Protected Friend",
            Accessibility.Private => "Private",
            Accessibility.ProtectedAndInternal => "Private Protected",
            _ => "Unspecified"
        };
    }

    private static string ToBaseTypeDisplayName(INamedTypeSymbol baseType)
    {
        return baseType.SpecialType == SpecialType.System_Object
            ? "System.Object"
            : baseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
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
