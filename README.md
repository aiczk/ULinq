# ULinq

**LINQ-style array operations for UdonSharp**, powered by compile-time Source Generator expansion.

UdonSharp (VRChat's C# → Udon compiler) does not support lambda expressions, delegates, or LINQ. ULinq works around this at compile time — a Roslyn Source Generator rewrites your lambda calls into plain loops *before* UdonSharp ever sees them. The result is readable code with zero runtime overhead.

```csharp
// You write this:
int[] evens = numbers.Where(x => x % 2 == 0);
int sum = evens.Aggregate(0, (acc, x) => acc + x);

// UdonSharp compiles this (generated automatically):
var __temp_0 = new int[numbers.Length];
var __count_0 = 0;
foreach (var __t_0 in numbers)
{
    if (!(__t_0 % 2 == 0)) continue;
    __temp_0[__count_0] = __t_0;
    __count_0++;
}
var evens = new int[__count_0];
for (var __i_0 = 0; __i_0 < __count_0; __i_0++)
    evens[__i_0] = __temp_0[__i_0];
var sum = 0;
foreach (var __t_1 in evens)
    sum = sum + __t_1;
```

## Why This Approach?

Udon VM — VRChat's runtime — has hard constraints that rule out conventional LINQ strategies:

| Constraint | Implication |
|---|---|
| No delegates / function pointers | `Func<T,R>`, `Action<T>` cannot exist at runtime |
| No generic struct instantiation | Struct-based enumerator chains (ZLinq) are impossible |
| No try/catch | Error-handling wrappers cannot be used |
| Arrays only | No `List<T>`, `Span<T>`, or custom collections |

**Compile-time inlining is the only viable path.** ULinq's Source Generator resolves all lambdas at build time, emitting Udon-compatible loops. The `[Inline]` attribute marks methods for expansion — this is a general-purpose mechanism, not limited to the built-in operators.

## Features

- **Compile-time expansion** — lambdas become loops before UdonSharp compilation. No runtime cost
- **Method chaining** — `array.Where(...).Select(...).FirstOrDefault(...)` composes naturally
- **Block lambdas** — multi-statement bodies with hoisting: `x => { var y = x * 2; return y + 1; }`
- **Nested lambdas** — inner lambda calls within outer lambda bodies
- **Expression contexts** — works in `return`, `if` conditions, method arguments, `while`/`for` conditions, expression-bodied members
- **Short-circuit preservation** — `a.Any(...) && b.All(...)` correctly skips `b` when `a` is false; same for `||` and ternary `?:`
- **Extensible** — define your own `[Inline]` methods with `if`/`switch` early returns; the SG expands them the same way

## Installation

### Manual

Copy the `ULinq` folder into your Unity project's `Assets/` directory.

```
Assets/
└── ULinq/
    ├── Editor/      ← Harmony hook (Editor-only)
    ├── Runtime/     ← ULinq operators + [Inline] attribute
    ├── Plugins/     ← Source Generator DLL
    └── Tests/       ← Unity EditMode tests
```

### VCC (VRChat Creator Companion)

1. Open VCC
2. Click **Settings** → **Packages** → **Add Repository**
3. Paste: `https://aiczk.github.io/VPM/index.json`
4. Add **ULinq** to your project

### Requirements

- Unity 2022.3+
- VRChat Worlds SDK 3.5.0+

> **Note:** `Runtime/` must NOT have an assembly definition. The Source Generator needs `[Inline]` method bodies and user code in the same compilation unit (Assembly-CSharp). This is a Roslyn SG limitation — it can only read syntax trees from the current compilation.

## Quick Start

1. Install ULinq (see above)
2. In Unity, right-click the Project window → **Create** → **U# Script**, name it `SumExample`
3. Replace the contents:

```csharp
using UdonSharp;
using ULinq;
using UnityEngine;

public class SumExample : UdonSharpBehaviour
{
    void Start()
    {
        int[] numbers = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        int[] evens = numbers.Where(x => x % 2 == 0);
        int sum = evens.Aggregate(0, (acc, x) => acc + x);
        Debug.Log("Sum of evens: " + sum); // 30
    }
}
```

4. Create an empty **GameObject** in your scene
5. **Add Component** → **SumExample**
6. Enter Play Mode — you should see `Sum of evens: 30` in the Console

> When installed via VCC, an `Assets/ULinq/` folder is created automatically on the first domain reload containing `Runtime/` and `Plugins/`. You may move these files anywhere under `Assets/`.

## API Reference

### Lambda Operators

```csharp
// Transform & Filter
T[] .Select<T,R>(x => ...)              // → R[]    Transform
T[] .Select<T,R>((x, i) => ...)         // → R[]    Transform with index
T[] .Where(x => ...)                    // → T[]    Filter
T[] .Where((x, i) => ...)               // → T[]    Filter with index
T[] .ForEach(x => ...)                  // → void   Side effect
T[] .SelectMany(x => ...)               // → R[]    Flat map

// Quantifiers
T[] .Any(x => ...)                      // → bool   Existential
T[] .All(x => ...)                      // → bool   Universal
T[] .Count(x => ...)                    // → int    Count matches

// Element access (predicate)
T[] .First(x => ...)                    // → T      First match (throws if none)
T[] .FirstOrDefault(x => ...)           // → T      First match (default if none)
T[] .Last(x => ...)                     // → T      Last match (throws if none)
T[] .LastOrDefault(x => ...)            // → T      Last match (default if none)
T[] .Single(x => ...)                   // → T      Single match (throws if 0 or >1)
T[] .SingleOrDefault(x => ...)          // → T      Single match (throws if >1)

// Aggregation
T[] .Aggregate((a, b) => ...)           // → T      Reduce
T[] .Aggregate(seed, (a, x) => ...)     // → R      Fold

// Conditional take/skip
T[] .TakeWhile(x => ...)               // → T[]    Take while true
T[] .SkipWhile(x => ...)               // → T[]    Skip while true

// Zip
T[] .Zip(U[], (x, y) => ...)           // → R[]    Merge two arrays

// Selector overloads (int/float)
T[] .Min(x => ...)                      // → int/float
T[] .Max(x => ...)                      // → int/float
T[] .Sum(x => ...)                      // → int/float
T[] .Average(x => ...)                  // → float

// Sorting (int/float keys, stable insertion sort)
T[] .OrderBy(x => ...)                  // → T[]    Ascending
T[] .OrderByDescending(x => ...)        // → T[]    Descending
```

### Non-Lambda Operators

```csharp
// Numeric (int[]/float[])
.Sum()  .Min()  .Max()  .Average()

// Element access
T[] .First()              T[] .Last()
T[] .FirstOrDefault()     T[] .LastOrDefault()
T[] .Single()             T[] .SingleOrDefault()
T[] .ElementAt(i)         T[] .ElementAtOrDefault(i)

// Quantifiers & query
T[] .Any()                T[] .Count()
T[] .Contains(value)      T[] .SequenceEqual(other)

// Slicing & combining
T[] .Take(n)     T[] .Skip(n)       T[] .Concat(other)
T[] .Append(v)   T[] .Prepend(v)    T[] .Reverse()
T[] .ToArray()   T[] .DefaultIfEmpty()  T[] .DefaultIfEmpty(v)

// Set operations (O(n²), .Equals() comparison)
T[] .Distinct()  T[] .Union(other)
T[] .Intersect(other)  T[] .Except(other)
```

### DataList Operators

All operators use `DataToken` (no generics). Requires `using VRC.SDK3.Data;`.

```csharp
// Lambda operators
DataList .ForEach(x => ...)                 // → void   Side effect
DataList .Select(x => ...)                  // → DataList  Transform
DataList .Where(x => ...)                   // → DataList  Filter
DataList .Any(x => ...)                     // → bool   Existential
DataList .All(x => ...)                     // → bool   Universal
DataList .Count(x => ...)                   // → int    Count matches
DataList .First(x => ...)                   // → DataToken  First match (throws if none)
DataList .FirstOrDefault(x => ...)          // → DataToken  First match (default if none)
DataList .Last(x => ...)                    // → DataToken  Last match (throws if none)
DataList .LastOrDefault(x => ...)           // → DataToken  Last match (default if none)
DataList .Single(x => ...)                  // → DataToken  Single match (throws if 0 or >1)
DataList .SingleOrDefault(x => ...)         // → DataToken  Single match (throws if >1)
DataList .Aggregate(seed, (a, x) => ...)    // → DataToken  Fold
DataList .TakeWhile(x => ...)              // → DataList  Take while true
DataList .SkipWhile(x => ...)              // → DataList  Skip while true

// Non-lambda operators
DataList .Any()               DataList .First()
DataList .FirstOrDefault()    DataList .Last()
DataList .LastOrDefault()     DataList .Single()
DataList .SingleOrDefault()   DataList .Take(n)
DataList .Skip(n)             DataList .Concat(other)
DataList .Append(token)       DataList .Prepend(token)
DataList .Distinct()          DataList .SequenceEqual(other)
```

## How It Works

```
                   Build Time                        UdonSharp Compile
┌──────────┐    ┌───────────────┐    ┌─────────┐    ┌──────────────────┐
│ Your .cs │───>│ Source        │───>│ Library/│───>│ Harmony patch    │
│ (lambda) │    │ Generator     │    │ .g.cs   │    │ intercepts read  │
└──────────┘    │ expands       │    └─────────┘    │ → returns .g.cs  │
                │ [Inline]      │                   └────────┬─────────┘
                └───────────────┘                            │
                                                    ┌────────▼─────────┐
                                                    │ UdonSharp sees   │
                                                    │ plain loops      │
                                                    │ → Udon bytecode  │
                                                    └──────────────────┘
```

1. Roslyn SG detects `[Inline]` method calls with lambda arguments
2. Rewrites each call site into expanded loops with unique variable names
3. Writes expanded `.cs` to `Library/ULinqGenerated/`
4. Harmony postfix on `UdonSharpUtils.ReadFileTextSync` returns the expanded source
5. UdonSharp compiles lambda-free code into Udon bytecode

## Custom Operators

Any static extension method with `[Inline]` is automatically expanded:

```csharp
using ULinq;

public static class MyExtensions
{
    [Inline]
    public static T[] TakeWhile<T>(this T[] array, Func<T, bool> predicate)
    {
        var temp = new T[array.Length];
        var count = 0;
        foreach (var t in array)
        {
            if (!predicate(t)) break;
            temp[count] = t;
            count++;
        }
        var result = new T[count];
        for (var i = 0; i < count; i++)
            result[i] = temp[i];
        return result;
    }
}
```

The SG inlines the method body at each call site, replacing `predicate(t)` with the actual lambda expression and resolving type parameters.

## Limitations

- **Same-assembly requirement** — `[Inline]` methods and calling code must be in Assembly-CSharp (no asmdef separation)
- **Chained operations** — each chained call allocates an intermediate array (e.g. `Where(...).Select(...)` creates a temp array between steps)

## Troubleshooting

**Q: UdonSharp errors like "does not support ... SimpleLambdaExpression"**
A: The Source Generator DLL is not being loaded. Check that `Plugins/ULinq.SourceGenerator.dll.meta` has `labels: [RoslynAnalyzer]` at the top level (not inside `PluginImporter:`). Reimport the DLL in Unity.

**Q: Changes to `[Inline]` methods are not reflected**
A: After rebuilding the SG DLL, copy it to `Plugins/` and verify the MD5 hash matches. Then change the **content** of any `.cs` file (not just timestamp) to invalidate Bee's cache.

**Q: "Unloading broken assembly" warning in Console**
A: The SG DLL's `.meta` file has `enabled: 1` under the Editor platform. Set `Editor: enabled: 0` and `Exclude Editor: 1`. The `labels: [RoslynAnalyzer]` label is sufficient — Unity passes it via `-analyzer:` automatically.

## License

[MIT](LICENSE)
