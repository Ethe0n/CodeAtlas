using CodeAtlas.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
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
        var nodes = graph.Blocks
            .Select(ToNode)
            .ToArray();

        var edges = graph.Blocks
            .SelectMany(ToEdges)
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

    private static ControlFlowNode ToNode(BasicBlock block)
    {
        var textLines = block.Operations
            .Select(operation => operation.Syntax.ToString().Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (block.BranchValue is not null && HasReturnSuccessor(block))
        {
            textLines.Add($"Return {block.BranchValue.Syntax.ToString().Trim()}");
        }
        else if (block.BranchValue is not null)
        {
            textLines.Add($"Condition: {block.BranchValue.Syntax.ToString().Trim()}");
        }

        return new ControlFlowNode(
            block.Ordinal,
            block.Kind.ToString(),
            string.Join(Environment.NewLine, textLines));
    }

    private static IEnumerable<ControlFlowEdge> ToEdges(BasicBlock block)
    {
        var condition = block.BranchValue?.Syntax.ToString().Trim();

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
            ControlFlowConditionKind.WhenTrue => isConditionalSuccessor ? "True" : "False",
            ControlFlowConditionKind.WhenFalse => isConditionalSuccessor ? "False" : "True",
            _ => isConditionalSuccessor ? "Conditional" : "FallThrough"
        };
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
}
