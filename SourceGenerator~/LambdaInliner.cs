using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ULinq.SourceGenerator;

/// <summary>
/// Rewrites the inlined method body:
/// - Replaces the receiver parameter name with the actual receiver expression
/// - Expands delegate invocations with the lambda body
/// </summary>
internal sealed class LambdaInliner : CSharpSyntaxRewriter
{
    readonly string _receiverParamName;
    readonly ExpressionSyntax _receiverExpr;
    readonly Dictionary<string, ExpressionSyntax> _delegateArgs;
    readonly Counter _counter;
    readonly Dictionary<string, DelegateTypeInfo> _delegateTypeInfos;
    readonly Dictionary<string, List<(string Name, string Type)>> _delegateCaptures;
    readonly List<MemberDeclarationSyntax> _generatedMethods;
    readonly List<Diagnostic> _diagnostics;
    readonly Location _invocationLocation;
    readonly List<StatementSyntax> _hoistedStatements = new List<StatementSyntax>();

    public LambdaInliner(
        string receiverParamName,
        ExpressionSyntax receiverExpr,
        Dictionary<string, ExpressionSyntax> delegateArgs,
        Counter counter,
        Dictionary<string, DelegateTypeInfo> delegateTypeInfos,
        Dictionary<string, List<(string Name, string Type)>> delegateCaptures,
        List<MemberDeclarationSyntax> generatedMethods,
        List<Diagnostic> diagnostics,
        Location invocationLocation)
    {
        _receiverParamName = receiverParamName;
        _receiverExpr = receiverExpr;
        _delegateArgs = delegateArgs;
        _counter = counter;
        _delegateTypeInfos = delegateTypeInfos;
        _delegateCaptures = delegateCaptures;
        _generatedMethods = generatedMethods;
        _diagnostics = diagnostics;
        _invocationLocation = invocationLocation;
    }

    public List<StatementSyntax> DrainHoistedStatements()
    {
        if (_hoistedStatements.Count == 0) return null;
        var result = new List<StatementSyntax>(_hoistedStatements);
        _hoistedStatements.Clear();
        return result;
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.Text == _receiverParamName)
        {
            if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                return base.VisitIdentifierName(node);

            return _receiverExpr.WithTriviaFrom(node);
        }
        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax invocation
            && invocation.Expression is IdentifierNameSyntax id
            && _delegateArgs.TryGetValue(id.Identifier.Text, out var lambdaExpr))
        {
            if (lambdaExpr is LambdaExpressionSyntax lambda)
            {
                var visitedArgs = (ArgumentListSyntax)VisitArgumentList(invocation.ArgumentList);
                return ExpandLambdaStatement(lambda, visitedArgs);
            }
        }

        var result = base.VisitExpressionStatement(node);
        var hoisted = DrainHoistedStatements();
        if (hoisted != null)
        {
            hoisted.Add(result is StatementSyntax s ? s : (StatementSyntax)result);
            return SyntaxFactory.Block(hoisted);
        }
        return result;
    }

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = new List<StatementSyntax>();
        bool changed = false;

        foreach (var stmt in node.Statements)
        {
            var visited = (SyntaxNode)Visit(stmt);
            var hoisted = DrainHoistedStatements();
            if (hoisted != null)
            {
                newStatements.AddRange(hoisted);
                changed = true;
            }
            if (visited is BlockSyntax block)
            {
                newStatements.AddRange(block.Statements);
                changed = true;
            }
            else if (visited is StatementSyntax s)
            {
                newStatements.Add(s);
                if (s != stmt) changed = true;
            }
        }

        return changed ? node.WithStatements(SyntaxFactory.List(newStatements)) : node;
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is IdentifierNameSyntax id
            && _delegateArgs.TryGetValue(id.Identifier.Text, out var lambdaExpr))
        {
            if (lambdaExpr is LambdaExpressionSyntax lambda)
            {
                var visitedArgs = (ArgumentListSyntax)VisitArgumentList(node.ArgumentList);
                if (lambda.Body is ExpressionSyntax)
                    return ExpandLambdaExpression(lambda, visitedArgs).WithTriviaFrom(node);

                // Block lambda in expression context: resolve type info eagerly
                DelegateTypeInfo? typeInfo = _delegateTypeInfos.TryGetValue(id.Identifier.Text, out var ti) ? ti : null;
                _delegateCaptures.TryGetValue(id.Identifier.Text, out var captures);
                return ExpandBlockLambdaExpression(typeInfo, captures, lambda, visitedArgs).WithTriviaFrom(node);
            }
        }
        return base.VisitInvocationExpression(node);
    }

    SyntaxNode ExpandLambdaStatement(LambdaExpressionSyntax lambda, ArgumentListSyntax args)
    {
        var paramNames = GetLambdaParameterNames(lambda);
        var argExprs = args.Arguments.Select(a => a.Expression).ToArray();

        if (lambda.Body is BlockSyntax block)
        {
            var declarations = new List<StatementSyntax>();
            var paramRenames = new Dictionary<string, string>();
            for (int i = 0; i < paramNames.Count && i < argExprs.Length; i++)
            {
                var uniqueName = $"__{paramNames[i]}_{_counter.Next()}";
                paramRenames[paramNames[i]] = uniqueName;
                declarations.Add(SyntaxFactory.ParseStatement(
                    $"var {uniqueName} = {argExprs[i].ToFullString().Trim()};"));
            }

            var renamedBlock = (BlockSyntax)new LocalVariableRenamer(paramRenames).Visit(block);
            declarations.AddRange(renamedBlock.Statements);
            return SyntaxFactory.Block(declarations);
        }
        else
        {
            var replacer = new ParameterReplacer(paramNames, argExprs);
            var replaced = replacer.Visit(lambda.Body);
            return SyntaxFactory.ExpressionStatement(
                (ExpressionSyntax)replaced,
                SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }
    }

    ExpressionSyntax ExpandLambdaExpression(LambdaExpressionSyntax lambda, ArgumentListSyntax args)
    {
        var paramNames = GetLambdaParameterNames(lambda);
        var argExprs = args.Arguments.Select(a => a.Expression).ToArray();
        var replacer = new ParameterReplacer(paramNames, argExprs);
        var result = (ExpressionSyntax)replacer.Visit(lambda.Body);
        return SyntaxFactory.ParenthesizedExpression(result);
    }

    /// <summary>
    /// Expands a block lambda in expression context by hoisting non-return statements
    /// and converting if/return patterns into conditional expressions.
    /// Falls back to method extraction when TrySplitBlockReturns fails (e.g. loop with return).
    /// </summary>
    ExpressionSyntax ExpandBlockLambdaExpression(
        DelegateTypeInfo? typeInfo, List<(string Name, string Type)> captures,
        LambdaExpressionSyntax lambda, ArgumentListSyntax args)
    {
        if (lambda.Body is not BlockSyntax block)
            return ExpandLambdaExpression(lambda, args);

        var paramNames = GetLambdaParameterNames(lambda);
        var argExprs = args.Arguments.Select(a => a.Expression).ToArray();

        // Create temp variables for parameters, rename block locals
        var paramRenames = new Dictionary<string, string>();
        var tempDecls = new List<StatementSyntax>();
        for (int i = 0; i < paramNames.Count && i < argExprs.Length; i++)
        {
            var uniqueName = $"__{paramNames[i]}_{_counter.Next()}";
            paramRenames[paramNames[i]] = uniqueName;
            tempDecls.Add(SyntaxFactory.ParseStatement(
                $"var {uniqueName} = {argExprs[i].ToFullString().Trim()};"));
        }

        var renamedBlock = _counter.RenameLocals(block, paramRenames);

        var hoistable = new List<StatementSyntax>();
        var returnCache = new Dictionary<SyntaxNode, bool>();
        if (TrySplitBlockReturns(renamedBlock.Statements, hoistable, returnCache, out var returnExpr))
        {
            _hoistedStatements.AddRange(tempDecls);
            _hoistedStatements.AddRange(hoistable);
            return SyntaxFactory.ParenthesizedExpression(returnExpr);
        }

        // Fallback: extract to private method
        if (typeInfo.HasValue)
            return ExtractToMethod(typeInfo.Value, captures, paramNames, argExprs, block);

        // Last resort (type info unavailable)
        _diagnostics.Add(Diagnostic.Create(ULinq.SourceGenerator.Diagnostics.UL0001, _invocationLocation));
        _hoistedStatements.AddRange(tempDecls);
        _hoistedStatements.AddRange(renamedBlock.Statements);
        return SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
    }

    ExpressionSyntax ExtractToMethod(
        DelegateTypeInfo typeInfo,
        List<(string Name, string Type)> captures,
        List<string> paramNames, ExpressionSyntax[] argExprs, BlockSyntax originalBlock)
    {
        captures ??= new List<(string, string)>();
        var methodName = $"__Lambda_{_counter.Next()}";

        // Parameters: lambda params + captures
        var parameters = new List<ParameterSyntax>();
        for (int i = 0; i < paramNames.Count && i < typeInfo.ParamTypes.Length; i++)
            parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramNames[i]))
                .WithType(SyntaxFactory.ParseTypeName(typeInfo.ParamTypes[i] + " ")));
        foreach (var (name, type) in captures)
            parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(name))
                .WithType(SyntaxFactory.ParseTypeName(type + " ")));

        var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.ParseTypeName(typeInfo.ReturnType), methodName)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
            .WithBody(originalBlock);
        _generatedMethods.Add(method);
        _diagnostics.Add(Diagnostic.Create(ULinq.SourceGenerator.Diagnostics.UL0002, _invocationLocation, methodName));

        // Build call: __Lambda_N(arg0, ..., capture0, ...)
        var callArgs = new List<ArgumentSyntax>();
        for (int i = 0; i < argExprs.Length && i < paramNames.Count; i++)
            callArgs.Add(SyntaxFactory.Argument(argExprs[i]));
        foreach (var (name, _) in captures)
            callArgs.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name)));

        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(methodName),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(callArgs)));
    }

    /// <summary>Returns true if the statement or any descendant contains a ReturnStatementSyntax.</summary>
    static bool ContainsReturn(StatementSyntax stmt, Dictionary<SyntaxNode, bool> cache)
    {
        if (cache.TryGetValue(stmt, out var cached)) return cached;
        var result = stmt is ReturnStatementSyntax || stmt.DescendantNodes().OfType<ReturnStatementSyntax>().Any();
        cache[stmt] = result;
        return result;
    }

    /// <summary>
    /// Splits a statement list into hoistable prefix statements and a conditional return expression.
    /// </summary>
    static bool TrySplitBlockReturns(
        SyntaxList<StatementSyntax> statements, List<StatementSyntax> hoistable,
        Dictionary<SyntaxNode, bool> returnCache, out ExpressionSyntax returnExpr)
        => TrySplitBlockReturns(statements.ToArray(), hoistable, returnCache, out returnExpr);

    static bool TrySplitBlockReturns(
        IReadOnlyList<StatementSyntax> statements, List<StatementSyntax> hoistable,
        Dictionary<SyntaxNode, bool> returnCache, out ExpressionSyntax returnExpr)
    {
        returnExpr = null;
        for (int i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i];

            // Direct return → extract expression
            if (stmt is ReturnStatementSyntax ret && ret.Expression != null)
            {
                returnExpr = ret.Expression;
                return true;
            }

            // If statement containing return → build conditional
            if (stmt is IfStatementSyntax ifStmt && ContainsReturn(ifStmt, returnCache))
            {
                var remaining = new List<StatementSyntax>();
                for (int j = i + 1; j < statements.Count; j++)
                    remaining.Add(statements[j]);
                return TryBuildConditional(ifStmt, remaining, hoistable, returnCache, out returnExpr);
            }

            // Non-if statement containing return → cannot inline, bail out
            if (ContainsReturn(stmt, returnCache))
                return false;

            // No return in this statement → hoist it
            hoistable.Add(stmt);
        }
        return false;
    }

    /// <summary>
    /// Builds a conditional expression from an if statement and remaining statements.
    /// if (c) return a; ... return b; → c ? a : b
    /// </summary>
    static bool TryBuildConditional(
        IfStatementSyntax ifStmt,
        List<StatementSyntax> remaining,
        List<StatementSyntax> hoistable,
        Dictionary<SyntaxNode, bool> returnCache,
        out ExpressionSyntax conditional)
    {
        conditional = null;

        // Extract then-branch expression
        if (!TryExtractBranchExpr(ifStmt.Statement, hoistable, returnCache, out var thenExpr))
            return false;

        ExpressionSyntax elseExpr;
        if (ifStmt.Else != null)
        {
            // else branch exists → extract from it
            if (!TryExtractBranchExpr(ifStmt.Else.Statement, hoistable, returnCache, out elseExpr))
                return false;
        }
        else
        {
            // No else → remaining statements serve as else
            if (!TrySplitBlockReturns(remaining, hoistable, returnCache, out elseExpr))
                return false;
        }

        conditional = SyntaxFactory.ConditionalExpression(ifStmt.Condition, thenExpr, elseExpr);
        return true;
    }

    /// <summary>
    /// Extracts a return expression from a branch statement.
    /// Handles: return expr; | { stmts; return expr; } | if (...) ...
    /// </summary>
    static bool TryExtractBranchExpr(
        StatementSyntax stmt, List<StatementSyntax> hoistable,
        Dictionary<SyntaxNode, bool> returnCache, out ExpressionSyntax expr)
    {
        expr = null;

        if (stmt is ReturnStatementSyntax ret && ret.Expression != null)
        {
            expr = ret.Expression;
            return true;
        }

        if (stmt is BlockSyntax block)
            return TrySplitBlockReturns(block.Statements, hoistable, returnCache, out expr);

        if (stmt is IfStatementSyntax nestedIf)
            return TryBuildConditional(nestedIf, new List<StatementSyntax>(), hoistable, returnCache, out expr);

        return false;
    }

    internal static List<string> GetLambdaParameterNames(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple =>
                new List<string> { simple.Parameter.Identifier.Text },
            ParenthesizedLambdaExpressionSyntax paren =>
                paren.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList(),
            _ => new List<string>()
        };
    }
}
