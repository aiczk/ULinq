using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonLambda.SourceGenerator;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor UL0001 = new(
        "UL0001", "Unresolved block lambda",
        "Block lambda could not be resolved; output may be incorrect",
        "UdonLambda", DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UL0002 = new(
        "UL0002", "Lambda extracted to method",
        "Block lambda extracted to private method '{0}'",
        "UdonLambda", DiagnosticSeverity.Info, true);
}

internal readonly struct InlineMethodInfo
{
    public readonly IMethodSymbol Symbol;
    public readonly MethodDeclarationSyntax Syntax;

    public InlineMethodInfo(IMethodSymbol symbol, MethodDeclarationSyntax syntax)
    {
        Symbol = symbol;
        Syntax = syntax;
    }
}

internal readonly struct InlineExpansion
{
    public readonly List<StatementSyntax> Statements;
    public readonly ExpressionSyntax ReturnExpression;

    public InlineExpansion(List<StatementSyntax> statements, ExpressionSyntax returnExpression)
    {
        Statements = statements;
        ReturnExpression = returnExpression;
    }
}

internal readonly struct DelegateTypeInfo
{
    public readonly string ReturnType;
    public readonly string[] ParamTypes;
    public DelegateTypeInfo(string returnType, string[] paramTypes)
    {
        ReturnType = returnType;
        ParamTypes = paramTypes;
    }
}

/// <summary>Shared mutable counter for generating unique variable names across rewriters.</summary>
internal sealed class Counter
{
    public int Value;
    public int Next() => Value++;

    /// <summary>Renames all locals in a block using scope-aware renaming (sibling scopes get unique names).</summary>
    public BlockSyntax RenameLocals(BlockSyntax block, Dictionary<string, string> seed = null)
        => (BlockSyntax)new ScopedRenamer(this, seed).Visit(block);
}

internal sealed class UdonLambdaRewriter : CSharpSyntaxRewriter
{
    readonly SemanticModel _model;
    readonly Dictionary<IMethodSymbol, InlineMethodInfo> _inlineBySymbol;
    readonly Counter _counter = new Counter();
    readonly List<MemberDeclarationSyntax> _generatedMethods = new();
    readonly List<Diagnostic> _diagnostics = new();
    readonly List<StatementSyntax> _pendingStatements = new();

    public bool HasTransformed { get; private set; }
    public IReadOnlyList<MemberDeclarationSyntax> GeneratedMethods => _generatedMethods;
    public ImmutableArray<Diagnostic> CollectedDiagnostics => _diagnostics.ToImmutableArray();

    public UdonLambdaRewriter(SemanticModel model, List<InlineMethodInfo> inlineMethods)
    {
        _model = model;
        _inlineBySymbol = new Dictionary<IMethodSymbol, InlineMethodInfo>(SymbolEqualityComparer.Default);
        foreach (var m in inlineMethods)
            _inlineBySymbol[m.Symbol] = m;
    }

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.ExpressionBody != null && HasInlineCall(node.ExpressionBody.Expression))
        {
            var (expanded, hoisted) = ExpandInlineCallsInExpression(node.ExpressionBody.Expression);
            if (hoisted.Count > 0 || expanded != node.ExpressionBody.Expression)
            {
                HasTransformed = true;
                var stmts = new List<StatementSyntax>(hoisted);
                if (node.ReturnType.ToString() != "void")
                    stmts.Add(SyntaxFactory.ReturnStatement(expanded));
                else
                    stmts.Add(SyntaxFactory.ExpressionStatement(expanded));
                return node.WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken))
                    .WithBody(SyntaxFactory.Block(stmts));
            }
        }
        return base.VisitMethodDeclaration(node);
    }

    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (node.ExpressionBody != null && HasInlineCall(node.ExpressionBody.Expression))
        {
            var (expanded, hoisted) = ExpandInlineCallsInExpression(node.ExpressionBody.Expression);
            if (hoisted.Count > 0 || expanded != node.ExpressionBody.Expression)
            {
                HasTransformed = true;
                var stmts = new List<StatementSyntax>(hoisted);
                stmts.Add(SyntaxFactory.ReturnStatement(expanded));
                var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithBody(SyntaxFactory.Block(stmts));
                return node.WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken))
                    .WithAccessorList(SyntaxFactory.AccessorList(
                        SyntaxFactory.SingletonList(getter)));
            }
        }
        return base.VisitPropertyDeclaration(node);
    }

    public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        // Extract invocation from direct call or assignment RHS
        InvocationExpressionSyntax invocation;
        AssignmentExpressionSyntax assignment = null;
        if (node.Expression is InvocationExpressionSyntax directCall)
            invocation = directCall;
        else if (node.Expression is AssignmentExpressionSyntax assign
                 && assign.Right is InvocationExpressionSyntax rhsCall)
            (invocation, assignment) = (rhsCall, assign);
        else
            return base.VisitExpressionStatement(node);

        if (!TryResolve(invocation, out var method, out var receiver))
            return base.VisitExpressionStatement(node);

        var expansion = ExpandInlineMethod(receiver, invocation, method);
        HasTransformed = true;
        var statements = new List<StatementSyntax>(expansion.Statements);
        if (assignment != null && expansion.ReturnExpression != null)
        {
            statements.Add(SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    assignment.Kind(), assignment.Left, expansion.ReturnExpression)));
        }
        return WrapStatements(statements);
    }

    public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        if (node.Declaration.Variables.Count == 1)
        {
            var declarator = node.Declaration.Variables[0];
            if (declarator.Initializer?.Value is InvocationExpressionSyntax invocation
                && TryResolve(invocation, out var method, out var receiver))
            {
                var expansion = ExpandInlineMethod(receiver, invocation, method);
                HasTransformed = true;
                var statements = new List<StatementSyntax>(expansion.Statements);
                if (expansion.ReturnExpression != null)
                {
                    var newDeclarator = declarator.WithInitializer(
                        SyntaxFactory.EqualsValueClause(expansion.ReturnExpression));
                    var newDecl = node.Declaration.WithVariables(
                        SyntaxFactory.SingletonSeparatedList(newDeclarator));
                    statements.Add(node.WithDeclaration(newDecl));
                }
                return WrapStatements(statements);
            }
        }
        return base.VisitLocalDeclarationStatement(node);
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (TryResolve(node, out var method, out var receiver))
        {
            var expansion = ExpandInlineMethod(receiver, node, method);
            HasTransformed = true;
            _pendingStatements.AddRange(expansion.Statements);
            return expansion.ReturnExpression
                ?? SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
        }
        return base.VisitInvocationExpression(node);
    }

    bool HasInlineCall(ExpressionSyntax expr)
    {
        if (expr == null) return false;
        foreach (var inv in expr.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            if (TryResolve(inv, out _, out _)) return true;
        return false;
    }

    /// <summary>
    /// Expands top-level inline calls within an expression.
    /// Chains are handled by ExpandInlineMethod internally; only outermost calls are expanded here.
    /// </summary>
    (ExpressionSyntax expanded, List<StatementSyntax> hoisted) ExpandInlineCallsInExpression(ExpressionSyntax expr)
    {
        var hoisted = new List<StatementSyntax>();
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>();

        foreach (var inv in expr.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (!TryResolve(inv, out var method, out var receiver)) continue;

            // Skip if nested under another inline call within this expression
            bool nested = false;
            if (inv != expr)
            {
                foreach (var ancestor in inv.Ancestors())
                {
                    if (ancestor is InvocationExpressionSyntax a && TryResolve(a, out _, out _))
                    { nested = true; break; }
                    if (ancestor == expr) break;
                }
            }
            if (nested) continue;

            var expansion = ExpandInlineMethod(receiver, inv, method);
            hoisted.AddRange(expansion.Statements);
            replacements[inv] = expansion.ReturnExpression
                ?? SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
        }

        if (replacements.Count == 0) return (expr, hoisted);
        var result = expr.ReplaceNodes(replacements.Keys, (orig, _) => (ExpressionSyntax)replacements[orig]);
        return (result, hoisted);
    }

    public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
    {
        if (!HasInlineCall(node.Condition))
            return base.VisitWhileStatement(node);

        var visitedCondition = (ExpressionSyntax)Visit(node.Condition);
        var pending = new List<StatementSyntax>(_pendingStatements);
        _pendingStatements.Clear();
        var visitedBody = (StatementSyntax)Visit(node.Statement);

        var stmts = new List<StatementSyntax>(pending);
        stmts.Add(IfNotBreak(visitedCondition));
        FlattenInto(stmts, visitedBody);

        return SyntaxFactory.WhileStatement(
            SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression),
            SyntaxFactory.Block(stmts));
    }

    public override SyntaxNode VisitForStatement(ForStatementSyntax node)
    {
        if (!HasInlineCall(node.Condition))
            return base.VisitForStatement(node);

        var visitedCondition = (ExpressionSyntax)Visit(node.Condition);
        var pending = new List<StatementSyntax>(_pendingStatements);
        _pendingStatements.Clear();
        var visitedBody = (StatementSyntax)Visit(node.Statement);

        var stmts = new List<StatementSyntax>(pending);
        stmts.Add(IfNotBreak(visitedCondition));
        FlattenInto(stmts, visitedBody);

        return node.WithCondition(null).WithStatement(SyntaxFactory.Block(stmts));
    }

    static IfStatementSyntax IfNotBreak(ExpressionSyntax condition)
        => SyntaxFactory.IfStatement(
            SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression(condition)),
            SyntaxFactory.BreakStatement());

    static void FlattenInto(List<StatementSyntax> target, StatementSyntax body)
    {
        if (body is BlockSyntax block)
            target.AddRange(block.Statements);
        else
            target.Add(body);
    }

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = new List<StatementSyntax>();
        bool changed = false;

        foreach (var stmt in node.Statements)
        {
            var visited = (SyntaxNode)Visit(stmt);

            // Drain pending statements from VisitInvocationExpression
            if (_pendingStatements.Count > 0)
            {
                newStatements.AddRange(_pendingStatements);
                _pendingStatements.Clear();
                changed = true;
            }

            // Flatten blocks introduced by expansion (existing logic)
            if (visited is BlockSyntax block && stmt is not BlockSyntax)
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

    bool TryResolve(InvocationExpressionSyntax invocation, out InlineMethodInfo method, out ExpressionSyntax receiver)
    {
        method = default;
        receiver = null;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
        if (_model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol resolved) return false;
        // ReducedFrom handles the case where an extension method call resolves to a reduced form
        var lookupKey = resolved.ReducedFrom?.OriginalDefinition ?? resolved.OriginalDefinition;
        if (!_inlineBySymbol.TryGetValue(lookupKey, out method)) return false;
        receiver = memberAccess.Expression;
        return true;
    }

    InlineExpansion ExpandInlineMethod(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        InlineMethodInfo method)
    {
        var prefixStatements = new List<StatementSyntax>();

        // Handle chained receiver: expand inner inline calls first
        if (receiver is InvocationExpressionSyntax innerInvocation
            && TryResolve(innerInvocation, out var innerMethod, out var innerReceiver))
        {
            var innerExpansion = ExpandInlineMethod(innerReceiver, innerInvocation, innerMethod);
            prefixStatements.AddRange(innerExpansion.Statements);

            if (innerExpansion.ReturnExpression != null)
            {
                var tempName = $"__chain_{_counter.Next()}";
                prefixStatements.Add(SyntaxFactory.ParseStatement(
                    $"var {tempName} = {innerExpansion.ReturnExpression.NormalizeWhitespace().ToFullString()};"));
                receiver = SyntaxFactory.IdentifierName(tempName);
            }
        }
        else if (!IsSimpleReceiver(receiver))
        {
            var tempName = $"__receiver_{_counter.Next()}";
            prefixStatements.Add(SyntaxFactory.ParseStatement(
                $"var {tempName} = {receiver.NormalizeWhitespace().ToFullString()};"));
            receiver = SyntaxFactory.IdentifierName(tempName);
        }

        var callArgs = invocation.ArgumentList;
        var methodParams = method.Symbol.Parameters;

        BlockSyntax methodBody = method.Syntax.Body;
        if (methodBody == null && method.Syntax.ExpressionBody != null)
        {
            var retStmt = SyntaxFactory.ReturnStatement(method.Syntax.ExpressionBody.Expression);
            methodBody = SyntaxFactory.Block(retStmt);
        }
        if (methodBody == null)
            return new InlineExpansion(prefixStatements, null);

        // Replace type parameters with concrete types from semantic model
        var typeParamMap = ResolveTypeParameters(invocation);

        // Build parameter mapping: first param = receiver (this), rest = call args
        var receiverParam = methodParams[0];
        var (delegateArgs, delegateTypeInfos, delegateCaptures, valueParamNames, valueParamExprs) =
            BuildParameterMappings(methodParams, callArgs, invocation);

        var expansion = ProcessMethodBody(
            methodBody, receiver, receiverParam.Name,
            delegateArgs, delegateTypeInfos, delegateCaptures,
            valueParamNames, valueParamExprs, typeParamMap,
            invocation.GetLocation());

        prefixStatements.AddRange(expansion.Statements);
        return new InlineExpansion(prefixStatements, expansion.ReturnExpression);
    }

    InlineExpansion ProcessMethodBody(
        BlockSyntax methodBody,
        ExpressionSyntax receiver,
        string receiverParamName,
        Dictionary<string, ExpressionSyntax> delegateArgs,
        Dictionary<string, DelegateTypeInfo> delegateTypeInfos,
        Dictionary<string, List<(string Name, string Type)>> delegateCaptures,
        List<string> valueParamNames,
        List<ExpressionSyntax> valueParamExprs,
        Dictionary<string, string> typeParamMap,
        Location location)
    {
        if (typeParamMap.Count > 0)
            methodBody = (BlockSyntax)new TypeParameterReplacer(typeParamMap).Visit(methodBody);

        var renamedBody = _counter.RenameLocals(methodBody);

        if (valueParamNames.Count > 0)
            renamedBody = (BlockSyntax)new ParameterReplacer(valueParamNames, valueParamExprs.ToArray()).Visit(renamedBody);

        var inliner = new LambdaInliner(
            receiverParamName,
            receiver.ToFullString().Trim(),
            delegateArgs,
            _counter,
            delegateTypeInfos,
            delegateCaptures,
            _generatedMethods,
            _diagnostics,
            location);

        var statements = new List<StatementSyntax>();
        ExpressionSyntax returnExpr = null;

        foreach (var stmt in renamedBody.Statements)
        {
            if (stmt is ReturnStatementSyntax returnStmt && returnStmt.Expression != null)
            {
                returnExpr = (ExpressionSyntax)inliner.Visit(returnStmt.Expression);
                var hoisted = inliner.DrainHoistedStatements();
                if (hoisted != null) statements.AddRange(hoisted);
                continue;
            }

            var result = inliner.Visit(stmt);
            var hoistedAfter = inliner.DrainHoistedStatements();
            if (hoistedAfter != null) statements.AddRange(hoistedAfter);
            if (result is BlockSyntax block)
                statements.AddRange(block.Statements);
            else if (result is StatementSyntax s)
                statements.Add(s);
        }

        return new InlineExpansion(statements, returnExpr);
    }

    (Dictionary<string, ExpressionSyntax> delegateArgs,
     Dictionary<string, DelegateTypeInfo> delegateTypeInfos,
     Dictionary<string, List<(string Name, string Type)>> delegateCaptures,
     List<string> valueParamNames,
     List<ExpressionSyntax> valueParamExprs)
    BuildParameterMappings(
        ImmutableArray<IParameterSymbol> methodParams,
        ArgumentListSyntax callArgs,
        InvocationExpressionSyntax invocation)
    {
        var delegateArgs = new Dictionary<string, ExpressionSyntax>();
        var delegateTypeInfos = new Dictionary<string, DelegateTypeInfo>();
        var delegateCaptures = new Dictionary<string, List<(string Name, string Type)>>();
        var valueParamNames = new List<string>();
        var valueParamExprs = new List<ExpressionSyntax>();

        for (int i = 1; i < methodParams.Length; i++)
        {
            if (i - 1 >= callArgs.Arguments.Count) continue;
            var argExpr = callArgs.Arguments[i - 1].Expression;
            if (argExpr is LambdaExpressionSyntax lambda)
            {
                if (lambda.Body is BlockSyntax)
                    delegateCaptures[methodParams[i].Name] = AnalyzeCaptures(lambda);
                delegateArgs[methodParams[i].Name] = PreExpandLambdaBody(lambda);
            }
            else
            {
                valueParamNames.Add(methodParams[i].Name);
                valueParamExprs.Add(argExpr);
            }
        }

        // Resolve delegate type info from semantic model
        if (_model.GetSymbolInfo(invocation).Symbol is IMethodSymbol resolved)
        {
            // GetSymbolInfo returns the reduced form for extension methods
            // (this parameter stripped), so offset by 1 for parameter mapping
            var paramOffset = resolved.ReducedFrom != null ? 1 : 0;
            for (int i = 1; i < methodParams.Length; i++)
            {
                var resolvedIdx = i - paramOffset;
                if (resolvedIdx < 0 || resolvedIdx >= resolved.Parameters.Length) continue;
                var paramName = methodParams[i].Name;
                if (!delegateArgs.ContainsKey(paramName)) continue;
                if (resolved.Parameters[resolvedIdx].Type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke })
                {
                    delegateTypeInfos[paramName] = new DelegateTypeInfo(
                        invoke.ReturnsVoid ? "void" : invoke.ReturnType.ToDisplayString(),
                        invoke.Parameters.Select(p => p.Type.ToDisplayString()).ToArray());
                }
            }
        }

        return (delegateArgs, delegateTypeInfos, delegateCaptures, valueParamNames, valueParamExprs);
    }

    Dictionary<string, string> ResolveTypeParameters(InvocationExpressionSyntax invocation)
    {
        var map = new Dictionary<string, string>();
        if (_model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { IsGenericMethod: true } resolved)
            return map;
        for (int i = 0; i < resolved.TypeParameters.Length; i++)
            map[resolved.TypeParameters[i].Name] = resolved.TypeArguments[i].ToDisplayString();
        return map;
    }

    /// <summary>
    /// Pre-expands inline calls inside a lambda body so that nested lambdas
    /// are fully expanded before the outer template processes them.
    /// </summary>
    ExpressionSyntax PreExpandLambdaBody(LambdaExpressionSyntax lambda)
    {
        if (lambda.Body is ExpressionSyntax exprBody)
        {
            // Expression lambda whose body is an inline call -> expand to block lambda
            if (exprBody is InvocationExpressionSyntax inv && TryResolve(inv, out var method, out var receiver))
            {
                var expansion = ExpandInlineMethod(receiver, inv, method);
                var stmts = new List<StatementSyntax>(expansion.Statements);
                if (expansion.ReturnExpression != null)
                    stmts.Add(SyntaxFactory.ReturnStatement(expansion.ReturnExpression));
                // void methods (e.g. ForEach): expansion.Statements already contains the expanded code
                return WithBlockBody(lambda, SyntaxFactory.Block(stmts));
            }

            // Body contains inline calls in sub-expressions → expand and convert to block lambda
            if (HasInlineCall(exprBody))
            {
                var (expanded, hoisted) = ExpandInlineCallsInExpression(exprBody);
                hoisted.Add(SyntaxFactory.ReturnStatement(expanded));
                return WithBlockBody(lambda, SyntaxFactory.Block(hoisted));
            }

            return lambda;
        }

        if (lambda.Body is BlockSyntax block)
        {
            var newStatements = new List<StatementSyntax>();
            bool changed = false;

            foreach (var stmt in block.Statements)
            {
                InvocationExpressionSyntax invocation = null;

                if (stmt is ExpressionStatementSyntax exprStmt)
                    invocation = exprStmt.Expression as InvocationExpressionSyntax;
                else if (stmt is ReturnStatementSyntax retStmt)
                    invocation = retStmt.Expression as InvocationExpressionSyntax;
                else if (stmt is LocalDeclarationStatementSyntax localDecl
                         && localDecl.Declaration.Variables.Count == 1)
                    invocation = localDecl.Declaration.Variables[0].Initializer?.Value as InvocationExpressionSyntax;

                if (invocation != null && TryResolve(invocation, out var m, out var r))
                {
                    var exp = ExpandInlineMethod(r, invocation, m);
                    newStatements.AddRange(exp.Statements);

                    if (stmt is ReturnStatementSyntax && exp.ReturnExpression != null)
                        newStatements.Add(SyntaxFactory.ReturnStatement(exp.ReturnExpression));
                    else if (stmt is LocalDeclarationStatementSyntax ld && exp.ReturnExpression != null)
                    {
                        var decl = ld.Declaration.Variables[0];
                        var newDecl = decl.WithInitializer(SyntaxFactory.EqualsValueClause(exp.ReturnExpression));
                        newStatements.Add(ld.WithDeclaration(
                            ld.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(newDecl))));
                    }
                    else if (exp.ReturnExpression != null)
                        newStatements.Add(SyntaxFactory.ExpressionStatement(exp.ReturnExpression));

                    changed = true;
                }
                else if (stmt is WhileStatementSyntax whileStmt && HasInlineCall(whileStmt.Condition))
                {
                    var (expandedCond, condHoisted) = ExpandInlineCallsInExpression(whileStmt.Condition);
                    var bodyStmts = new List<StatementSyntax>(condHoisted);
                    bodyStmts.Add(IfNotBreak(expandedCond));
                    FlattenInto(bodyStmts, whileStmt.Statement);
                    newStatements.Add(SyntaxFactory.WhileStatement(
                        SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression),
                        SyntaxFactory.Block(bodyStmts)));
                    changed = true;
                }
                else if (stmt is ForStatementSyntax forStmt && HasInlineCall(forStmt.Condition))
                {
                    var (expandedCond, condHoisted) = ExpandInlineCallsInExpression(forStmt.Condition);
                    var bodyStmts = new List<StatementSyntax>(condHoisted);
                    bodyStmts.Add(IfNotBreak(expandedCond));
                    FlattenInto(bodyStmts, forStmt.Statement);
                    newStatements.Add(forStmt.WithCondition(null).WithStatement(SyntaxFactory.Block(bodyStmts)));
                    changed = true;
                }
                else
                {
                    // Check for inline calls in sub-expressions of the statement
                    ExpressionSyntax stmtExpr = stmt switch
                    {
                        ReturnStatementSyntax ret => ret.Expression,
                        ExpressionStatementSyntax es => es.Expression,
                        LocalDeclarationStatementSyntax ld2 when ld2.Declaration.Variables.Count == 1
                            => ld2.Declaration.Variables[0].Initializer?.Value,
                        _ => null
                    };

                    if (stmtExpr != null && HasInlineCall(stmtExpr))
                    {
                        var (expanded, hoisted) = ExpandInlineCallsInExpression(stmtExpr);
                        newStatements.AddRange(hoisted);

                        if (stmt is ReturnStatementSyntax)
                            newStatements.Add(SyntaxFactory.ReturnStatement(expanded));
                        else if (stmt is ExpressionStatementSyntax)
                            newStatements.Add(SyntaxFactory.ExpressionStatement(expanded));
                        else if (stmt is LocalDeclarationStatementSyntax ld2)
                        {
                            var decl = ld2.Declaration.Variables[0];
                            var newDecl = decl.WithInitializer(SyntaxFactory.EqualsValueClause(expanded));
                            newStatements.Add(ld2.WithDeclaration(
                                ld2.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(newDecl))));
                        }
                        changed = true;
                    }
                    else
                    {
                        newStatements.Add(stmt);
                    }
                }
            }

            if (changed)
                return WithBlockBody(lambda, SyntaxFactory.Block(newStatements));
        }

        return lambda;
    }

    static LambdaExpressionSyntax WithBlockBody(LambdaExpressionSyntax lambda, BlockSyntax block)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.WithBlock(block).WithExpressionBody(null),
            ParenthesizedLambdaExpressionSyntax paren => paren.WithBlock(block).WithExpressionBody(null),
            _ => lambda
        };
    }

    static SyntaxNode WrapStatements(List<StatementSyntax> statements)
    {
        if (statements.Count == 1)
            return statements[0]
                .WithLeadingTrivia(SyntaxFactory.ElasticLineFeed)
                .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        return SyntaxFactory.Block(statements)
            .WithLeadingTrivia(SyntaxFactory.ElasticLineFeed)
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);
    }

    static bool IsSimpleReceiver(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax => true,
        ThisExpressionSyntax => true,
        MemberAccessExpressionSyntax ma => IsSimpleReceiver(ma.Expression),
        _ => false
    };

    List<(string Name, string Type)> AnalyzeCaptures(LambdaExpressionSyntax lambda)
    {
        var captures = new List<(string, string)>();
        var seen = new HashSet<string>();
        var lambdaParams = new HashSet<string>(LambdaInliner.GetLambdaParameterNames(lambda));

        foreach (var id in lambda.Body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (seen.Contains(id.Identifier.Text)) continue;
            var sym = _model.GetSymbolInfo(id).Symbol;
            switch (sym)
            {
                case ILocalSymbol local when local.DeclaringSyntaxReferences.Length > 0
                    && !lambda.Span.Contains(local.DeclaringSyntaxReferences[0].Span):
                    seen.Add(id.Identifier.Text);
                    captures.Add((local.Name, local.Type.ToDisplayString()));
                    break;
                case IParameterSymbol param when !lambdaParams.Contains(param.Name):
                    seen.Add(id.Identifier.Text);
                    captures.Add((param.Name, param.Type.ToDisplayString()));
                    break;
            }
        }
        return captures;
    }
}

/// <summary>
/// Scope-aware variable renamer. Each for/foreach introduces a new scope;
/// sibling scopes get independent unique names for identically-named variables.
/// </summary>
internal sealed class ScopedRenamer : CSharpSyntaxRewriter
{
    readonly Counter _counter;
    readonly Stack<Dictionary<string, string>> _scopes = new();

    public ScopedRenamer(Counter counter, Dictionary<string, string> seed = null)
    {
        _counter = counter;
        _scopes.Push(seed != null ? new Dictionary<string, string>(seed) : new Dictionary<string, string>());
    }

    string Resolve(string name)
    {
        foreach (var scope in _scopes)
            if (scope.TryGetValue(name, out var renamed)) return renamed;
        return null;
    }

    string GetOrRegister(string name)
    {
        var existing = Resolve(name);
        if (existing != null) return existing;
        var renamed = $"__{name}_{_counter.Next()}";
        _scopes.Peek()[name] = renamed;
        return renamed;
    }

    public override SyntaxNode VisitForStatement(ForStatementSyntax node)
    {
        _scopes.Push(new Dictionary<string, string>());
        var result = (ForStatementSyntax)base.VisitForStatement(node);
        _scopes.Pop();
        return result;
    }

    public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
    {
        _scopes.Push(new Dictionary<string, string>());
        var newName = GetOrRegister(node.Identifier.Text);
        var result = (ForEachStatementSyntax)base.VisitForEachStatement(node);
        _scopes.Pop();
        return result.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(result.Identifier));
    }

    public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        var newName = GetOrRegister(node.Identifier.Text);
        node = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node);
        return node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        var resolved = Resolve(node.Identifier.Text);
        if (resolved != null)
        {
            if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                return base.VisitIdentifierName(node);
            if (node.Parent is NameColonSyntax)
                return base.VisitIdentifierName(node);
            return node.WithIdentifier(SyntaxFactory.Identifier(resolved).WithTriviaFrom(node.Identifier));
        }
        return base.VisitIdentifierName(node);
    }
}

/// <summary>Renames local variables declared in the inlined method body to unique names.</summary>
internal sealed class LocalVariableRenamer : CSharpSyntaxRewriter
{
    readonly Dictionary<string, string> _renames;

    public LocalVariableRenamer(Dictionary<string, string> renames) => _renames = renames;

    public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        node = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node);
        if (_renames.TryGetValue(node.Identifier.Text, out var newName))
            return node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
        return node;
    }

    public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
    {
        node = (ForEachStatementSyntax)base.VisitForEachStatement(node);
        if (_renames.TryGetValue(node.Identifier.Text, out var newName))
            return node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
        return node;
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (_renames.TryGetValue(node.Identifier.Text, out var newName))
        {
            if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                return base.VisitIdentifierName(node);
            if (node.Parent is NameColonSyntax)
                return base.VisitIdentifierName(node);

            return node.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(node.Identifier));
        }
        return base.VisitIdentifierName(node);
    }
}

/// <summary>
/// Rewrites the inlined method body:
/// - Replaces the receiver parameter name with the actual receiver expression
/// - Expands delegate invocations with the lambda body
/// </summary>
internal sealed class LambdaInliner : CSharpSyntaxRewriter
{
    readonly string _receiverParamName;
    readonly string _receiverExpr;
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
        string receiverExpr,
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

            return SyntaxFactory.ParseExpression(_receiverExpr).WithTriviaFrom(node);
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
        if (TrySplitBlockReturns(renamedBlock.Statements, hoistable, out var returnExpr))
        {
            _hoistedStatements.AddRange(tempDecls);
            _hoistedStatements.AddRange(hoistable);
            return SyntaxFactory.ParenthesizedExpression(returnExpr);
        }

        // Fallback: extract to private method
        if (typeInfo.HasValue)
            return ExtractToMethod(typeInfo.Value, captures, paramNames, argExprs, block);

        // Last resort (type info unavailable)
        _diagnostics.Add(Diagnostic.Create(UdonLambda.SourceGenerator.Diagnostics.UL0001, _invocationLocation));
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
        _diagnostics.Add(Diagnostic.Create(UdonLambda.SourceGenerator.Diagnostics.UL0002, _invocationLocation, methodName));

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
    static bool ContainsReturn(StatementSyntax stmt)
        => stmt is ReturnStatementSyntax || stmt.DescendantNodes().OfType<ReturnStatementSyntax>().Any();

    /// <summary>
    /// Splits a statement list into hoistable prefix statements and a conditional return expression.
    /// </summary>
    static bool TrySplitBlockReturns(
        SyntaxList<StatementSyntax> statements, List<StatementSyntax> hoistable, out ExpressionSyntax returnExpr)
        => TrySplitBlockReturns(statements.ToArray(), hoistable, out returnExpr);

    static bool TrySplitBlockReturns(
        IReadOnlyList<StatementSyntax> statements, List<StatementSyntax> hoistable, out ExpressionSyntax returnExpr)
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
            if (stmt is IfStatementSyntax ifStmt && ContainsReturn(ifStmt))
            {
                var remaining = new List<StatementSyntax>();
                for (int j = i + 1; j < statements.Count; j++)
                    remaining.Add(statements[j]);
                return TryBuildConditional(ifStmt, remaining, hoistable, out returnExpr);
            }

            // Non-if statement containing return → cannot inline, bail out
            if (ContainsReturn(stmt))
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
        out ExpressionSyntax conditional)
    {
        conditional = null;

        // Extract then-branch expression
        if (!TryExtractBranchExpr(ifStmt.Statement, hoistable, out var thenExpr))
            return false;

        ExpressionSyntax elseExpr;
        if (ifStmt.Else != null)
        {
            // else branch exists → extract from it
            if (!TryExtractBranchExpr(ifStmt.Else.Statement, hoistable, out elseExpr))
                return false;
        }
        else
        {
            // No else → remaining statements serve as else
            if (!TrySplitBlockReturns(remaining, hoistable, out elseExpr))
                return false;
        }

        conditional = SyntaxFactory.ConditionalExpression(ifStmt.Condition, thenExpr, elseExpr);
        return true;
    }

    /// <summary>
    /// Extracts a return expression from a branch statement.
    /// Handles: return expr; | { stmts; return expr; } | if (...) ...
    /// </summary>
    static bool TryExtractBranchExpr(StatementSyntax stmt, List<StatementSyntax> hoistable, out ExpressionSyntax expr)
    {
        expr = null;

        if (stmt is ReturnStatementSyntax ret && ret.Expression != null)
        {
            expr = ret.Expression;
            return true;
        }

        if (stmt is BlockSyntax block)
            return TrySplitBlockReturns(block.Statements, hoistable, out expr);

        if (stmt is IfStatementSyntax nestedIf)
            return TryBuildConditional(nestedIf, new List<StatementSyntax>(), hoistable, out expr);

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

/// <summary>Replaces type parameter names (e.g. T, TResult) with concrete type names.</summary>
internal sealed class TypeParameterReplacer : CSharpSyntaxRewriter
{
    readonly Dictionary<string, string> _typeMap;

    public TypeParameterReplacer(Dictionary<string, string> typeMap) => _typeMap = typeMap;

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (_typeMap.TryGetValue(node.Identifier.Text, out var concreteType))
            return SyntaxFactory.IdentifierName(concreteType).WithTriviaFrom(node);
        return base.VisitIdentifierName(node);
    }
}

/// <summary>Replaces lambda parameter identifiers with the actual argument expressions.</summary>
internal sealed class ParameterReplacer : CSharpSyntaxRewriter
{
    readonly Dictionary<string, ExpressionSyntax> _replacements;

    public ParameterReplacer(List<string> paramNames, ExpressionSyntax[] argExprs)
    {
        _replacements = new Dictionary<string, ExpressionSyntax>();
        for (int i = 0; i < paramNames.Count && i < argExprs.Length; i++)
            _replacements[paramNames[i]] = argExprs[i];
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (_replacements.TryGetValue(node.Identifier.Text, out var replacement))
        {
            if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                return base.VisitIdentifierName(node);

            return replacement.WithTriviaFrom(node);
        }
        return base.VisitIdentifierName(node);
    }
}
