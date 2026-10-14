using CodeAtlas.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace CodeAtlas.Roslyn;

public sealed class ControlFlowAnalyzer
{
    public ControlFlowInfo? Analyze(
        MethodBlockSyntax methodBlock,
        SemanticModel semanticModel,
        bool isGenerated)
    {
        if (isGenerated)
        {
            return null;
        }

        if (semanticModel.GetDeclaredSymbol(methodBlock.SubOrFunctionStatement) is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        var operation = semanticModel.GetOperation(methodBlock) as IBlockOperation;
        if (operation is null)
        {
            return null;
        }

        var graph = ControlFlowGraph.Create(operation);
        var captureValues = GetFlowCaptureValues(graph);
        var nodes = graph.Blocks
            .Select(block => ToNode(block, captureValues))
            .ToArray();

        var edges = graph.Blocks
            .SelectMany(block => ToEdges(block, captureValues))
            .Distinct()
            .OrderBy(edge => edge.From)
            .ThenBy(edge => edge.To)
            .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
            .ToArray();

        var rawRegions = EnumerateRegions(graph.Root).ToArray();
        var regionIds = rawRegions
            .Select((region, index) => (Region: region, Id: $"region_{index}"))
            .ToDictionary(item => item.Region, item => item.Id);
        var usingRegionRoots = GetUsingRegionRoots(methodBlock, rawRegions, graph.Blocks);
        var regionSourceKinds = rawRegions.ToDictionary(
            region => region,
            region => usingRegionRoots.Any(root => IsRegionWithin(region, root))
                ? ControlFlowRegionSourceKind.Using
                : ControlFlowRegionSourceKind.None);

        var regions = rawRegions
            .Select(region => new ControlFlowRegionInfo(
                regionIds[region],
                region.Kind.ToString(),
                region.EnclosingRegion is null ? null : regionIds[region.EnclosingRegion],
                region.FirstBlockOrdinal,
                region.LastBlockOrdinal,
                region.ExceptionType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            {
                SourceKind = regionSourceKinds[region]
            })
            .ToArray();
        var transfers = graph.Blocks
            .SelectMany(block => ToTransfers(block, captureValues, regionIds))
            .ToArray();

        return new ControlFlowInfo(
            GetSymbolId(methodSymbol),
            methodSymbol.Name,
            nodes,
            edges)
        {
            Regions = regions,
            Transfers = transfers
        };
    }

    private static ControlFlowNode ToNode(
        BasicBlock block,
        IReadOnlyDictionary<object, string> captureValues)
    {
        var operations = block.Operations
            .Select(operation => ToOperationInfo(operation, captureValues))
            .Where(operation => operation is not null)
            .Select(operation => operation!)
            .ToArray();
        var operationTexts = operations
            .Select(operation => operation.Text)
            .ToArray();
        var textLines = operationTexts
            .ToList();
        string? condition = null;
        string? returnText = null;

        if (block.BranchValue is not null && HasReturnSuccessor(block))
        {
            returnText = $"Return {FormatExpression(block.BranchValue, captureValues)}";
            textLines.Add(returnText);
        }
        else if (block.BranchValue is not null && HasThrowSuccessor(block))
        {
            textLines.Add($"Throw {FormatExpression(block.BranchValue, captureValues)}");
        }
        else if (block.BranchValue is not null)
        {
            condition = FormatConditionExpression(block.BranchValue, captureValues);
            textLines.Add($"Condition: {condition}");
        }

        return new ControlFlowNode(
            block.Ordinal,
            block.Kind.ToString(),
            string.Join(Environment.NewLine, textLines),
            GetSourceLocation(block))
        {
            OperationTexts = operationTexts,
            Operations = operations,
            Condition = condition,
            ReturnText = returnText,
            ControlStructure = GetControlStructure(block)
        };
    }

    private static ControlFlowOperationInfo? ToOperationInfo(
        IOperation operation,
        IReadOnlyDictionary<object, string> captureValues)
    {
        var text = FormatOperation(operation, captureValues);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new ControlFlowOperationInfo(
            operation.Kind.ToString(),
            text,
            operation.IsImplicit,
            GetOperationRole(operation),
            ToSpanInfo(operation.Syntax.GetLocation().GetLineSpan()));
    }

    private static ControlFlowOperationRole GetOperationRole(IOperation operation)
    {
        if (IsErrorHandlingDirective(operation.Syntax))
        {
            return ControlFlowOperationRole.ErrorHandlingDirective;
        }

        var candidate = operation is IExpressionStatementOperation expression
            ? expression.Operation
            : operation;

        if (candidate is not ISimpleAssignmentOperation assignment ||
            (!operation.IsImplicit && !assignment.IsImplicit))
        {
            return ControlFlowOperationRole.None;
        }

        var value = assignment.Value;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        if (value is not IPropertyReferenceOperation propertyReference ||
            !propertyReference.IsImplicit ||
            !string.Equals(propertyReference.Property.Name, "Current", StringComparison.Ordinal) ||
            !HasForEachStatementSyntax(assignment.Syntax))
        {
            return ControlFlowOperationRole.None;
        }

        return ControlFlowOperationRole.ForEachIterationAssignment;
    }

    private static ControlFlowStructureInfo? GetControlStructure(BasicBlock block)
    {
        if (block.BranchValue is not null)
        {
            var forEachStatement = block.BranchValue.Syntax
                .AncestorsAndSelf()
                .OfType<ForEachStatementSyntax>()
                .FirstOrDefault();
            if (forEachStatement is not null)
            {
                return new ControlFlowStructureInfo(
                    ControlFlowStructureKind.ForEach,
                    FormatSyntax(forEachStatement),
                    ToSpanInfo(forEachStatement.GetLocation().GetLineSpan()));
            }
        }

        var syntax = block.BranchValue?.Syntax ?? block.Operations.FirstOrDefault()?.Syntax;
        var caseBlock = syntax?
            .AncestorsAndSelf()
            .OfType<CaseBlockSyntax>()
            .FirstOrDefault();
        var selectBlock = caseBlock?
            .AncestorsAndSelf()
            .OfType<SelectBlockSyntax>()
            .FirstOrDefault();
        if (caseBlock is null || selectBlock is null)
        {
            return null;
        }

        var branchSyntax = block.BranchValue?.Syntax;
        var role = branchSyntax is not null && caseBlock.CaseStatement.Span.Contains(branchSyntax.Span)
            ? ControlFlowStructureRole.BranchTest
            : ControlFlowStructureRole.BranchBody;

        return new ControlFlowStructureInfo(
            ControlFlowStructureKind.SelectCase,
            FormatSyntax(selectBlock.SelectStatement),
            ToSpanInfo(selectBlock.SelectStatement.GetLocation().GetLineSpan()))
        {
            GroupId = $"select_{selectBlock.SpanStart}_{selectBlock.Span.Length}",
            Role = role,
            BranchLabel = FormatSyntax(caseBlock.CaseStatement),
            BranchCondition = FormatSelectCaseCondition(selectBlock, caseBlock)
        };
    }

    private static string FormatSelectCaseCondition(
        SelectBlockSyntax selectBlock,
        CaseBlockSyntax caseBlock)
    {
        if (caseBlock.CaseStatement.Kind() == SyntaxKind.CaseElseStatement)
        {
            return "Else";
        }

        var selector = FormatSyntax(selectBlock.SelectStatement.Expression);
        return string.Join(
            " Or ",
            caseBlock.CaseStatement.Cases.Select(caseClause => caseClause switch
            {
                SimpleCaseClauseSyntax simpleCase =>
                    $"{selector} = {FormatSyntax(simpleCase.Value)}",
                RangeCaseClauseSyntax rangeCase =>
                    $"{selector} >= {FormatSyntax(rangeCase.LowerBound)} And " +
                    $"{selector} <= {FormatSyntax(rangeCase.UpperBound)}",
                RelationalCaseClauseSyntax relationalCase =>
                    $"{selector} {relationalCase.OperatorToken.Text} {FormatSyntax(relationalCase.Value)}",
                _ => $"{selector} matches {FormatSyntax(caseClause)}"
            }));
    }

    private static bool HasForEachStatementSyntax(SyntaxNode syntax)
    {
        return syntax
            .AncestorsAndSelf()
            .OfType<ForEachStatementSyntax>()
            .Any();
    }

    private static IEnumerable<ControlFlowEdge> ToEdges(
        BasicBlock block,
        IReadOnlyDictionary<object, string> captureValues)
    {
        var condition = block.BranchValue is null
            ? null
            : FormatConditionExpression(block.BranchValue, captureValues);

        if (block.ConditionalSuccessor?.Destination is { } conditionalDestination)
        {
            yield return new ControlFlowEdge(
                block.Ordinal,
                conditionalDestination.Ordinal,
                GetConditionalEdgeKind(block, isConditionalSuccessor: true),
                condition);
        }

        if (block.FallThroughSuccessor?.Destination is { } fallThroughDestination)
        {
            yield return new ControlFlowEdge(
                block.Ordinal,
                fallThroughDestination.Ordinal,
                GetConditionalEdgeKind(block, isConditionalSuccessor: false),
                block.ConditionalSuccessor is null ? null : condition);
        }
    }

    private static IEnumerable<ControlFlowRegion> EnumerateRegions(ControlFlowRegion region)
    {
        yield return region;

        foreach (var nestedRegion in region.NestedRegions)
        {
            foreach (var descendant in EnumerateRegions(nestedRegion))
            {
                yield return descendant;
            }
        }
    }

    private static IReadOnlySet<ControlFlowRegion> GetUsingRegionRoots(
        MethodBlockSyntax methodBlock,
        IReadOnlyList<ControlFlowRegion> regions,
        IReadOnlyList<BasicBlock> blocks)
    {
        var roots = new HashSet<ControlFlowRegion>();
        foreach (var usingBlock in methodBlock.DescendantNodes().OfType<UsingBlockSyntax>())
        {
            var root = regions
                .Where(region =>
                    region.Kind == ControlFlowRegionKind.TryAndFinally &&
                    RegionContainsUsingSyntax(region, blocks, usingBlock))
                .OrderBy(region => region.LastBlockOrdinal - region.FirstBlockOrdinal)
                .FirstOrDefault();
            if (root is not null)
            {
                roots.Add(root);
            }
        }

        return roots;
    }

    private static bool RegionContainsUsingSyntax(
        ControlFlowRegion region,
        IReadOnlyList<BasicBlock> blocks,
        UsingBlockSyntax usingBlock)
    {
        for (var ordinal = region.FirstBlockOrdinal;
             ordinal <= region.LastBlockOrdinal;
             ordinal++)
        {
            var block = blocks[ordinal];
            if (block.Operations.Any(operation => IsInsideUsing(operation.Syntax, usingBlock)) ||
                block.BranchValue is not null && IsInsideUsing(block.BranchValue.Syntax, usingBlock))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideUsing(SyntaxNode syntax, UsingBlockSyntax usingBlock)
    {
        return syntax.AncestorsAndSelf()
            .OfType<UsingBlockSyntax>()
            .Any(candidate => candidate.Span == usingBlock.Span);
    }

    private static bool IsRegionWithin(
        ControlFlowRegion region,
        ControlFlowRegion ancestor)
    {
        for (var current = region; current is not null; current = current.EnclosingRegion)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ControlFlowTransferInfo> ToTransfers(
        BasicBlock block,
        IReadOnlyDictionary<object, string> captureValues,
        IReadOnlyDictionary<ControlFlowRegion, string> regionIds)
    {
        var condition = block.BranchValue is null
            ? null
            : FormatConditionExpression(block.BranchValue, captureValues);

        if (block.ConditionalSuccessor is { } conditionalSuccessor)
        {
            yield return ToTransferInfo(
                block.Ordinal,
                conditionalSuccessor,
                "Conditional",
                condition,
                regionIds);
        }

        if (block.FallThroughSuccessor is { } fallThroughSuccessor)
        {
            yield return ToTransferInfo(
                block.Ordinal,
                fallThroughSuccessor,
                "FallThrough",
                block.ConditionalSuccessor is null ? null : condition,
                regionIds);
        }
    }

    private static ControlFlowTransferInfo ToTransferInfo(
        int from,
        ControlFlowBranch branch,
        string slot,
        string? condition,
        IReadOnlyDictionary<ControlFlowRegion, string> regionIds)
    {
        return new ControlFlowTransferInfo(
            from,
            branch.Destination?.Ordinal,
            slot,
            branch.Semantics.ToString(),
            condition,
            branch.LeavingRegions.Select(region => regionIds[region]).ToArray(),
            branch.EnteringRegions.Select(region => regionIds[region]).ToArray(),
            branch.FinallyRegions.Select(region => regionIds[region]).ToArray());
    }

    private static string GetConditionalEdgeKind(BasicBlock block, bool isConditionalSuccessor)
    {
        if (block.ConditionalSuccessor is null)
        {
            return IsReturnSuccessor(block.FallThroughSuccessor)
                ? "Return"
                : "FallThrough";
        }

        return block.ConditionKind switch
        {
            ControlFlowConditionKind.WhenTrue => isConditionalSuccessor ? "ConditionalTrue" : "ConditionalFalse",
            ControlFlowConditionKind.WhenFalse => isConditionalSuccessor ? "ConditionalFalse" : "ConditionalTrue",
            _ => isConditionalSuccessor ? "Conditional" : "FallThrough"
        };
    }

    private static string? FormatOperation(
        IOperation operation,
        IReadOnlyDictionary<object, string> captureValues)
    {
        if (operation.Syntax is MethodBlockBaseSyntax or MethodStatementSyntax)
        {
            return null;
        }

        return operation switch
        {
            IFlowCaptureOperation => null,
            IExpressionStatementOperation expression => FormatExpression(expression.Operation, captureValues),
            ISimpleAssignmentOperation assignment => $"{FormatExpression(assignment.Target, captureValues)} = {FormatExpression(assignment.Value, captureValues)}",
            IReturnOperation returnOperation => returnOperation.ReturnedValue is null
                ? "Return"
                : $"Return {FormatExpression(returnOperation.ReturnedValue, captureValues)}",
            _ => FormatFallbackOperation(operation)
        };
    }

    private static string? FormatFallbackOperation(IOperation operation)
    {
        if (operation.IsImplicit || operation.Syntax is not StatementSyntax)
        {
            return null;
        }

        return FormatSyntax(operation.Syntax);
    }

    private static bool IsErrorHandlingDirective(SyntaxNode syntax)
    {
        return syntax.Kind() is
            SyntaxKind.OnErrorGoToZeroStatement or
            SyntaxKind.OnErrorGoToMinusOneStatement or
            SyntaxKind.OnErrorGoToLabelStatement or
            SyntaxKind.OnErrorResumeNextStatement;
    }

    private static string FormatExpression(
        IOperation operation,
        IReadOnlyDictionary<object, string>? captureValues)
    {
        return operation switch
        {
            IConversionOperation conversion => FormatExpression(conversion.Operand, captureValues),
            ICompoundAssignmentOperation assignment => $"{FormatExpression(assignment.Target, captureValues)} {GetCompoundOperator(assignment.OperatorKind)}= {FormatExpression(assignment.Value, captureValues)}",
            ISimpleAssignmentOperation assignment => $"{FormatExpression(assignment.Target, captureValues)} = {FormatExpression(assignment.Value, captureValues)}",
            IBinaryOperation binary => $"{FormatExpression(binary.LeftOperand, captureValues)} {GetBinaryOperator(binary.OperatorKind)} {FormatExpression(binary.RightOperand, captureValues)}",
            ILocalReferenceOperation local => local.Local.Name,
            IParameterReferenceOperation parameter => parameter.Parameter.Name,
            IFieldReferenceOperation field => FormatMemberReference(
                field.Instance,
                field.Field.Name,
                field.Syntax,
                captureValues),
            IPropertyReferenceOperation property => property.Arguments.Length > 0
                ? FormatSyntax(property.Syntax)
                : FormatMemberReference(
                    property.Instance,
                    property.Property.Name,
                    property.Syntax,
                    captureValues),
            IInvocationOperation invocation => FormatSyntax(invocation.Syntax),
            ILiteralOperation literal => FormatLiteral(literal),
            IFlowCaptureReferenceOperation capture => FormatFlowCaptureReference(capture, captureValues),
            _ => FormatSyntax(operation.Syntax)
        };
    }

    private static string FormatConditionExpression(
        IOperation operation,
        IReadOnlyDictionary<object, string>? captureValues)
    {
        var candidate = UnwrapConversion(operation);
        if (candidate is not IBinaryOperation binary ||
            !IsLogicalOperator(binary.OperatorKind))
        {
            return FormatExpression(operation, captureValues);
        }

        var terms = new List<IOperation>();
        CollectLogicalTerms(binary, binary.OperatorKind, terms);
        if (terms.Count <= 2)
        {
            return FormatExpression(operation, captureValues);
        }

        var operatorText = GetBinaryOperator(binary.OperatorKind);
        var lines = terms
            .Chunk(2)
            .Select((chunk, index) =>
            {
                var line = string.Join(
                    $" {operatorText} ",
                    chunk.Select(term => FormatLogicalTerm(
                        term,
                        binary.OperatorKind,
                        captureValues)));
                return index == 0 ? line : $"{operatorText} {line}";
            });

        return string.Join(Environment.NewLine, lines);
    }

    private static void CollectLogicalTerms(
        IOperation operation,
        BinaryOperatorKind operatorKind,
        ICollection<IOperation> terms)
    {
        var candidate = UnwrapConversion(operation);
        if (candidate is IBinaryOperation binary && binary.OperatorKind == operatorKind)
        {
            CollectLogicalTerms(binary.LeftOperand, operatorKind, terms);
            CollectLogicalTerms(binary.RightOperand, operatorKind, terms);
            return;
        }

        terms.Add(candidate);
    }

    private static string FormatLogicalTerm(
        IOperation operation,
        BinaryOperatorKind parentOperatorKind,
        IReadOnlyDictionary<object, string>? captureValues)
    {
        var text = FormatExpression(operation, captureValues);
        var candidate = UnwrapConversion(operation);
        return candidate is IBinaryOperation binary &&
            IsLogicalOperator(binary.OperatorKind) &&
            binary.OperatorKind != parentOperatorKind
                ? $"({text})"
                : text;
    }

    private static IOperation UnwrapConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool IsLogicalOperator(BinaryOperatorKind operatorKind)
    {
        return operatorKind is
            BinaryOperatorKind.And or
            BinaryOperatorKind.Or or
            BinaryOperatorKind.ConditionalAnd or
            BinaryOperatorKind.ConditionalOr;
    }

    private static string FormatLiteral(ILiteralOperation literal)
    {
        if (!literal.IsImplicit)
        {
            return FormatSyntax(literal.Syntax);
        }

        return literal.ConstantValue.HasValue
            ? literal.ConstantValue.Value?.ToString() ?? "Nothing"
            : FormatSyntax(literal.Syntax);
    }

    private static string FormatMemberReference(
        IOperation? instance,
        string memberName,
        SyntaxNode syntax,
        IReadOnlyDictionary<object, string>? captureValues)
    {
        if (instance is null)
        {
            return syntax is MemberAccessExpressionSyntax
                ? FormatSyntax(syntax)
                : memberName;
        }

        if (instance is IInstanceReferenceOperation instanceReference &&
            instanceReference.IsImplicit)
        {
            return memberName;
        }

        var instanceText = instance is IInstanceReferenceOperation
            ? FormatSyntax(instance.Syntax)
            : FormatExpression(instance, captureValues);
        return string.IsNullOrWhiteSpace(instanceText)
            ? memberName
            : $"{instanceText}.{memberName}";
    }

    private static string FormatFlowCaptureReference(
        IFlowCaptureReferenceOperation capture,
        IReadOnlyDictionary<object, string>? captureValues)
    {
        if (captureValues is not null && captureValues.TryGetValue(capture.Id, out var value))
        {
            return value;
        }

        var text = FormatSyntax(capture.Syntax);
        var asIndex = text.IndexOf(" As ", StringComparison.OrdinalIgnoreCase);
        return asIndex > 0 ? text[..asIndex] : text;
    }

    private static IReadOnlyDictionary<object, string> GetFlowCaptureValues(ControlFlowGraph graph)
    {
        var captureValues = new Dictionary<object, string>();
        foreach (var capture in graph.Blocks.SelectMany(block => block.Operations).OfType<IFlowCaptureOperation>())
        {
            captureValues[capture.Id] = FormatExpression(capture.Value, captureValues);
        }

        return captureValues;
    }

    private static string FormatSyntax(SyntaxNode syntax)
    {
        return string.Join(
            " ",
            syntax
                .ToString()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetBinaryOperator(BinaryOperatorKind operatorKind)
    {
        return operatorKind switch
        {
            BinaryOperatorKind.Add => "+",
            BinaryOperatorKind.Subtract => "-",
            BinaryOperatorKind.Multiply => "*",
            BinaryOperatorKind.Divide => "/",
            BinaryOperatorKind.IntegerDivide => "\\",
            BinaryOperatorKind.Remainder => "Mod",
            BinaryOperatorKind.Equals => "=",
            BinaryOperatorKind.NotEquals => "<>",
            BinaryOperatorKind.LessThan => "<",
            BinaryOperatorKind.LessThanOrEqual => "<=",
            BinaryOperatorKind.GreaterThan => ">",
            BinaryOperatorKind.GreaterThanOrEqual => ">=",
            BinaryOperatorKind.ConditionalAnd => "AndAlso",
            BinaryOperatorKind.ConditionalOr => "OrElse",
            BinaryOperatorKind.And => "And",
            BinaryOperatorKind.Or => "Or",
            _ => operatorKind.ToString()
        };
    }

    private static string GetCompoundOperator(BinaryOperatorKind operatorKind)
    {
        return operatorKind switch
        {
            BinaryOperatorKind.Add => "+",
            BinaryOperatorKind.Subtract => "-",
            BinaryOperatorKind.Multiply => "*",
            BinaryOperatorKind.Divide => "/",
            BinaryOperatorKind.IntegerDivide => "\\",
            _ => GetBinaryOperator(operatorKind)
        };
    }

    private static TextSpanInfo? GetSourceLocation(BasicBlock block)
    {
        var syntax = block.Operations.FirstOrDefault()?.Syntax ?? block.BranchValue?.Syntax;
        if (syntax is null)
        {
            return null;
        }

        return ToSpanInfo(syntax.GetLocation().GetLineSpan());
    }

    private static bool HasReturnSuccessor(BasicBlock block)
    {
        return IsReturnSuccessor(block.ConditionalSuccessor) ||
            IsReturnSuccessor(block.FallThroughSuccessor);
    }

    private static bool IsReturnSuccessor(ControlFlowBranch? branch)
    {
        return string.Equals(branch?.Semantics.ToString(), "Return", StringComparison.Ordinal);
    }

    private static bool HasThrowSuccessor(BasicBlock block)
    {
        return IsThrowSuccessor(block.ConditionalSuccessor) ||
            IsThrowSuccessor(block.FallThroughSuccessor);
    }

    private static bool IsThrowSuccessor(ControlFlowBranch? branch)
    {
        return branch?.Semantics is
            ControlFlowBranchSemantics.Throw or
            ControlFlowBranchSemantics.Rethrow;
    }

    private static string GetSymbolId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
