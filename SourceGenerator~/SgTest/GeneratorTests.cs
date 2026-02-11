using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ULinq.SourceGenerator;
using Xunit;

namespace SgTest;

/// <summary>
/// Core transformation logic tests only.
/// Simple method expansion (ForEach, Select, Where, Sum, Min, etc.) is validated by
/// Unity-side tests: ULinqEditorTests (compilation) + ULinqArrayValueTests (values).
/// These 20 tests cover transformation patterns that could silently produce wrong results
/// even when compilation passes.
/// </summary>
public class GeneratorTests
{
    const string Stubs = @"
namespace UdonSharp { public class UdonSharpBehaviour : UnityEngine.MonoBehaviour { } }
namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public static class Debug { public static void Log(object message) { } }
}
";

    const string Attribute = @"
namespace ULinq
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class InlineAttribute : System.Attribute { }
}
";

    const string Extensions = @"
using System;
namespace ULinq
{
    public static class ArrayExtensions
    {
        [Inline] public static void ForEach<T>(this T[] array, Action<T> action) { foreach (var t in array) action(t); }
        [Inline] public static TResult[] Select<T, TResult>(this T[] array, Func<T, TResult> func) { var result = new TResult[array.Length]; for (var i = 0; i < array.Length; i++) result[i] = func(array[i]); return result; }
        [Inline] public static T[] Where<T>(this T[] array, Func<T, bool> predicate) { var temp = new T[array.Length]; var count = 0; foreach (var t in array) { if (!predicate(t)) continue; temp[count] = t; count++; } var result = new T[count]; for (var idx = 0; idx < count; idx++) result[idx] = temp[idx]; return result; }
        [Inline] public static bool Any<T>(this T[] array, Func<T, bool> predicate) { var result = false; foreach (var t in array) { if (!predicate(t)) continue; result = true; break; } return result; }
        [Inline] public static int Count<T>(this T[] array, Func<T, bool> predicate) { var count = 0; foreach (var t in array) { if (!predicate(t)) continue; count++; } return count; }
        [Inline] public static bool All<T>(this T[] array, Func<T, bool> predicate) { var result = true; foreach (var t in array) { if (predicate(t)) continue; result = false; break; } return result; }
    }
}
";

    const string DataListStubs = @"
using System;
namespace VRC.SDK3.Data
{
    public struct DataToken { public int Int; public string String; }
    public class DataList
    {
        private DataToken[] _items = new DataToken[0];
        public int Count => _items.Length;
        public DataToken this[int index] => _items[index];
        public void Add(DataToken t) { var n = new DataToken[_items.Length + 1]; for (int i = 0; i < _items.Length; i++) n[i] = _items[i]; n[_items.Length] = t; _items = n; }
        public void Add(int v) { Add(new DataToken { Int = v }); }
    }
}
";

    const string DataListExtensions = @"
using System;
using VRC.SDK3.Data;
namespace ULinq
{
    public static class DataListExtensions
    {
        [Inline] public static void ForEach(this DataList list, Action<DataToken> action) { for (var i = 0; i < list.Count; i++) action(list[i]); }
        [Inline] public static DataList Where(this DataList list, Func<DataToken, bool> predicate) { var result = new DataList(); for (var i = 0; i < list.Count; i++) { var t = list[i]; if (!predicate(t)) continue; result.Add(t); } return result; }
        [Inline] public static DataList Select(this DataList list, Func<DataToken, DataToken> func) { var result = new DataList(); for (var i = 0; i < list.Count; i++) result.Add(func(list[i])); return result; }
        [Inline] public static bool Any(this DataList list, Func<DataToken, bool> predicate) { var result = false; for (var i = 0; i < list.Count; i++) { var t = list[i]; if (!predicate(t)) continue; result = true; break; } return result; }
    }
}
";

    static (ImmutableArray<SyntaxTree> generatedTrees, ImmutableArray<Diagnostic> diagnostics)
        RunGenerator(string userSource, params string[] extraSources)
    {
        var sourceList = new System.Collections.Generic.List<string> { Stubs, Attribute, Extensions, userSource };
        sourceList.AddRange(extraSources);
        var sources = sourceList.ToArray();
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();

        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create("TestAssembly",
            trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ULinqGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out _);

        var result = driver.GetRunResult();

        var compileErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(compileErrors.Length == 0,
            $"Generated code has compile errors:\n{string.Join("\n", compileErrors.Select(d => d.ToString()))}");

        return (result.GeneratedTrees, result.Diagnostics);
    }

    static string GetGeneratedSource(string userSource, params string[] extraSources)
    {
        var (trees, _) = RunGenerator(userSource, extraSources);
        Assert.NotEmpty(trees);
        return trees.First().GetText().ToString();
    }

    // === Block Lambda: hoisting & conditional conversion (5) ===

    [Fact]
    public void BlockLambda_WithLoopReturn_ExtractsMethod()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() {
        var idx = nums.Select(x => {
            for (var i = 0; i < others.Length; i++) { if (others[i] == x) return i; }
            return -1;
        });
    }
}");
        Assert.Contains("__Lambda_", source);
        Assert.Contains("private int __Lambda_", source);
    }

    [Fact]
    public void BlockLambda_Simple_InlinesAsStatements()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var t = nums.Select(x => { var y = x * 2; return y + 1; }); }
}");
        Assert.Contains("var __y_", source);
        Assert.Contains("+ 1", source);
    }

    [Fact]
    public void BlockLambda_IfElseReturn_ConvertsToConditional()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Select(x => { if (x > 0) return x; else return -x; }); }
}");
        Assert.Contains("?", source);
        Assert.Contains(":", source);
        Assert.DoesNotContain("__Lambda_", source);
    }

    [Fact]
    public void BlockLambda_HoistablePrefixThenConditionalReturn()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Select(x => { var d = x * 2; if (d > 10) return d; return 0; }); }
}");
        Assert.Contains("__d_", source);
        Assert.Contains("?", source);
        Assert.DoesNotContain("__Lambda_", source);
    }

    [Fact]
    public void BlockLambda_NestedIf_BuildsNestedConditional()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Select(x => { if (x > 10) return 2; if (x > 0) return 1; return 0; }); }
}");
        var qCount = source.Split('?').Length - 1;
        Assert.True(qCount >= 2, $"Expected >= 2 ternary operators, got {qCount}");
    }

    // === Chain & scoping (2) ===

    [Fact]
    public void Chain_SelectThenWhere()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var c = nums.Select(x => x * 2).Where(x => x > 0); }
}");
        Assert.DoesNotContain("__chain_", source);
        Assert.Contains("* 2", source);
        Assert.Contains("> 0", source);
    }

    [Fact]
    public void ScopedRenamer_SiblingForLoops_UniqueNames()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var c = nums.Select(x => x * 2).Where(x => x > 0); }
}");
        var matches = System.Text.RegularExpressions.Regex.Matches(source, @"__result_(\d+)");
        var distinctIds = matches.Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(distinctIds.Count >= 2, $"Expected >= 2 distinct __result_ vars, got {distinctIds.Count}: {string.Join(", ", distinctIds)}");
    }

    // === Expression context expansion (5) ===

    [Fact]
    public void Return_InlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    int[] DoSelect() { return nums.Select(x => x * 2); }
}");
        Assert.Contains("return", source);
        Assert.Contains("* 2", source);
        Assert.DoesNotContain(".Select(", source);
    }

    [Fact]
    public void IfCondition_InlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { if (nums.Any(x => x > 0)) Debug.Log(""yes""); }
}");
        Assert.Contains("= false", source);
        Assert.Contains("break", source);
        Assert.DoesNotContain(".Any(", source);
    }

    [Fact]
    public void MethodArgument_InlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { Debug.Log(nums.Count(x => x > 0)); }
}");
        Assert.Contains("Debug.Log", source);
        Assert.Contains("++", source);
        Assert.DoesNotContain(".Count(", source);
    }

    [Fact]
    public void WhileCondition_InlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { while (nums.Any(x => x > 0)) { Debug.Log(""loop""); } }
}");
        Assert.Contains("while (true)", source);
        Assert.Contains("break", source);
        Assert.DoesNotContain(".Any(", source);
    }

    [Fact]
    public void ForCondition_InlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { for (int i = 0; i < nums.Count(x => x > 0); i++) { Debug.Log(i); } }
}");
        Assert.Contains("if (!", source);
        Assert.Contains("break", source);
        Assert.DoesNotContain(".Count(", source);
    }

    // === Nested & recursive expansion (2) ===

    [Fact]
    public void NestedLambda_ExpandsBothLevels()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    void Start() { a.ForEach(x => b.ForEach(y => Debug.Log(x))); }
}");
        var foreachCount = source.Split("foreach").Length - 1;
        Assert.True(foreachCount >= 2, $"Expected >= 2 foreach, got {foreachCount}");
    }

    [Fact]
    public void PreExpandLambdaBody_BlockLambda_ReturnIsInlineCall()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    void Start() { var r = a.Select(x => { return b.Where(y => y > x); }); }
}");
        Assert.DoesNotContain(".Where(", source);
        Assert.Contains("__idx_", source);
    }

    // === Sub-expression inline in lambda (1) ===

    [Fact]
    public void ExprLambda_SubExpressionInlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() { var r = nums.Select(x => others.Count(y => y > x) + 1); }
}");
        Assert.Contains("+ 1", source);
        Assert.DoesNotContain(".Count(", source);
    }

    // === Expression-bodied members (2) ===

    [Fact]
    public void ExprBodied_Method_InlineCall_ConvertsToBlock()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    int[] GetDoubled() => nums.Select(x => x * 2);
}");
        Assert.Contains("* 2", source);
        Assert.DoesNotContain(".Select(", source);
        Assert.DoesNotContain("=>", source);
    }

    [Fact]
    public void ExprBodied_Property_InlineCall_ConvertsToGetter()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    int PositiveCount => nums.Count(x => x > 0);
}");
        Assert.Contains("> 0", source);
        Assert.DoesNotContain(".Count(", source);
        Assert.Contains("get", source);
    }

    // === Receiver double-evaluation (2) ===

    [Fact]
    public void MethodCallReceiver_EvaluatedOnce()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    int[] GetNums() { return new int[] { 1, 2, 3 }; }
    void Start() { var r = GetNums().Select(x => x * 2); }
}");
        Assert.Contains("__receiver_", source);
        Assert.Contains("= GetNums()", source);
        Assert.DoesNotContain(".Select(", source);
    }

    [Fact]
    public void SimpleReceiver_NoTempVariable()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Select(x => x * 2); }
}");
        Assert.DoesNotContain("__receiver_", source);
        Assert.DoesNotContain(".Select(", source);
    }

    // === Early return conversion (2) ===

    [Fact]
    public void EarlyReturn_IfReturnWithSideEffect_ConvertsToResultVariable()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public static class TestHelper {
    [Inline] public static int Check(this int actual, int expected)
    { if (actual == expected) return 0; Debug.Log(""fail""); return 1; }
}
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    int _fail;
    void Start() { _fail += nums.Length.Check(5); }
}");
        Assert.Contains("__result_", source);
        Assert.DoesNotContain("return 0", source);
        Assert.DoesNotContain("return 1", source);
    }

    [Fact]
    public void EarlyReturn_MultipleIfReturns_NestsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public static class TestHelper2 {
    [Inline] public static int Classify(this int x)
    { if (x > 10) return 2; if (x > 0) return 1; return 0; }
}
public class Foo : UdonSharpBehaviour {
    void Start() { var r = 5.Classify(); }
}");
        Assert.Contains("__result_", source);
        Assert.Contains("else", source);
        Assert.DoesNotContain("return 2", source);
        Assert.DoesNotContain("return 0", source);
    }

    // === Closure capture (1) ===

    [Fact]
    public void ExtractToMethod_WithCaptures_AddsCapturesToParams()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() {
        int threshold = 5;
        var idx = nums.Select(x => {
            for (var i = 0; i < others.Length; i++) { if (others[i] == x && i > threshold) return i; }
            return -1;
        });
    }
}");
        Assert.Contains("__Lambda_", source);
        Assert.Contains("int threshold", source);
    }

    // === Switch early return (3) ===

    [Fact]
    public void Switch_AllCasesReturn_ConvertsToResultVariable()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public static class SwitchHelper {
    [Inline] public static string Describe(this int x)
    { switch (x) { case 0: return ""zero""; case 1: return ""one""; default: return ""other""; } }
}
public class Foo : UdonSharpBehaviour {
    void Start() { var r = 5.Describe(); }
}");
        Assert.Contains("__result_", source);
        Assert.DoesNotContain("return \"zero\"", source);
        Assert.DoesNotContain("return \"one\"", source);
        Assert.Contains("switch", source);
    }

    [Fact]
    public void Switch_CaseWithSideEffect_PreservesSideEffect()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public static class SwitchHelper2 {
    [Inline] public static int Process(this int x)
    { switch (x) { case 0: Debug.Log(""zero""); return 0; default: return x * 2; } }
}
public class Foo : UdonSharpBehaviour {
    void Start() { var r = 3.Process(); }
}");
        Assert.Contains("__result_", source);
        Assert.Contains("Debug.Log", source);
        Assert.Contains("switch", source);
    }

    [Fact]
    public void Switch_PartialReturnWithRemaining_ProcessesRemaining()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public static class SwitchHelper3 {
    [Inline] public static int Adjust(this int x)
    { switch (x) { case 0: return -1; default: break; } return x + 10; }
}
public class Foo : UdonSharpBehaviour {
    void Start() { var r = 3.Adjust(); }
}");
        Assert.Contains("__result_", source);
        Assert.Contains("+ 10", source);
        Assert.Contains("switch", source);
    }

    // === Short-circuit evaluation (5) ===

    [Fact]
    public void ShortCircuit_AndWithInlineCalls_GeneratesScVariable()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    void Start() { if (a.Any(x => x > 0) && b.All(x => x > 0)) Debug.Log(""yes""); }
}");
        Assert.Contains("__sc_", source);
        Assert.DoesNotContain(".Any(", source);
        Assert.DoesNotContain(".All(", source);
    }

    [Fact]
    public void ShortCircuit_OrWithInlineCalls_GeneratesScVariable()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    void Start() { if (a.Any(x => x > 0) || b.Any(x => x < 0)) Debug.Log(""yes""); }
}");
        Assert.Contains("__sc_", source);
        Assert.DoesNotContain(".Any(", source);
    }

    [Fact]
    public void ShortCircuit_ChainedAnd_RecursivelyHandled()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    public int[] c;
    void Start() { if (a.Any(x => x > 0) && b.Any(x => x > 1) && c.Any(x => x > 2)) Debug.Log(""yes""); }
}");
        var scCount = System.Text.RegularExpressions.Regex.Matches(source, @"__sc_\d+")
            .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).Distinct().Count();
        Assert.True(scCount >= 2, $"Expected >= 2 distinct __sc_ vars, got {scCount}");
    }

    [Fact]
    public void ShortCircuit_Ternary_GeneratesIfElse()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    public bool flag;
    void Start() { var r = flag ? a.Count(x => x > 0) : b.Count(x => x < 0); }
}");
        Assert.Contains("__sc_", source);
        Assert.Contains("else", source);
        Assert.DoesNotContain(".Count(", source);
    }

    [Fact]
    public void ShortCircuit_LeftOnlyInlineCall_NoScVariable()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public bool flag;
    void Start() { if (nums.Any(x => x > 0) && flag) Debug.Log(""yes""); }
}");
        Assert.DoesNotContain("__sc_", source);
        Assert.DoesNotContain(".Any(", source);
    }

    // === DataList SG expansion (3) ===

    [Fact]
    public void DataList_Where_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using VRC.SDK3.Data;
public class Foo : UdonSharpBehaviour {
    void Start() { var dl = new DataList(); dl.Add(1); var r = dl.Where(x => x.Int > 0); }
}", DataListStubs, DataListExtensions);
        Assert.Contains("new DataList()", source);
        Assert.DoesNotContain(".Where(", source);
    }

    [Fact]
    public void DataList_Select_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using VRC.SDK3.Data;
public class Foo : UdonSharpBehaviour {
    void Start() { var dl = new DataList(); dl.Add(1); var r = dl.Select(x => new DataToken { Int = x.Int * 2 }); }
}", DataListStubs, DataListExtensions);
        Assert.Contains("* 2", source);
        Assert.DoesNotContain(".Select(", source);
    }

    [Fact]
    public void DataList_Any_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine; using VRC.SDK3.Data;
public class Foo : UdonSharpBehaviour {
    void Start() { var dl = new DataList(); dl.Add(1); if (dl.Any(x => x.Int > 0)) Debug.Log(""yes""); }
}", DataListStubs, DataListExtensions);
        Assert.Contains("= false", source);
        Assert.Contains("break", source);
        Assert.DoesNotContain(".Any(", source);
    }
}
