using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UdonLambda.SourceGenerator;
using Xunit;

namespace SgTest;

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
namespace UdonLambda
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class InlineAttribute : System.Attribute { }
}
";

    const string Extensions = @"
using System;
namespace ULinq
{
    using UdonLambda;
    public static class ArrayExtensions
    {
        [Inline] public static void ForEach<T>(this T[] array, Action<T> action) { foreach (var t in array) action(t); }
        [Inline] public static TResult[] Select<T, TResult>(this T[] array, Func<T, TResult> func) { var result = new TResult[array.Length]; for (var i = 0; i < array.Length; i++) result[i] = func(array[i]); return result; }
        [Inline] public static T[] Where<T>(this T[] array, Func<T, bool> predicate) { var temp = new T[array.Length]; var count = 0; foreach (var t in array) { if (!predicate(t)) continue; temp[count] = t; count++; } var result = new T[count]; for (var i = 0; i < count; i++) result[i] = temp[i]; return result; }
        [Inline] public static bool Any<T>(this T[] array, Func<T, bool> predicate) { var result = false; foreach (var t in array) { if (!predicate(t)) continue; result = true; break; } return result; }
        [Inline] public static bool All<T>(this T[] array, Func<T, bool> predicate) { var result = true; foreach (var t in array) { if (predicate(t)) continue; result = false; break; } return result; }
        [Inline] public static int Count<T>(this T[] array, Func<T, bool> predicate) { var count = 0; foreach (var t in array) { if (!predicate(t)) continue; count++; } return count; }
        [Inline] public static T First<T>(this T[] array, Func<T, bool> predicate) { var result = default(T); foreach (var t in array) { if (!predicate(t)) continue; result = t; break; } return result; }
        [Inline] public static T Last<T>(this T[] array, Func<T, bool> predicate) { var result = default(T); for (var i = array.Length - 1; i >= 0; i--) { if (!predicate(array[i])) continue; result = array[i]; break; } return result; }
        [Inline] public static T Aggregate<T>(this T[] array, Func<T, T, T> func) { var result = array[0]; for (var i = 1; i < array.Length; i++) result = func(result, array[i]); return result; }
        [Inline] public static TResult Aggregate<T, TResult>(this T[] array, TResult seed, Func<TResult, T, TResult> func) { var result = seed; for (var i = 0; i < array.Length; i++) result = func(result, array[i]); return result; }
        [Inline] public static TResult[] SelectMany<T, TResult>(this T[] array, Func<T, TResult[]> func) { var temps = new TResult[array.Length][]; var totalCount = 0; for (var i = 0; i < array.Length; i++) { temps[i] = func(array[i]); totalCount += temps[i].Length; } var result = new TResult[totalCount]; var offset = 0; for (var i = 0; i < array.Length; i++) { for (var j = 0; j < temps[i].Length; j++) result[offset + j] = temps[i][j]; offset += temps[i].Length; } return result; }
        [Inline] public static int Sum(this int[] array) { var result = 0; for (var i = 0; i < array.Length; i++) result += array[i]; return result; }
        [Inline] public static float Sum(this float[] array) { var result = 0f; for (var i = 0; i < array.Length; i++) result += array[i]; return result; }
        [Inline] public static int Min(this int[] array) { var result = array[0]; for (var i = 1; i < array.Length; i++) { if (array[i] >= result) continue; result = array[i]; } return result; }
        [Inline] public static float Min(this float[] array) { var result = array[0]; for (var i = 1; i < array.Length; i++) { if (array[i] >= result) continue; result = array[i]; } return result; }
        [Inline] public static int Max(this int[] array) { var result = array[0]; for (var i = 1; i < array.Length; i++) { if (array[i] <= result) continue; result = array[i]; } return result; }
        [Inline] public static float Max(this float[] array) { var result = array[0]; for (var i = 1; i < array.Length; i++) { if (array[i] <= result) continue; result = array[i]; } return result; }
        [Inline] public static float Average(this int[] array) { var sum = 0; for (var i = 0; i < array.Length; i++) sum += array[i]; var result = (float)sum / array.Length; return result; }
        [Inline] public static float Average(this float[] array) { var sum = 0f; for (var i = 0; i < array.Length; i++) sum += array[i]; var result = sum / array.Length; return result; }
        [Inline] public static bool Contains<T>(this T[] array, T value) { var result = false; for (var i = 0; i < array.Length; i++) { if (!array[i].Equals(value)) continue; result = true; break; } return result; }
        [Inline] public static T[] Reverse<T>(this T[] array) { var result = new T[array.Length]; for (var i = 0; i < array.Length; i++) result[i] = array[array.Length - 1 - i]; return result; }
        [Inline] public static T[] Take<T>(this T[] array, int count) { var result = new T[count]; for (var i = 0; i < count; i++) result[i] = array[i]; return result; }
        [Inline] public static T[] Skip<T>(this T[] array, int count) { var len = array.Length - count; var result = new T[len]; for (var i = 0; i < len; i++) result[i] = array[count + i]; return result; }
        [Inline] public static T[] Concat<T>(this T[] array, T[] other) { var result = new T[array.Length + other.Length]; for (var i = 0; i < array.Length; i++) result[i] = array[i]; for (var i = 0; i < other.Length; i++) result[array.Length + i] = other[i]; return result; }
        [Inline] public static T Last<T>(this T[] array) { var result = array[array.Length - 1]; return result; }
        [Inline] public static T[] Append<T>(this T[] array, T value) { var result = new T[array.Length + 1]; for (var i = 0; i < array.Length; i++) result[i] = array[i]; result[array.Length] = value; return result; }
        [Inline] public static T[] Prepend<T>(this T[] array, T value) { var result = new T[array.Length + 1]; result[0] = value; for (var i = 0; i < array.Length; i++) result[i + 1] = array[i]; return result; }
        [Inline] public static T[] Distinct<T>(this T[] array) { var temp = new T[array.Length]; var count = 0; for (var i = 0; i < array.Length; i++) { var found = false; for (var j = 0; j < count; j++) { if (!temp[j].Equals(array[i])) continue; found = true; break; } if (found) continue; temp[count] = array[i]; count++; } var result = new T[count]; for (var i = 0; i < count; i++) result[i] = temp[i]; return result; }
        [Inline] public static bool SequenceEqual<T>(this T[] array, T[] other) { var result = true; if (array.Length != other.Length) { result = false; } else { for (var i = 0; i < array.Length; i++) { if (array[i].Equals(other[i])) continue; result = false; break; } } return result; }
        [Inline] public static bool Any<T>(this T[] array) { var result = array.Length > 0; return result; }
        [Inline] public static int Count<T>(this T[] array) { var result = array.Length; return result; }
        [Inline] public static T First<T>(this T[] array) { var result = array[0]; return result; }
        [Inline] public static T FirstOrDefault<T>(this T[] array) { var result = default(T); if (array.Length > 0) result = array[0]; return result; }
    }
}
";

    static (ImmutableArray<SyntaxTree> generatedTrees, ImmutableArray<Diagnostic> diagnostics)
        RunGenerator(string userSource)
    {
        var sources = new[] { Stubs, Attribute, Extensions, userSource };
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();

        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create("TestAssembly",
            trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new UdonLambdaGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out _, out _);

        var result = driver.GetRunResult();
        return (result.GeneratedTrees, result.Diagnostics);
    }

    static string GetGeneratedSource(string userSource)
    {
        var (trees, _) = RunGenerator(userSource);
        Assert.NotEmpty(trees);
        return trees.First().GetText().ToString();
    }

    // --- Existing tests (updated to ULinq naming) ---

    [Fact]
    public void ForEach_ExpandsToForeachLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { nums.ForEach(x => Debug.Log(x)); }
}");
        Assert.Contains("foreach", source);
        Assert.Contains("Debug.Log", source);
        Assert.DoesNotContain("ForEach", source);
    }

    [Fact]
    public void Select_ExpandsToForLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var d = nums.Select(x => x * 2); }
}");
        Assert.Contains("new int[nums.Length]", source);
        Assert.Contains("* 2", source);
        Assert.DoesNotContain(".Select(", source);
    }

    [Fact]
    public void Where_ExpandsToFilterPattern()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var p = nums.Where(x => x > 0); }
}");
        Assert.Contains("__count_", source);
        Assert.Contains("__temp_", source);
        Assert.DoesNotContain(".Where(", source);
    }

    [Fact]
    public void Chain_SelectThenWhere()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var c = nums.Select(x => x * 2).Where(x => x > 0); }
}");
        // No fusion: intermediate chain variable is generated
        Assert.Contains("__chain_", source);
        Assert.Contains("__count_", source);
        Assert.Contains("* 2", source);
        Assert.Contains("> 0", source);
    }

    [Fact]
    public void Any_ExpandsToBreakPattern()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Any(x => x > 0); }
}");
        Assert.Contains("= false", source);
        Assert.Contains("= true", source);
        Assert.Contains("break", source);
    }

    [Fact]
    public void All_ExpandsToBreakPattern()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.All(x => x > 0); }
}");
        Assert.Contains("= true", source);
        Assert.Contains("= false", source);
        Assert.Contains("break", source);
    }

    [Fact]
    public void Count_ExpandsToCounterPattern()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var c = nums.Count(x => x > 0); }
}");
        Assert.Contains("__count_", source);
        Assert.Contains("++", source);
    }

    [Fact]
    public void First_ExpandsToBreakPattern()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var f = nums.First(x => x > 0); }
}");
        Assert.Contains("default(int)", source);
        Assert.Contains("break", source);
    }

    [Fact]
    public void Aggregate_ExpandsTwoParamLambda()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var s = nums.Aggregate((acc, x) => acc + x); }
}");
        Assert.Contains("nums[0]", source);
        Assert.Contains("+ nums[", source);
    }

    [Fact]
    public void Aggregate_WithSeed_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var p = nums.Aggregate(1, (acc, x) => acc * x); }
}");
        Assert.Contains("= 1", source);
        Assert.Contains("* nums[", source);
    }

    [Fact]
    public void SelectMany_ExpandsToJaggedArray()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var p = nums.SelectMany(x => new int[] { x, -x }); }
}");
        Assert.Contains("__temps_", source);
        Assert.Contains("__totalCount_", source);
        Assert.Contains("__offset_", source);
    }

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
    public void NoInlineUsage_ProducesNoOutput()
    {
        var (trees, _) = RunGenerator(@"
using UdonSharp;
public class Foo : UdonSharpBehaviour {
    void Start() { var x = 1 + 2; }
}");
        Assert.Empty(trees);
    }

    [Fact]
    public void NamespacedClass_UsesQualifiedHintName()
    {
        var (trees, _) = RunGenerator(@"
using ULinq; using UdonSharp; using UnityEngine;
namespace MyGame.Core {
    public class Player : UdonSharpBehaviour {
        public int[] nums;
        void Start() { nums.ForEach(x => Debug.Log(x)); }
    }
}");
        Assert.NotEmpty(trees);
        var hintName = trees.First().FilePath;
        Assert.Contains("MyGame_Core_Player", hintName);
    }

    [Fact]
    public void ExternalCapture_PreservesVariable()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { int th = 3; var r = nums.Where(x => x > th); }
}");
        Assert.Contains("th", source);
        Assert.Contains("> th", source);
    }

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
    public void MethodExtraction_EmitsInfoDiagnostic()
    {
        var (trees, diagnostics) = RunGenerator(@"
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
        Assert.NotEmpty(trees);
        var ul0002 = diagnostics.Where(d => d.Id == "UL0002").ToArray();
        Assert.NotEmpty(ul0002);
        Assert.Contains("__Lambda_", ul0002[0].GetMessage());
    }

    // --- High priority edge cases ---

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
    public void BlockLambda_IfReturnThenReturn_ConvertsToConditional()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Select(x => { if (x > 0) return x; return -x; }); }
}");
        Assert.Contains("?", source);
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

    [Fact]
    public void BlockLambda_BlockThenBranch_HoistsAndReturns()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Select(x => { if (x > 0) { var y = x * 2; return y; } return 0; }); }
}");
        Assert.Contains("__y_", source);
        Assert.Contains("?", source);
    }

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

    [Fact]
    public void Assignment_WithInlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    int[] result;
    void Start() { result = nums.Select(x => x * 2); }
}");
        Assert.Contains("result =", source);
        Assert.Contains("* 2", source);
        Assert.DoesNotContain(".Select(", source);
    }

    [Fact]
    public void ForEach_BlockLambda_ExpandsAsBlock()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { nums.ForEach(x => { var y = x + 1; Debug.Log(y); }); }
}");
        Assert.Contains("var y =", source);
        Assert.Contains("Debug.Log", source);
        Assert.Contains("foreach", source);
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
        Assert.Contains("__temp_", source);
    }

    [Fact]
    public void PreExpandLambdaBody_BlockLambda_LocalDeclIsInlineCall()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    void Start() { var r = a.Select(x => { var filtered = b.Where(y => y > x); return filtered; }); }
}");
        Assert.DoesNotContain(".Where(", source);
        Assert.Contains("__temp_", source);
    }

    // --- Expression context expansion ---

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
    public void Return_ChainedInlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    int[] DoIt() { return nums.Select(x => x * 2).Where(x => x > 0); }
}");
        Assert.Contains("return", source);
        // No fusion: intermediate chain variable is generated
        Assert.Contains("__chain_", source);
        Assert.DoesNotContain(".Select(", source);
        Assert.DoesNotContain(".Where(", source);
    }

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

    [Fact]
    public void ExprLambda_MemberAccessOnInlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() { var r = nums.Select(x => others.Where(y => y > x).Length); }
}");
        Assert.Contains(".Length", source);
        Assert.DoesNotContain(".Where(", source);
    }

    [Fact]
    public void BlockLambda_ReturnSubExpressionInlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() { var r = nums.Select(x => { return others.Count(y => y > x) + 1; }); }
}");
        Assert.Contains("+ 1", source);
        Assert.DoesNotContain(".Count(", source);
    }

    [Fact]
    public void BlockLambda_WhileConditionInlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() {
        var r = nums.Select(x => {
            while (others.Any(y => y > x)) { Debug.Log(""loop""); }
            return x;
        });
    }
}");
        Assert.Contains("while (true)", source);
        Assert.DoesNotContain(".Any(", source);
    }

    [Fact]
    public void BlockLambda_ForConditionInlineCall_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() {
        var r = nums.Select(x => {
            for (int j = 0; j < others.Count(y => y > x); j++) { Debug.Log(j); }
            return x;
        });
    }
}");
        Assert.Contains("if (!", source);
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

    // --- Medium priority edge cases ---

    [Fact]
    public void TripleChain_SelectWhereSelect()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Select(x => x + 1).Where(x => x > 0).Select(x => x * 2); }
}");
        // No fusion: each chain step produces a chain variable
        var chainVarDecls = System.Text.RegularExpressions.Regex.Matches(source, @"var __chain_\d+");
        Assert.True(chainVarDecls.Count <= 2,
            $"Expected <= 2 chain var declarations, got {chainVarDecls.Count}");
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
        var matches = System.Text.RegularExpressions.Regex.Matches(source, @"__i_(\d+)");
        var distinctIds = matches.Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(distinctIds.Count >= 2, $"Expected >= 2 distinct __i_ vars, got {distinctIds.Count}: {string.Join(", ", distinctIds)}");
    }

    [Fact]
    public void UsingULinq_ExcludedFromOutput()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { nums.ForEach(x => Debug.Log(x)); }
}");
        Assert.DoesNotContain("using ULinq", source);
        Assert.DoesNotContain("using UdonLambda", source);
        Assert.Contains("using UdonSharp", source);
    }

    [Fact]
    public void MultipleInlineCalls_SameMethod_AllExpanded()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour {
    public int[] a;
    public int[] b;
    void Start() { a.ForEach(x => Debug.Log(x)); b.ForEach(y => Debug.Log(y)); }
}");
        var foreachCount = source.Split("foreach").Length - 1;
        Assert.True(foreachCount >= 2, $"Expected >= 2 foreach, got {foreachCount}");
    }

    // --- New operator tests ---

    [Fact]
    public void Sum_Int_ExpandsToLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var s = nums.Sum(); }
}");
        Assert.Contains("+=", source);
        Assert.DoesNotContain(".Sum(", source);
    }

    [Fact]
    public void Min_Int_ExpandsToLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var m = nums.Min(); }
}");
        Assert.Contains("nums[0]", source);
        Assert.Contains(">=", source);
        Assert.DoesNotContain(".Min(", source);
    }

    [Fact]
    public void Max_Int_ExpandsToLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var m = nums.Max(); }
}");
        Assert.Contains("nums[0]", source);
        Assert.Contains("<=", source);
        Assert.DoesNotContain(".Max(", source);
    }

    [Fact]
    public void Average_Int_ExpandsToLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var a = nums.Average(); }
}");
        Assert.Contains("(float)", source);
        Assert.Contains("+=", source);
        Assert.DoesNotContain(".Average(", source);
    }

    [Fact]
    public void Contains_ExpandsToLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var c = nums.Contains(3); }
}");
        Assert.Contains(".Equals(", source);
        Assert.Contains("break", source);
        Assert.DoesNotContain(".Contains(", source);
    }

    [Fact]
    public void Reverse_ExpandsToLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Reverse(); }
}");
        Assert.Contains("Length - 1", source);
        Assert.DoesNotContain(".Reverse(", source);
    }

    [Fact]
    public void Take_ExpandsWithParamSubstitution()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Take(3); }
}");
        Assert.Contains("new int[3]", source);
        Assert.DoesNotContain(".Take(", source);
    }

    [Fact]
    public void Skip_ExpandsWithParamSubstitution()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Skip(2); }
}");
        Assert.Contains("nums.Length - 2", source);
        Assert.DoesNotContain(".Skip(", source);
    }

    [Fact]
    public void Concat_ExpandsToTwoLoops()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() { var r = nums.Concat(others); }
}");
        Assert.Contains("nums.Length + others.Length", source);
        Assert.DoesNotContain(".Concat(", source);
    }

    [Fact]
    public void Last_NoPredicate_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var l = nums.Last(); }
}");
        Assert.Contains("Length - 1", source);
        Assert.DoesNotContain(".Last(", source);
    }

    [Fact]
    public void Last_WithPredicate_ExpandsToLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var l = nums.Last(x => x > 0); }
}");
        Assert.Contains("default(int)", source);
        Assert.Contains("Length - 1", source);
        Assert.Contains("break", source);
        Assert.DoesNotContain(".Last(", source);
    }

    [Fact]
    public void Append_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Append(99); }
}");
        Assert.Contains("nums.Length + 1", source);
        Assert.DoesNotContain(".Append(", source);
    }

    [Fact]
    public void Prepend_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Prepend(0); }
}");
        Assert.Contains("nums.Length + 1", source);
        Assert.DoesNotContain(".Prepend(", source);
    }

    [Fact]
    public void Distinct_ExpandsToNestedLoop()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    void Start() { var r = nums.Distinct(); }
}");
        Assert.Contains(".Equals(", source);
        Assert.Contains("__found_", source);
        Assert.DoesNotContain(".Distinct(", source);
    }

    [Fact]
    public void SequenceEqual_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    public int[] others;
    void Start() { var r = nums.SequenceEqual(others); }
}");
        Assert.Contains(".Equals(", source);
        Assert.Contains("nums.Length != others.Length", source);
        Assert.DoesNotContain(".SequenceEqual(", source);
    }

    // --- Receiver double-evaluation bug fix ---

    [Fact]
    public void MethodCallReceiver_EvaluatedOnce()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    int[] GetNums() { return new int[] { 1, 2, 3 }; }
    void Start() { var r = GetNums().Select(x => x * 2); }
}");
        // GetNums() appears in method definition + once in __receiver_ assignment = 2 total
        // The key check: __receiver_ variable exists and .Select( is expanded away
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
        // Simple identifier receiver should NOT generate __receiver_ temp
        Assert.DoesNotContain("__receiver_", source);
        Assert.DoesNotContain(".Select(", source);
    }

    [Fact]
    public void IndexAccessReceiver_EvaluatedOnce()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[][] arrays;
    void Start() { var r = arrays[0].Select(x => x * 2); }
}");
        Assert.Contains("__receiver_", source);
        Assert.DoesNotContain(".Select(", source);
    }

    // --- Expression-bodied member expansion ---

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

    [Fact]
    public void ExprBodied_Method_Chain_ExpandsCorrectly()
    {
        var source = GetGeneratedSource(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour {
    public int[] nums;
    int[] FilterDoubled() => nums.Where(x => x > 0).Select(x => x * 2);
}");
        // No fusion: chain variable is generated
        Assert.Contains("__chain_", source);
        Assert.Contains("> 0", source);
        Assert.Contains("* 2", source);
    }
}
