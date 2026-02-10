using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ULinq.SourceGenerator;

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

/// <summary>Replaces type parameter names (e.g. T, TResult) with concrete type names.</summary>
internal sealed class TypeParameterReplacer : CSharpSyntaxRewriter
{
    readonly Dictionary<string, string> _typeMap;

    public TypeParameterReplacer(Dictionary<string, string> typeMap) => _typeMap = typeMap;

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (_typeMap.TryGetValue(node.Identifier.Text, out var concreteType) && IsTypeContext(node))
            return SyntaxFactory.IdentifierName(concreteType).WithTriviaFrom(node);
        return base.VisitIdentifierName(node);
    }

    static bool IsTypeContext(IdentifierNameSyntax node) => node.Parent is
        ArrayTypeSyntax or DefaultExpressionSyntax or TypeOfExpressionSyntax or
        CastExpressionSyntax or TypeArgumentListSyntax or ObjectCreationExpressionSyntax or
        NullableTypeSyntax or QualifiedNameSyntax
        || (node.Parent is VariableDeclarationSyntax vd && vd.Type == node)
        || (node.Parent is ParameterSyntax p && p.Type == node);
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
