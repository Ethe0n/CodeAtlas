using CodeAtlas.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
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

        return new ControlFlowInfo(
            GetSymbolId(methodSymbol),
            methodSymbol.Name,
            nodes,
            edges);
    }

    private static ControlFlowNode ToNode(
        BasicBlock block,
        IReadOnlyDictionary<object, string> captureValues)
    {
        var textLines = block.Operations
            .Select(operation => FormatOperation(operation, captureValues))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (block.BranchValue is not null && HasReturnSuccessor(block))
        {
            textLines.Add($"Return {FormatExpression(block.BranchValue, captureValues)}");
        }
        else if (block.BranchValue is not null)
        {
            textLines.Add($"Condition: {FormatExpression(block.BranchValue, captureValues)}");
        }

        return new ControlFlowNode(
            block.Ordinal,
            block.Kind.ToString(),
            string.Join(Environment.NewLine, textLines),
            GetSourceLocation(block));
    }

    private static IEnumerable<ControlFlowEdge> ToEdges(
        BasicBlock block,
        IReadOnlyDictionary<object, string> captureValues)
    {
        var condition = block.BranchValue is null ? null : FormatExpression(block.BranchValue, captureValues);

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
        return operation switch
        {
            IFlowCaptureOperation => null,
            IExpressionStatementOperation expression => FormatExpression(expression.Operation, captureValues),
            ISimpleAssignmentOperation assignment => $"{FormatExpression(assignment.Target, captureValues)} = {FormatExpression(assignment.Value, captureValues)}",
            IReturnOperation returnOperation => returnOperation.ReturnedValue is null
                ? "Return"
                : $"Return {FormatExpression(returnOperation.ReturnedValue, captureValues)}",
            _ => FormatSyntax(operation.Syntax)
        };
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
            IFieldReferenceOperation field => field.Field.Name,
            IPropertyReferenceOperation property => property.Property.Name,
            IInvocationOperation invocation => FormatSyntax(invocation.Syntax),
            ILiteralOperation literal => literal.ConstantValue.HasValue
                ? literal.ConstantValue.Value?.ToString() ?? "Nothing"
                : FormatSyntax(literal.Syntax),
            IFlowCaptureReferenceOperation capture => FormatFlowCaptureReference(capture, captureValues),
            _ => FormatSyntax(operation.Syntax)
        };
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
