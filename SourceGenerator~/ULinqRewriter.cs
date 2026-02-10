using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ULinq.SourceGenerator;

internal sealed class ULinqRewriter : CSharpSyntaxRewriter
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

    public ULinqRewriter(SemanticModel model, List<InlineMethodInfo> inlineMethods)
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
            // Visit receiver to expand any nested inline calls (e.g. nums.Reverse()[0])
            receiver = (ExpressionSyntax)Visit(receiver);
            if (_pendingStatements.Count > 0)
            {
                prefixStatements.AddRange(_pendingStatements);
                _pendingStatements.Clear();
            }
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

        var returnTypeName = _model.GetSymbolInfo(invocation).Symbol is IMethodSymbol resolvedForType
            ? (resolvedForType.ReturnsVoid ? "void" : resolvedForType.ReturnType.ToDisplayString())
            : "object";

        var expansion = ProcessMethodBody(
            methodBody, receiver, receiverParam.Name,
            delegateArgs, delegateTypeInfos, delegateCaptures,
            valueParamNames, valueParamExprs, typeParamMap,
            invocation.GetLocation(), returnTypeName);

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
        Location location,
        string returnTypeName)
    {
        if (typeParamMap.Count > 0)
            methodBody = (BlockSyntax)new TypeParameterReplacer(typeParamMap).Visit(methodBody);

        var renamedBody = _counter.RenameLocals(methodBody);

        if (valueParamNames.Count > 0)
            renamedBody = (BlockSyntax)new ParameterReplacer(valueParamNames, valueParamExprs.ToArray()).Visit(renamedBody);

        // Convert early returns (if/return patterns) to result-variable assignments
        if (returnTypeName != "void" && HasEarlyReturn(renamedBody.Statements))
        {
            var rv = $"__result_{_counter.Next()}";
            var converted = new List<StatementSyntax>();
            converted.Add(SyntaxFactory.ParseStatement($"{returnTypeName} {rv} = default;"));
            converted.AddRange(ConvertEarlyReturns(renamedBody.Statements, rv));
            converted.Add(SyntaxFactory.ParseStatement($"return {rv};"));
            renamedBody = SyntaxFactory.Block(converted);
        }

        var inliner = new LambdaInliner(
            receiverParamName,
            receiver,
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

    /// <summary>Returns true if any non-last top-level statement contains a return.</summary>
    static bool HasEarlyReturn(SyntaxList<StatementSyntax> statements)
    {
        for (int i = 0; i < statements.Count - 1; i++)
        {
            if (statements[i] is ReturnStatementSyntax) return true;
            if (statements[i].DescendantNodes().OfType<ReturnStatementSyntax>().Any()) return true;
        }
        return false;
    }

    /// <summary>
    /// Converts early returns into result-variable assignments with if/else nesting.
    /// <c>if (c) return A; stmts; return B;</c> → <c>if (c) { rv = A; } else { stmts; rv = B; }</c>
    /// </summary>
    static List<StatementSyntax> ConvertEarlyReturns(SyntaxList<StatementSyntax> stmts, string rv)
        => ConvertEarlyReturns(stmts.ToArray(), rv);

    static List<StatementSyntax> ConvertEarlyReturns(IReadOnlyList<StatementSyntax> stmts, string rv)
    {
        var result = new List<StatementSyntax>();
        for (int i = 0; i < stmts.Count; i++)
        {
            if (stmts[i] is ReturnStatementSyntax ret && ret.Expression != null)
            {
                result.Add(AssignResult(rv, ret.Expression));
                break;
            }

            if (stmts[i] is IfStatementSyntax ifStmt
                && ifStmt.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
            {
                var remaining = new List<StatementSyntax>();
                for (int j = i + 1; j < stmts.Count; j++) remaining.Add(stmts[j]);

                var thenBranch = ConvertBranch(ifStmt.Statement, rv);
                StatementSyntax elseBranch;
                if (ifStmt.Else != null)
                {
                    elseBranch = ConvertBranch(ifStmt.Else.Statement, rv);
                    if (remaining.Count > 0
                        && !ifStmt.Else.Statement.DescendantNodesAndSelf()
                            .OfType<ReturnStatementSyntax>().Any())
                    {
                        var combined = elseBranch is BlockSyntax eb
                            ? eb.Statements.ToList()
                            : new List<StatementSyntax> { elseBranch };
                        combined.AddRange(ConvertEarlyReturns(remaining, rv));
                        elseBranch = SyntaxFactory.Block(combined);
                    }
                }
                else
                {
                    elseBranch = SyntaxFactory.Block(ConvertEarlyReturns(remaining, rv));
                }

                result.Add(ifStmt.WithStatement(thenBranch)
                    .WithElse(SyntaxFactory.ElseClause(elseBranch)));
                break;
            }

            result.Add(stmts[i]);
        }
        return result;
    }

    static StatementSyntax ConvertBranch(StatementSyntax stmt, string rv)
    {
        if (stmt is ReturnStatementSyntax ret && ret.Expression != null)
            return SyntaxFactory.Block(AssignResult(rv, ret.Expression));
        if (stmt is BlockSyntax block)
            return SyntaxFactory.Block(ConvertEarlyReturns(block.Statements, rv));
        if (stmt is IfStatementSyntax ifStmt)
            return SyntaxFactory.Block(ConvertEarlyReturns(new StatementSyntax[] { ifStmt }, rv));
        return stmt;
    }

    static ExpressionStatementSyntax AssignResult(string rv, ExpressionSyntax value)
        => SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(rv), value));

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
