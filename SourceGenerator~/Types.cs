using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ULinq.SourceGenerator;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor UL0001 = new(
        "UL0001", "Unresolved block lambda",
        "Block lambda could not be resolved; output may be incorrect",
        "ULinq", DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UL0002 = new(
        "UL0002", "Lambda extracted to method",
        "Block lambda extracted to private method '{0}'",
        "ULinq", DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor UL0003 = new(
        "UL0003", "Disk write failed",
        "Failed to write expanded source to Library/ULinqGenerated/: {0}",
        "ULinq", DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UL0004 = new(
        "UL0004", "Source generation failed",
        "Failed to process class '{0}': {1}",
        "ULinq", DiagnosticSeverity.Warning, true);
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
