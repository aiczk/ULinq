using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ULinq.SourceGenerator;
using Xunit;
using Xunit.Abstractions;

namespace SgTest;
public class DumpTest(ITestOutputHelper output)
{
    const string Stubs = @"
namespace UdonSharp { public class UdonSharpBehaviour : UnityEngine.MonoBehaviour { } }
namespace UnityEngine { public class Object {} public class Component : Object {} public class Behaviour : Component {} public class MonoBehaviour : Behaviour {} public static class Debug { public static void Log(object m){} } }";
    const string Attr = @"namespace ULinq { [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class InlineAttribute : System.Attribute {} }";
    const string Ext = @"using System; namespace ULinq { public static class ArrayExtensions {
[Inline] public static void ForEach<T>(this T[] array, Action<T> action) { foreach (var t in array) action(t); }
[Inline] public static TResult[] Select<T, TResult>(this T[] array, Func<T, TResult> func) { var result = new TResult[array.Length]; for (var i = 0; i < array.Length; i++) result[i] = func(array[i]); return result; }
[Inline] public static T[] Where<T>(this T[] array, Func<T, bool> predicate) { var count = 0; foreach (var t in array) { if (!predicate(t)) continue; count++; } var result = new T[count]; var idx = 0; foreach (var t in array) { if (!predicate(t)) continue; result[idx] = t; idx++; } return result; }
[Inline] public static bool Any<T>(this T[] array, Func<T, bool> predicate) { var result = false; foreach (var t in array) { if (!predicate(t)) continue; result = true; break; } return result; }
[Inline] public static int Count<T>(this T[] array, Func<T, bool> predicate) { var count = 0; foreach (var t in array) { if (!predicate(t)) continue; count++; } return count; }
}}";

    string Gen(string src)
    {
        var trees = new[] { Stubs, Attr, Ext, src }.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();
        var refs = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location) };
        var comp = CSharpCompilation.Create("T", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var drv = CSharpGeneratorDriver.Create(new ULinqGenerator());
        drv = (CSharpGeneratorDriver)drv.RunGeneratorsAndUpdateCompilation(comp, out _, out _);
        return drv.GetRunResult().GeneratedTrees.First().GetText().ToString();
    }

    [Fact] public void Dump_Select() { output.WriteLine("=== Select ==="); output.WriteLine(Gen(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour { public int[] nums; void Start() { var r = nums.Select(x => x * 2); } }")); }

    [Fact] public void Dump_Chain() { output.WriteLine("=== Chain ==="); output.WriteLine(Gen(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour { public int[] nums; void Start() { var c = nums.Select(x => x * 2).Where(x => x > 0); } }")); }

    [Fact] public void Dump_Any() { output.WriteLine("=== Any ==="); output.WriteLine(Gen(@"
using ULinq; using UdonSharp; using UnityEngine;
public class Foo : UdonSharpBehaviour { public int[] nums; void Start() { if (nums.Any(x => x > 0)) Debug.Log(""y""); } }")); }

    [Fact] public void Dump_TripleChain() { output.WriteLine("=== TripleChain ==="); output.WriteLine(Gen(@"
using ULinq; using UdonSharp;
public class Foo : UdonSharpBehaviour { public int[] nums; void Start() { var r = nums.Where(x => x > 0).Select(x => x * 2).Where(x => x < 100); } }")); }
}
