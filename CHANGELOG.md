# Changelog

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
- Short-circuit operators (`&&`, `||`, ternary) in expanded code evaluate both sides
- Chained operations allocate intermediate arrays between steps
- `Take`/`Skip` throw on out-of-range counts (unlike LINQ which clamps)
- `Aggregate` (no seed) throws `IndexOutOfRangeException` on empty arrays
- `Min`/`Max` throw `IndexOutOfRangeException` on empty arrays
- `Average` throws `DivideByZeroException` on empty arrays
