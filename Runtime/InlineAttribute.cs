namespace ULinq
{
    /// <summary>
    /// Marks a static extension method for compile-time inline expansion by the ULinq Source Generator.
    /// The method body is substituted at each call site, with lambda parameters replaced by the actual lambda expression
    /// and type parameters resolved to concrete types.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class InlineAttribute : System.Attribute { }
}
