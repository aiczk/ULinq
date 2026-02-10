using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

[TestFixture]
public class ULinqArrayValueTests
{
    static readonly Type Ext;

    static ULinqArrayValueTests()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        Ext = asm?.GetType("ULinq.ArrayExtensions");
    }

    [OneTimeSetUp]
    public void Setup() =>
        Assert.IsNotNull(Ext, "ULinq.ArrayExtensions not found in Assembly-CSharp");

    static object Call(string name, Type[] typeArgs, params object[] args)
    {
        foreach (var m in Ext.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == name && m.GetGenericArguments().Length == typeArgs.Length))
        {
            var concrete = typeArgs.Length > 0 ? m.MakeGenericMethod(typeArgs) : m;
            var ps = concrete.GetParameters();
            if (ps.Length != args.Length) continue;
            if (ps.Select((p, i) => p.ParameterType.IsAssignableFrom(args[i].GetType())).All(x => x))
                return concrete.Invoke(null, args);
        }
        Assert.Fail($"{name}<{string.Join(",", typeArgs.Select(t => t.Name))}> not found for ({string.Join(", ", args.Select(a => a.GetType().Name))})");
        return null;
    }

    static object G<T>(string name, params object[] args) => Call(name, new[] { typeof(T) }, args);
    static object G<T1, T2>(string name, params object[] args) => Call(name, new[] { typeof(T1), typeof(T2) }, args);
    static object G<T1, T2, T3>(string name, params object[] args) => Call(name, new[] { typeof(T1), typeof(T2), typeof(T3) }, args);
    static object N(string name, params object[] args) => Call(name, Type.EmptyTypes, args);

    // === Lambda basics ===

    [Test]
    public void ForEach_VisitsAll()
    {
        var sum = 0;
        G<int>("ForEach", new[] { 1, 2, 3 }, (Action<int>)(x => sum += x));
        Assert.AreEqual(6, sum);
    }

    [Test]
    public void Select_Transforms() =>
        CollectionAssert.AreEqual(new[] { 2, 4, 6 }, (int[])G<int, int>("Select", new[] { 1, 2, 3 }, (Func<int, int>)(x => x * 2)));

    [Test]
    public void Where_Filters() =>
        CollectionAssert.AreEqual(new[] { 2, 4 }, (int[])G<int>("Where", new[] { 1, 2, 3, 4, 5 }, (Func<int, bool>)(x => x % 2 == 0)));

    [Test]
    public void Where_Empty() =>
        CollectionAssert.AreEqual(new int[0], (int[])G<int>("Where", new int[0], (Func<int, bool>)(x => x > 0)));

    [Test]
    public void Any_Predicate_True() =>
        Assert.IsTrue((bool)G<int>("Any", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 2)));

    [Test]
    public void Any_Predicate_False() =>
        Assert.IsFalse((bool)G<int>("Any", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 10)));

    [Test]
    public void All_True() =>
        Assert.IsTrue((bool)G<int>("All", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 0)));

    [Test]
    public void All_False() =>
        Assert.IsFalse((bool)G<int>("All", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 2)));

    [Test]
    public void Count_Predicate() =>
        Assert.AreEqual(2, (int)G<int>("Count", new[] { 1, 2, 3, 4, 5 }, (Func<int, bool>)(x => x % 2 == 0)));

    [Test]
    public void FirstOrDefault_Predicate_Found() =>
        Assert.AreEqual(2, (int)G<int>("FirstOrDefault", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 1)));

    [Test]
    public void FirstOrDefault_Predicate_NotFound() =>
        Assert.AreEqual(0, (int)G<int>("FirstOrDefault", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 10)));

    [Test]
    public void LastOrDefault_Predicate_Found() =>
        Assert.AreEqual(2, (int)G<int>("LastOrDefault", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x < 3)));

    [Test]
    public void LastOrDefault_Predicate_NotFound() =>
        Assert.AreEqual(0, (int)G<int>("LastOrDefault", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 10)));

    [Test]
    public void Aggregate_NoSeed() =>
        Assert.AreEqual(6, (int)G<int>("Aggregate", new[] { 1, 2, 3 }, (Func<int, int, int>)((a, b) => a + b)));

    [Test]
    public void Aggregate_WithSeed() =>
        Assert.AreEqual(16, (int)G<int, int>("Aggregate", new[] { 1, 2, 3 }, 10, (Func<int, int, int>)((a, b) => a + b)));

    [Test]
    public void SelectMany_Flattens() =>
        CollectionAssert.AreEqual(new[] { 1, 10, 2, 20, 3, 30 },
            (int[])G<int, int>("SelectMany", new[] { 1, 2, 3 }, (Func<int, int[]>)(x => new[] { x, x * 10 })));

    // === No-predicate overloads ===

    [Test]
    public void Any_NoArgs_True() =>
        Assert.IsTrue((bool)G<int>("Any", new[] { 1 }));

    [Test]
    public void Any_NoArgs_Empty() =>
        Assert.IsFalse((bool)G<int>("Any", new int[0]));

    [Test]
    public void Count_NoArgs() =>
        Assert.AreEqual(3, (int)G<int>("Count", new[] { 1, 2, 3 }));

    [Test]
    public void First_NoArgs() =>
        Assert.AreEqual(10, (int)G<int>("First", new[] { 10, 20, 30 }));

    [Test]
    public void First_Predicate() =>
        Assert.AreEqual(3, (int)G<int>("First", new[] { 1, 2, 3, 4 }, (Func<int, bool>)(x => x > 2)));

    [Test]
    public void FirstOrDefault_NoArgs_NonEmpty() =>
        Assert.AreEqual(10, (int)G<int>("FirstOrDefault", new[] { 10, 20 }));

    [Test]
    public void FirstOrDefault_NoArgs_Empty() =>
        Assert.AreEqual(0, (int)G<int>("FirstOrDefault", new int[0]));

    [Test]
    public void Last_NoArgs() =>
        Assert.AreEqual(30, (int)G<int>("Last", new[] { 10, 20, 30 }));

    [Test]
    public void Last_Predicate() =>
        Assert.AreEqual(2, (int)G<int>("Last", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x < 3)));

    [Test]
    public void LastOrDefault_NoArgs_NonEmpty() =>
        Assert.AreEqual(20, (int)G<int>("LastOrDefault", new[] { 10, 20 }));

    [Test]
    public void LastOrDefault_NoArgs_Empty() =>
        Assert.AreEqual(0, (int)G<int>("LastOrDefault", new int[0]));

    // === Single series ===

    [Test]
    public void Single_NoArgs() =>
        Assert.AreEqual(42, (int)G<int>("Single", new[] { 42 }));

    [Test]
    public void Single_Predicate() =>
        Assert.AreEqual(2, (int)G<int>("Single", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x == 2)));

    [Test]
    public void SingleOrDefault_NoArgs() =>
        Assert.AreEqual(42, (int)G<int>("SingleOrDefault", new[] { 42 }));

    [Test]
    public void SingleOrDefault_NoArgs_Empty() =>
        Assert.AreEqual(0, (int)G<int>("SingleOrDefault", new int[0]));

    [Test]
    public void SingleOrDefault_Predicate() =>
        Assert.AreEqual(2, (int)G<int>("SingleOrDefault", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x == 2)));

    [Test]
    public void SingleOrDefault_Predicate_NotFound() =>
        Assert.AreEqual(0, (int)G<int>("SingleOrDefault", new[] { 1, 2, 3 }, (Func<int, bool>)(x => x > 10)));

    // === Numeric (non-generic) ===

    [Test] public void Sum_Int() => Assert.AreEqual(6, (int)N("Sum", new[] { 1, 2, 3 }));
    [Test] public void Sum_Float() => Assert.AreEqual(6f, (float)N("Sum", new[] { 1f, 2f, 3f }));
    [Test] public void Min_Int() => Assert.AreEqual(1, (int)N("Min", new[] { 3, 1, 2 }));
    [Test] public void Min_Float() => Assert.AreEqual(1f, (float)N("Min", new[] { 3f, 1f, 2f }));
    [Test] public void Max_Int() => Assert.AreEqual(3, (int)N("Max", new[] { 1, 3, 2 }));
    [Test] public void Max_Float() => Assert.AreEqual(3f, (float)N("Max", new[] { 1f, 3f, 2f }));
    [Test] public void Average_Int() => Assert.AreEqual(2f, (float)N("Average", new[] { 1, 2, 3 }));
    [Test] public void Average_Float() => Assert.AreEqual(2f, (float)N("Average", new[] { 1f, 2f, 3f }));

    // === Selector overloads ===

    [Test]
    public void Sum_Selector() =>
        Assert.AreEqual(6, (int)G<string>("Sum", new[] { "a", "bb", "ccc" }, (Func<string, int>)(s => s.Length)));

    [Test]
    public void Min_Selector() =>
        Assert.AreEqual(1, (int)G<string>("Min", new[] { "ccc", "a", "bb" }, (Func<string, int>)(s => s.Length)));

    [Test]
    public void Max_Selector() =>
        Assert.AreEqual(3, (int)G<string>("Max", new[] { "a", "ccc", "bb" }, (Func<string, int>)(s => s.Length)));

    [Test]
    public void Average_Selector() =>
        Assert.AreEqual(2f, (float)G<string>("Average", new[] { "a", "bb", "ccc" }, (Func<string, int>)(s => s.Length)));

    // === Collection operations ===

    [Test] public void Contains_True() => Assert.IsTrue((bool)G<int>("Contains", new[] { 1, 2, 3 }, 2));
    [Test] public void Contains_False() => Assert.IsFalse((bool)G<int>("Contains", new[] { 1, 2, 3 }, 5));

    [Test]
    public void Reverse_Works() =>
        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, (int[])G<int>("Reverse", new[] { 1, 2, 3 }));

    [Test]
    public void Take_ReturnsPrefix() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])G<int>("Take", new[] { 1, 2, 3, 4, 5 }, 3));

    [Test]
    public void Skip_ReturnsSuffix() =>
        CollectionAssert.AreEqual(new[] { 4, 5 }, (int[])G<int>("Skip", new[] { 1, 2, 3, 4, 5 }, 3));

    [Test]
    public void Concat_MergesArrays() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, (int[])G<int>("Concat", new[] { 1, 2 }, new[] { 3, 4 }));

    [Test]
    public void Append_AddsToEnd() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])G<int>("Append", new[] { 1, 2 }, 3));

    [Test]
    public void Prepend_AddsToStart() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])G<int>("Prepend", new[] { 2, 3 }, 1));

    [Test]
    public void Distinct_RemovesDuplicates() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])G<int>("Distinct", new[] { 1, 2, 2, 3, 3, 3 }));

    [Test]
    public void SequenceEqual_True() =>
        Assert.IsTrue((bool)G<int>("SequenceEqual", new[] { 1, 2, 3 }, new[] { 1, 2, 3 }));

    [Test]
    public void SequenceEqual_False() =>
        Assert.IsFalse((bool)G<int>("SequenceEqual", new[] { 1, 2, 3 }, new[] { 1, 2, 4 }));

    [Test]
    public void ElementAt_Valid() =>
        Assert.AreEqual(20, (int)G<int>("ElementAt", new[] { 10, 20, 30 }, 1));

    [Test]
    public void ElementAtOrDefault_InRange() =>
        Assert.AreEqual(20, (int)G<int>("ElementAtOrDefault", new[] { 10, 20, 30 }, 1));

    [Test]
    public void ElementAtOrDefault_OutOfRange() =>
        Assert.AreEqual(0, (int)G<int>("ElementAtOrDefault", new[] { 10, 20, 30 }, 10));

    [Test]
    public void DefaultIfEmpty_NonEmpty() =>
        CollectionAssert.AreEqual(new[] { 1, 2 }, (int[])G<int>("DefaultIfEmpty", new[] { 1, 2 }));

    [Test]
    public void DefaultIfEmpty_Empty() =>
        CollectionAssert.AreEqual(new[] { 0 }, (int[])G<int>("DefaultIfEmpty", new int[0]));

    [Test]
    public void ToArray_Copies()
    {
        var src = new[] { 1, 2, 3 };
        var copy = (int[])G<int>("ToArray", src);
        CollectionAssert.AreEqual(src, copy);
        Assert.AreNotSame(src, copy);
    }

    [Test]
    public void TakeWhile_ReturnsPrefix() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])G<int>("TakeWhile", new[] { 1, 2, 3, 4, 5 }, (Func<int, bool>)(x => x < 4)));

    [Test]
    public void SkipWhile_SkipsPrefix() =>
        CollectionAssert.AreEqual(new[] { 4, 5 }, (int[])G<int>("SkipWhile", new[] { 1, 2, 3, 4, 5 }, (Func<int, bool>)(x => x < 4)));

    // === Set operations ===

    [Test]
    public void Union_Works() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, (int[])G<int>("Union", new[] { 1, 2, 3 }, new[] { 2, 3, 4 }));

    [Test]
    public void Intersect_Works() =>
        CollectionAssert.AreEqual(new[] { 2, 3 }, (int[])G<int>("Intersect", new[] { 1, 2, 3 }, new[] { 2, 3, 4 }));

    [Test]
    public void Except_Works() =>
        CollectionAssert.AreEqual(new[] { 1 }, (int[])G<int>("Except", new[] { 1, 2, 3 }, new[] { 2, 3, 4 }));

    // === Sort ===

    [Test]
    public void OrderBy_SortsAscending() =>
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])G<int>("OrderBy", new[] { 3, 1, 2 }, (Func<int, int>)(x => x)));

    [Test]
    public void OrderByDescending_SortsDescending() =>
        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, (int[])G<int>("OrderByDescending", new[] { 1, 3, 2 }, (Func<int, int>)(x => x)));

    // === Indexed / Zip ===

    [Test]
    public void Select_Indexed() =>
        CollectionAssert.AreEqual(new[] { "a0", "b1", "c2" },
            (string[])G<string, string>("Select", new[] { "a", "b", "c" }, (Func<string, int, string>)((s, i) => s + i)));

    [Test]
    public void Where_Indexed() =>
        CollectionAssert.AreEqual(new[] { 10, 30, 50 },
            (int[])G<int>("Where", new[] { 10, 20, 30, 40, 50 }, (Func<int, int, bool>)((x, i) => i % 2 == 0)));

    [Test]
    public void Zip_Combines() =>
        CollectionAssert.AreEqual(new[] { 5, 7, 9 },
            (int[])G<int, int, int>("Zip", new[] { 1, 2, 3 }, new[] { 4, 5, 6 }, (Func<int, int, int>)((a, b) => a + b)));

    // === Chains ===

    [Test]
    public void Chain_Where_Select()
    {
        var filtered = (int[])G<int>("Where", new[] { 1, 2, 3, 4, 5 }, (Func<int, bool>)(x => x % 2 == 0));
        var result = (int[])G<int, int>("Select", filtered, (Func<int, int>)(x => x * 10));
        CollectionAssert.AreEqual(new[] { 20, 40 }, result);
    }

    [Test]
    public void Chain_Select_Sum()
    {
        var mapped = (int[])G<string, int>("Select", new[] { "a", "bb", "ccc" }, (Func<string, int>)(s => s.Length));
        Assert.AreEqual(6, (int)N("Sum", mapped));
    }
}
