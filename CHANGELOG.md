# Changelog

## [1.0.5] - 2026-02-14

### Added
- Short-circuit preservation: `a.Any(...) && b.All(...)` now correctly skips the right operand when the left determines the result. Applies to `&&`, `||`, and ternary `?:` in statement contexts
- `ConvertEarlyReturns` switch support: `[Inline]` methods can now use `switch` with `return` in case branches

### Fixed
- Static calls to `[Inline]` extension methods (`Class.Method(arg)`) now correctly use the first argument as receiver instead of the class name
- Expanded expressions from `[Inline]` methods are now parenthesized when substituted into operator contexts (e.g. `!x.IsTerminal()` → `!(x < 27 && ...)` instead of `!x < 27 && ...`)
- `HasEarlyReturn` failed to detect `return` statements inside a `switch` when it was the only/last statement in the method body
- Stale generated files in `Library/ULinqGenerated/` were never cleaned up — if a class stopped using `[Inline]` methods, the old expanded code was still served to UdonSharp. Now cleaned on `compilationStarted`
- Unbraced loop bodies with `[Inline]` calls (e.g. `for (...) if (a && arr.Any(...)) c++;`) caused CS0103 — `__sc_N` pending statements leaked outside loop scope. Fixed with post-visit drain in `VisitForStatement`/`VisitWhileStatement`/`VisitForEachStatement`

## [1.0.4] - 2026-02-11

### Changed
- `Where` / `Where` (indexed): single-pass temp array algorithm — predicate is now evaluated once per element (was twice in v1.0.3)
- `SequenceEqual`, `FirstOrDefault()`, `LastOrDefault()`, `DefaultIfEmpty`: rewritten with early return guard clauses
- SG: eliminated redundant `__chain_N` intermediate variables in method chaining
- SG: reduced `__receiver_N` temp variables — simple receivers (identifiers, member access, literals, indexers) no longer wrapped

### Fixed
- SG output directory changed from `Temp/` to `Library/ULinqGenerated/` — fixes compilation errors on project reopen (Temp/ is cleared when Unity closes)
- SG DLL `.meta`: added `labels: [RoslynAnalyzer]`, set `Editor: enabled: 0` — fixes `ReflectionTypeLoadException` from VRChat SDK scanning the SG assembly at runtime
- `Harmony.Patch()` now wrapped in try-catch with diagnostic log
- `ReadFilePostfix` IOException catch now sets `__result = ""` (prevents NullReferenceException)
- `ContainsReturn()` memoized with `Dictionary<SyntaxNode, bool>` cache (eliminates O(n²) tree traversal)
- Thread safety: `RebuildExpandedFileMap` now builds into local variables before publishing — fixes `IndexOutOfRangeException` when UdonSharp's `Parallel.ForEach` invokes `ReadFilePostfix` concurrently

## [1.0.3] - 2026-02-11

### Changed
- Renamed all internal identifiers from `UdonLambda` to `ULinq` (namespace, DLL, asmdef, Harmony ID, temp directory)
- `InlineAttribute` namespace: `UdonLambda` → `ULinq`
- `Where` / `Where` (indexed): two-pass algorithm eliminates over-allocated temp array
- `TakeWhile` / `SkipWhile`: direct array access eliminates temp array
- DataList method variables: `__i` / `__j` → `i` / `j` for readability

### Added
- Early return conversion: `[Inline]` methods can now use `if (cond) return A; return B;` patterns
- `TestAssert.Eq<T>`: generic `[Inline]` test assertion helper
- `ULinqArrayValueTests`: reflection-based runtime value tests for all operators
- SG diagnostics: UL0003 (disk write failure), UL0004 (class processing failure)
- Warning when `Temp/ULinqGenerated/` is missing (helps diagnose SG DLL not loaded)

### Fixed
- SG `WriteToDisk` now derives absolute path from `SyntaxTree.FilePath` (fixes potential CWD mismatch)
- Replaced all silent `catch {}` with diagnostic reporting or `Debug.LogWarning`
- `LambdaInliner`: receiver parameter changed from `string` to `ExpressionSyntax` (avoids redundant parsing)

## [1.0.0] - 2026-02-09

### Lambda Operators (Array)
- `Select` / `Select` (indexed) — transform each element
- `Where` / `Where` (indexed) — filter elements by predicate
- `ForEach` — execute side effect per element
- `Any` / `All` — existential / universal quantifier
- `Count` — count elements matching predicate
- `First` / `FirstOrDefault` — find first match
- `Last` / `LastOrDefault` — find last match
- `Single` / `SingleOrDefault` — find single match
- `Aggregate` — reduce (no seed) and fold (with seed)
- `SelectMany` — flat map
- `TakeWhile` / `SkipWhile` — conditional take/skip
- `Zip` — merge two arrays element-wise
- `Min` / `Max` / `Sum` / `Average` with selector (int/float)
- `OrderBy` / `OrderByDescending` — stable insertion sort (int/float keys)

### Numeric Specializations
- `Sum`, `Min`, `Max`, `Average` for `int[]` and `float[]`

### DataList Operators
- **Lambda operators**: `Select`, `First`, `Last`, `LastOrDefault`, `Single`, `SingleOrDefault`, `Aggregate`, `TakeWhile`, `SkipWhile`
- **Non-lambda operators**: `Any()`, `First()`, `FirstOrDefault()`, `Last()`, `LastOrDefault()`, `Single()`, `SingleOrDefault()`, `Take`, `Skip`, `Concat`, `Append`, `Prepend`, `Distinct`, `SequenceEqual`
- Total: 29 methods (6 existing + 23 added)

### Generic Operators (Array)
- `Contains`, `Reverse`, `Take`, `Skip`, `Concat`, `Append`, `Prepend`, `Distinct`, `SequenceEqual`, `Last`
- `ElementAt` / `ElementAtOrDefault` — index-based access
- `DefaultIfEmpty` — empty array fallback
- `Union` / `Intersect` / `Except` — set operations

### Source Generator Features
- Compile-time lambda expansion via Roslyn Source Generator
- Harmony-based UdonSharp compiler integration
- Method chaining support
- Block lambda support with hoisting
- Nested lambda support
- Expression context expansion (return, if, method arguments, while/for conditions)
- Expression-bodied member support (methods and properties with `=>` syntax)
- `[Inline]` extensibility — define custom operators

### Testing
- Source Generator unit tests (xunit + CSharpGeneratorDriver)
- Unity EditMode tests — per-script UdonSharp compilation verification

### Known Limitations
- `[Inline]` methods and calling code must be in Assembly-CSharp (no asmdef separation)
- Short-circuit operators (`&&`, `||`, ternary) in expanded code evaluate both sides (fixed in v1.0.5 for statement contexts)
- Chained operations allocate intermediate arrays between steps
- `Take`/`Skip` throw on out-of-range counts (unlike LINQ which clamps)
- `Aggregate` (no seed) throws `IndexOutOfRangeException` on empty arrays
- `Min`/`Max` throw `IndexOutOfRangeException` on empty arrays
- `Average` throws `DivideByZeroException` on empty arrays
