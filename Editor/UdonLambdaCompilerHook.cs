using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace UdonLambda.Editor
{
    /// <summary>
    /// Harmony postfix hook on <c>UdonSharpUtils.ReadFileTextSync</c>.
    /// Intercepts UdonSharp's source file reads and substitutes Source Generator-expanded code
    /// from <c>Temp/UdonLambdaGenerated/</c>. Files containing <c>[Inline]</c> method definitions
    /// are blanked to prevent UdonSharp from attempting to compile them directly.
    /// </summary>
    [InitializeOnLoad]
    internal static class UdonLambdaCompilerHook
    {
        private const string HarmonyId = "com.udonlambda.compiler-hook";
        private const string TempDir = "Temp/UdonLambdaGenerated";
        private const string GeneratedSuffix = ".udon.g.cs";

        // SG now writes directly to TempDir; no subdirectory creation needed

        private static readonly Regex GeneratedGuard =
            new(@"^\s*//\s*@source:.*\r?\n|^\s*#if\s+UDONLAMBDA_GENERATED\s*\r?\n|^\s*#endif\s*\r?\n?", RegexOptions.Multiline);

        private static readonly Regex HasInlineAttribute =
            new(@"\[\s*Inline\s*[\],\)]", RegexOptions.Compiled);

        // @source path -> expanded file path (primary lookup)
        private static Dictionary<string, string> _expandedFileMap;
        // original filename -> expanded file path (fallback for path mismatches)
        private static Dictionary<string, string> _expandedFileNameMap;
        private static bool _patched;

        // Fixed GUIDs for runtime files copied to Assets/ (VPM install).
        // Users may move these files freely — we track by GUID, not path.
        private static readonly (string fileName, string guid)[] RuntimeFiles =
        {
            ("InlineAttribute.cs", "f47ac10b58cc4372a5670e02b2c3d479"),
            ("ULinq.cs",           "8e3d5b6a9c1f4d7e2a0b8c5d6f3e4a71"),
        };

        static UdonLambdaCompilerHook()
        {
            EnsureRuntimeInAssets();
            PatchReadFileTextSync();
        }

        /// <summary>
        /// VPM installs to Packages/ where code without asmdef won't compile.
        /// Copy Runtime/*.cs to Assets/ so they join Assembly-CSharp,
        /// where the SG can read [Inline] method bodies.
        /// Files are tracked by GUID — users can move them anywhere under Assets/.
        /// </summary>
        private static void EnsureRuntimeInAssets()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(UdonLambdaCompilerHook).Assembly);
            if (info == null) return; // Running from Assets/ — no copy needed

            var srcDir = Path.Combine(info.resolvedPath, "Runtime");
            if (!Directory.Exists(srcDir)) return;

            var needsRefresh = false;
            foreach (var (fileName, guid) in RuntimeFiles)
            {
                var src = Path.Combine(srcDir, fileName);
                if (!File.Exists(src)) continue;

                var existing = AssetDatabase.GUIDToAssetPath(guid);
                string dst;
                if (!string.IsNullOrEmpty(existing) && File.Exists(existing))
                {
                    dst = existing; // user may have moved it — update in place
                }
                else
                {
                    const string defaultDir = "Assets/ULinqRuntime";
                    Directory.CreateDirectory(defaultDir);
                    dst = Path.Combine(defaultDir, fileName).Replace('\\', '/');
                    File.WriteAllText(dst + ".meta",
                        $"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n");
                }

                var srcText = File.ReadAllText(src);
                if (!File.Exists(dst) || File.ReadAllText(dst) != srcText)
                {
                    File.WriteAllText(dst, srcText);
                    needsRefresh = true;
                }
            }

            if (needsRefresh)
                EditorApplication.delayCall += AssetDatabase.Refresh;
        }

        private static void PatchReadFileTextSync()
        {
            if (_patched) return;

            var targetType = Type.GetType("UdonSharp.UdonSharpUtils, UdonSharp.Editor");
            if (targetType == null)
            {
                Debug.LogWarning("[UdonLambda] UdonSharpUtils type not found. Harmony patch skipped.");
                return;
            }

            var original = targetType.GetMethod("ReadFileTextSync", BindingFlags.Static | BindingFlags.Public);
            if (original == null)
            {
                Debug.LogWarning("[UdonLambda] ReadFileTextSync method not found. Harmony patch skipped.");
                return;
            }

            var postfix = typeof(UdonLambdaCompilerHook).GetMethod(nameof(ReadFilePostfix), BindingFlags.Static | BindingFlags.NonPublic);
            var harmony = new Harmony(HarmonyId);
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            _patched = true;
        }

        /// <remarks>
        /// Files containing <c>[Inline]</c> are blanked (set to empty string) because they contain
        /// <c>Func&lt;T,R&gt;</c> / <c>Action&lt;T&gt;</c> parameter types that UdonSharp cannot compile.
        /// The actual method bodies are inlined at call sites by the Source Generator.
        /// </remarks>
        private static void ReadFilePostfix(string filePath, ref string __result)
        {
            if (_expandedFileMap == null)
                RebuildExpandedFileMap();

            var normalizedPath = NormalizePath(filePath);
            if (!_expandedFileMap!.TryGetValue(normalizedPath, out var expandedPath))
            {
                // Absolute → relative fallback
                var idx = normalizedPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                    _expandedFileMap.TryGetValue(normalizedPath[idx..], out expandedPath);
            }
            // Filename-based fallback (handles path format mismatches between SG and UdonSharp)
            if (expandedPath == null)
                _expandedFileNameMap!.TryGetValue(Path.GetFileName(filePath), out expandedPath);
            if (expandedPath != null)
            {
                try
                {
                    var content = File.ReadAllText(expandedPath);
                    __result = GeneratedGuard.Replace(content, "");
                }
                catch (IOException)
                {
                    // Temp file not ready yet; fall through to original
                }
                return;
            }

            // Hide files containing [Inline] methods from UdonSharp
            if (__result != null && HasInlineAttribute.IsMatch(__result))
                __result = "";
        }

        private static void RebuildExpandedFileMap()
        {
            _expandedFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _expandedFileNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(TempDir)) return;

            foreach (var file in Directory.GetFiles(TempDir, "*" + GeneratedSuffix, SearchOption.AllDirectories))
            {
                try
                {
                    using var reader = new StreamReader(file);
                    var firstLine = reader.ReadLine();
                    if (firstLine == null || !firstLine.StartsWith("// @source: "))
                        continue;

                    var sourcePath = NormalizePath(firstLine["// @source: ".Length..].Trim());
                    if (string.IsNullOrEmpty(sourcePath))
                        continue;

                    _expandedFileMap[sourcePath] = file;
                    // Filename fallback (first entry wins — @source provides collision safety)
                    var fileName = Path.GetFileName(sourcePath);
                    if (!_expandedFileNameMap.ContainsKey(fileName))
                        _expandedFileNameMap[fileName] = file;
                }
                catch (IOException) { }
            }
        }

        private static string NormalizePath(string path) => path.Replace('\\', '/');

        /// <summary>
        /// Called after Unity C# compilation to refresh the map for the next UdonSharp compile.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RegisterCompilationCallback()
        {
            UnityEditor.Compilation.CompilationPipeline.compilationFinished += _ =>
            {
                _expandedFileMap = null;
                _expandedFileNameMap = null;
            };
        }
    }
}
