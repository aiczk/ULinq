using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VRC.SDK3.Data;

[TestFixture]
public class ULinqDataListValueTests
{
    static readonly Type Ext;

    static ULinqDataListValueTests()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        Ext = asm?.GetType("ULinq.DataListExtensions");
    }

    [OneTimeSetUp]
    public void Setup() =>
        Assert.IsNotNull(Ext, "ULinq.DataListExtensions not found in Assembly-CSharp");

    static DataList MakeList(params int[] values)
    {
        var list = new DataList();
        foreach (var v in values) list.Add(v);
        return list;
    }

    static object Invoke(string name, params object[] args)
    {
        var types = args.Select(a => a.GetType()).ToArray();
        var method = Ext.GetMethod(name, types);
        Assert.IsNotNull(method, $"{name}({string.Join(", ", types.Select(t => t.Name))}) not found");
        return method.Invoke(null, args);
    }

    static object Invoke(string name, Type[] paramTypes, params object[] args)
    {
        var method = Ext.GetMethod(name, paramTypes);
        Assert.IsNotNull(method, $"{name} not found");
        return method.Invoke(null, args);
    }

    // === Existing 6 methods ===

    [Test]
    public void ForEach_VisitsAll()
    {
        var sum = 0;
        Action<DataToken> action = x => sum += x.Int;
        Invoke("ForEach", MakeList(1, 2, 3), action);
        Assert.AreEqual(6, sum);
    }

    [Test]
    public void Where_FiltersCorrectly()
    {
        Func<DataToken, bool> pred = x => x.Int % 2 == 0;
        var result = (DataList)Invoke("Where", MakeList(1, 2, 3, 4, 5), pred);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(2, result[0].Int);
        Assert.AreEqual(4, result[1].Int);
    }

    [Test]
    public void Any_Predicate_True() =>
        Assert.IsTrue((bool)Invoke("Any", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 2)));

    [Test]
    public void Any_Predicate_False() =>
        Assert.IsFalse((bool)Invoke("Any", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 10)));

    [Test]
    public void All_True() =>
        Assert.IsTrue((bool)Invoke("All", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 0)));

    [Test]
    public void All_False() =>
        Assert.IsFalse((bool)Invoke("All", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 2)));

    [Test]
    public void Count_Predicate() =>
        Assert.AreEqual(2, (int)Invoke("Count", MakeList(1, 2, 3, 4, 5), (Func<DataToken, bool>)(x => x.Int % 2 == 0)));

    [Test]
    public void FirstOrDefault_Predicate_Found() =>
        Assert.AreEqual(2, ((DataToken)Invoke("FirstOrDefault", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 1))).Int);

    [Test]
    public void FirstOrDefault_Predicate_NotFound() =>
        Assert.AreEqual(default(DataToken), (DataToken)Invoke("FirstOrDefault", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 10)));

    // === Lambda-based (9 new) ===

    [Test]
    public void Select_Transforms()
    {
        Func<DataToken, DataToken> func = x => new DataToken(x.Int * 2);
        var result = (DataList)Invoke("Select", MakeList(1, 2, 3), func);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(2, result[0].Int);
        Assert.AreEqual(4, result[1].Int);
        Assert.AreEqual(6, result[2].Int);
    }

    [Test]
    public void First_Predicate_Found() =>
        Assert.AreEqual(3, ((DataToken)Invoke("First", MakeList(1, 2, 3, 4), (Func<DataToken, bool>)(x => x.Int > 2))).Int);

    [Test]
    public void Last_Predicate_Found() =>
        Assert.AreEqual(2, ((DataToken)Invoke("Last", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int < 3))).Int);

    [Test]
    public void LastOrDefault_Predicate_Found() =>
        Assert.AreEqual(2, ((DataToken)Invoke("LastOrDefault", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int < 3))).Int);

    [Test]
    public void LastOrDefault_Predicate_NotFound() =>
        Assert.AreEqual(default(DataToken), (DataToken)Invoke("LastOrDefault", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 10)));

    [Test]
    public void Single_Predicate_Found() =>
        Assert.AreEqual(2, ((DataToken)Invoke("Single", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int == 2))).Int);

    [Test]
    public void SingleOrDefault_Predicate_Found() =>
        Assert.AreEqual(2, ((DataToken)Invoke("SingleOrDefault", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int == 2))).Int);

    [Test]
    public void SingleOrDefault_Predicate_NotFound() =>
        Assert.AreEqual(default(DataToken), (DataToken)Invoke("SingleOrDefault", MakeList(1, 2, 3), (Func<DataToken, bool>)(x => x.Int > 10)));

    [Test]
    public void Aggregate_Sum()
    {
        Func<DataToken, DataToken, DataToken> func = (acc, x) => new DataToken(acc.Int + x.Int);
        var result = (DataToken)Invoke("Aggregate", MakeList(1, 2, 3), new DataToken(0), func);
        Assert.AreEqual(6, result.Int);
    }

    [Test]
    public void TakeWhile_ReturnsPrefix()
    {
        Func<DataToken, bool> pred = x => x.Int < 4;
        var result = (DataList)Invoke("TakeWhile", MakeList(1, 2, 3, 4, 5), pred);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(1, result[0].Int);
        Assert.AreEqual(3, result[2].Int);
    }

    [Test]
    public void SkipWhile_SkipsPrefix()
    {
        Func<DataToken, bool> pred = x => x.Int < 4;
        var result = (DataList)Invoke("SkipWhile", MakeList(1, 2, 3, 4, 5), pred);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(4, result[0].Int);
        Assert.AreEqual(5, result[1].Int);
    }

    // === Non-lambda (14 new) ===

    [Test]
    public void Any_NoArgs_True() =>
        Assert.IsTrue((bool)Invoke("Any", MakeList(1)));

    [Test]
    public void Any_NoArgs_Empty() =>
        Assert.IsFalse((bool)Invoke("Any", new DataList()));

    [Test]
    public void First_NoArgs() =>
        Assert.AreEqual(10, ((DataToken)Invoke("First", MakeList(10, 20, 30))).Int);

    [Test]
    public void FirstOrDefault_NoArgs_NonEmpty() =>
        Assert.AreEqual(10, ((DataToken)Invoke("FirstOrDefault", MakeList(10, 20))).Int);

    [Test]
    public void FirstOrDefault_NoArgs_Empty() =>
        Assert.AreEqual(default(DataToken), (DataToken)Invoke("FirstOrDefault", new DataList()));

    [Test]
    public void Last_NoArgs() =>
        Assert.AreEqual(30, ((DataToken)Invoke("Last", MakeList(10, 20, 30))).Int);

    [Test]
    public void LastOrDefault_NoArgs_NonEmpty() =>
        Assert.AreEqual(20, ((DataToken)Invoke("LastOrDefault", MakeList(10, 20))).Int);

    [Test]
    public void LastOrDefault_NoArgs_Empty() =>
        Assert.AreEqual(default(DataToken), (DataToken)Invoke("LastOrDefault", new DataList()));

    [Test]
    public void Single_NoArgs() =>
        Assert.AreEqual(42, ((DataToken)Invoke("Single", MakeList(42))).Int);

    [Test]
    public void SingleOrDefault_NoArgs() =>
        Assert.AreEqual(42, ((DataToken)Invoke("SingleOrDefault", MakeList(42))).Int);

    [Test]
    public void SingleOrDefault_NoArgs_Empty() =>
        Assert.AreEqual(default(DataToken), (DataToken)Invoke("SingleOrDefault", new DataList()));

    [Test]
    public void Take_ReturnsPrefix()
    {
        var result = (DataList)Invoke("Take", new[] { typeof(DataList), typeof(int) }, MakeList(1, 2, 3, 4, 5), 3);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(1, result[0].Int);
        Assert.AreEqual(3, result[2].Int);
    }

    [Test]
    public void Skip_ReturnsSuffix()
    {
        var result = (DataList)Invoke("Skip", new[] { typeof(DataList), typeof(int) }, MakeList(1, 2, 3, 4, 5), 3);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(4, result[0].Int);
        Assert.AreEqual(5, result[1].Int);
    }

    [Test]
    public void Concat_MergesLists()
    {
        var result = (DataList)Invoke("Concat", MakeList(1, 2), MakeList(3, 4));
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(1, result[0].Int);
        Assert.AreEqual(4, result[3].Int);
    }

    [Test]
    public void Append_AddsToEnd()
    {
        var result = (DataList)Invoke("Append", MakeList(1, 2), new DataToken(3));
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(3, result[2].Int);
    }

    [Test]
    public void Prepend_AddsToStart()
    {
        var result = (DataList)Invoke("Prepend", MakeList(2, 3), new DataToken(1));
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(1, result[0].Int);
    }

    [Test]
    public void Distinct_RemovesDuplicates()
    {
        var result = (DataList)Invoke("Distinct", MakeList(1, 2, 2, 3, 3, 3));
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(1, result[0].Int);
        Assert.AreEqual(2, result[1].Int);
        Assert.AreEqual(3, result[2].Int);
    }

    [Test]
    public void SequenceEqual_True() =>
        Assert.IsTrue((bool)Invoke("SequenceEqual", MakeList(1, 2, 3), MakeList(1, 2, 3)));

    [Test]
    public void SequenceEqual_False_Values() =>
        Assert.IsFalse((bool)Invoke("SequenceEqual", MakeList(1, 2, 3), MakeList(1, 2, 4)));

    [Test]
    public void SequenceEqual_False_Length() =>
        Assert.IsFalse((bool)Invoke("SequenceEqual", MakeList(1, 2, 3), MakeList(1, 2)));
}
