# Contributing to ULinq

## Development Environment

- **Unity** 2022.3+ (tested on 2022.3.22f1)
- **VRChat Worlds SDK** 3.5.0+ (via VCC or manual import)
- **.NET SDK** 6.0+ (for building/testing the Source Generator)

## Project Structure

```
Assets/ULinq/
├── Editor/            Harmony hook (Editor-only)
├── Runtime/           ULinq operators + [Inline] attribute
├── Plugins/           Source Generator DLL
├── Tests/            Unity EditMode tests
└── SourceGenerator~/  SG source (excluded from Unity by ~ suffix)
    └── SgTest/        xunit tests
```

## Building the Source Generator

```bash
cd Assets/ULinq/SourceGenerator~
dotnet build UdonLambda.SourceGenerator.csproj -c Release
```

Copy the built DLL to `Plugins/`:

```bash
cp bin/Release/netstandard2.0/UdonLambda.SourceGenerator.dll ../Plugins/
```

**Verify the hash matches** — Bee silently ignores DLLs that don't match:

```bash
md5sum ../Plugins/UdonLambda.SourceGenerator.dll
md5sum bin/Release/netstandard2.0/UdonLambda.SourceGenerator.dll
```

After copying, touch a `.cs` file's **content** (not just timestamp) to invalidate Bee's cache.

## Running Tests

### Source Generator (xunit)

```bash
cd Assets/ULinq/SourceGenerator~
dotnet test SgTest
```

### Unity EditMode

1. Window → General → Test Runner
2. Run `ULinqEditorTests` under the EditMode tab
3. Each test verifies per-script UdonSharp compilation success

## Adding a New Operator

1. **Define the method** in `Runtime/ULinq.cs` with `[Inline]`:
   - The method must be `public static` with `this T[] array` as the first parameter
   - Use only Udon-compatible constructs (no LINQ, no delegates at runtime, no try/catch)
   - Lambda parameters use `Func<>` / `Action<>` types — the SG replaces them at compile time

2. **Add the method stub** to the `Extensions` constant in `SgTest/GeneratorTests.cs`
   - Variable names must exactly match `ULinq.cs` (the SG renames them to `__varname_N`)

3. **Write tests** — at minimum:
   - SG xunit: pattern assertion + `AssertGeneratedCodeCompiles`
   - Unity EditMode: add calls to the new method in `Tests/ULinqTestBasic.cs` etc.

4. **Update documentation** — `README.md` API Reference and `CHANGELOG.md`

5. **Build and copy the DLL** — see above

## Commit Messages

Use imperative mood, present tense:
- `Add TakeWhile operator`
- `Fix ScopedRenamer duplicate variable names`
- `Update README API reference`
