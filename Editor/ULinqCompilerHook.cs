using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace ULinq.Editor
{
    /// <summary>
    /// Harmony postfix hook on <c>UdonSharpUtils.ReadFileTextSync</c>.
    /// Intercepts UdonSharp's source file reads and substitutes Source Generator-expanded code
    /// from <c>Temp/ULinqGenerated/</c>. Files containing <c>[Inline]</c> method definitions
    /// are blanked to prevent UdonSharp from attempting to compile them directly.
    /// </summary>
    [InitializeOnLoad]
    internal static class ULinqCompilerHook
    {
        private const string HarmonyId = "com.ulinq.compiler-hook";
        private const string TempDir = "Temp/ULinqGenerated";
        private const string GeneratedSuffix = ".udon.g.cs";

        // SG now writes directly to TempDir; no subdirectory creation needed

        private static readonly Regex GeneratedGuard =
            new(@"^\s*//\s*@source:.*\r?\n|^\s*#if\s+ULINQ_GENERATED\s*\r?\n|^\s*#endif\s*\r?\n?", RegexOptions.Multiline);

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

        // SG DLL must also be in Assets/ — Packages/ analyzers are not applied to Assembly-CSharp
        private const string SgDllName = "ULinq.SourceGenerator.dll";
        private const string SgDllGuid = "a1b2c3d4e5f647890123456789abcdef";

        static ULinqCompilerHook()
        {
            EnsureRuntimeInAssets();
            PatchReadFileTextSync();
        }

        /// <summary>
        /// VPM installs to Packages/ where code without asmdef won't compile.
        /// Copy Runtime/*.cs and SG DLL to Assets/ so they join Assembly-CSharp.
        /// Files are tracked by GUID — users can move them anywhere under Assets/.
        /// </summary>
        private static void EnsureRuntimeInAssets()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ULinqCompilerHook).Assembly);
            if (info == null) return;

            Debug.Log($"[ULinq] Package detected: {info.name}@{info.version} at {info.resolvedPath}");

            var srcDir = Path.Combine(info.resolvedPath, "Runtime");
            if (!Directory.Exists(srcDir))
            {
                Debug.LogWarning($"[ULinq] Runtime directory not found: {srcDir}");
                return;
            }

            var copiedPaths = new List<string>();
            try
            {
                CopyRuntimeFiles(srcDir, copiedPaths);
                CopySgDll(info.resolvedPath, copiedPaths);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ULinq] EnsureRuntimeInAssets failed: {e}");
                return;
            }

            if (copiedPaths.Count > 0)
                TriggerReimport(copiedPaths);
        }

        /// <summary>
        /// Resolves the Assets/ destination for a GUID-tracked file.
        /// Returns existing path if GUID is already registered, otherwise creates defaultDir and returns the default path.
        /// </summary>
        private static string ResolveDestination(string guid, string defaultDir, string fileName)
        {
            var existing = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(existing) && File.Exists(existing))
                return existing;

            Directory.CreateDirectory(defaultDir);
            return Path.Combine(defaultDir, fileName).Replace('\\', '/');
        }

        private static void CopyRuntimeFiles(string srcDir, List<string> copiedPaths)
        {
            foreach (var (fileName, guid) in RuntimeFiles)
            {
                var src = Path.Combine(srcDir, fileName);
                if (!File.Exists(src))
                {
                    Debug.LogWarning($"[ULinq] Source file not found: {src}");
                    continue;
                }

                var dst = ResolveDestination(guid, "Assets/ULinq/Runtime", fileName);
                if (!File.Exists(dst + ".meta"))
                    File.WriteAllText(dst + ".meta",
                        $"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n");

                var srcText = File.ReadAllText(src);
                if (!File.Exists(dst) || File.ReadAllText(dst) != srcText)
                {
                    File.WriteAllText(dst, srcText);
                    copiedPaths.Add(dst);
                    Debug.Log($"[ULinq] Copied {fileName} → {dst}");
                }
            }
        }

        private static void CopySgDll(string packagePath, List<string> copiedPaths)
        {
            var sgSrc = Path.Combine(packagePath, "Plugins", SgDllName);
            if (!File.Exists(sgSrc))
            {
                Debug.LogWarning($"[ULinq] SG DLL not found: {sgSrc}");
                return;
            }

            var sgDst = ResolveDestination(SgDllGuid, "Assets/ULinq/Plugins", SgDllName);
            if (!File.Exists(sgDst + ".meta"))
            {
                var metaSrc = sgSrc + ".meta";
                if (File.Exists(metaSrc))
                {
                    var meta = File.ReadAllText(metaSrc);
                    meta = Regex.Replace(meta, @"(?<=guid: )\w+", SgDllGuid);
                    File.WriteAllText(sgDst + ".meta", meta);
                }
                else
                {
                    Debug.LogWarning($"[ULinq] SG DLL .meta not found: {metaSrc}");
                }
            }

            if (!File.Exists(sgDst) || !FilesEqual(sgSrc, sgDst))
            {
                File.Copy(sgSrc, sgDst, true);
                copiedPaths.Add(sgDst);
                Debug.Log($"[ULinq] Copied {SgDllName} → {sgDst}");
            }
        }

        private static void TriggerReimport(List<string> copiedPaths)
        {
            Debug.LogWarning("[ULinq] Runtime files copied to Assets/. Recompiling — initial errors will resolve automatically.");
            EditorApplication.delayCall += () =>
            {
                foreach (var path in copiedPaths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            };
        }

        private static void PatchReadFileTextSync()
        {
            if (_patched) return;

            var targetType = Type.GetType("UdonSharp.UdonSharpUtils, UdonSharp.Editor");
            if (targetType == null)
            {
                Debug.LogError("[ULinq] UdonSharpUtils type not found — Harmony patch skipped. Ensure VRChat SDK (com.vrchat.worlds) is installed and UdonSharp.Editor assembly is loaded.");
                return;
            }

            var original = targetType.GetMethod("ReadFileTextSync", BindingFlags.Static | BindingFlags.Public);
            if (original == null)
            {
                Debug.LogError("[ULinq] ReadFileTextSync method not found — Harmony patch skipped. UdonSharp API may have changed; check for ULinq updates.");
                return;
            }

            var postfix = typeof(ULinqCompilerHook).GetMethod(nameof(ReadFilePostfix), BindingFlags.Static | BindingFlags.NonPublic);
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

            if (TryResolveExpandedPath(filePath, out var expandedPath))
            {
                try { __result = GeneratedGuard.Replace(File.ReadAllText(expandedPath), ""); }
                catch (IOException e) { Debug.LogWarning($"[ULinq] Failed to read expanded source: {expandedPath}\n{e.Message}"); }
                return;
            }

            // Hide files containing [Inline] methods from UdonSharp
            if (__result != null && HasInlineAttribute.IsMatch(__result))
                __result = "";
        }

        /// <summary>
        /// 3-stage lookup: normalized path → Assets/-relative fallback → filename fallback.
        /// </summary>
        private static bool TryResolveExpandedPath(string filePath, out string expandedPath)
        {
            var normalizedPath = NormalizePath(filePath);
            if (_expandedFileMap!.TryGetValue(normalizedPath, out expandedPath))
                return true;

            var idx = normalizedPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx > 0 && _expandedFileMap.TryGetValue(normalizedPath[idx..], out expandedPath))
                return true;

            if (_expandedFileNameMap!.TryGetValue(Path.GetFileName(filePath), out expandedPath))
                return true;

            expandedPath = null;
            return false;
        }

        private static void RebuildExpandedFileMap()
        {
            _expandedFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _expandedFileNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(TempDir))
            {
                Debug.LogWarning($"[ULinq] {TempDir} not found. Source Generator may not be running. Check that ULinq.SourceGenerator.dll is in Assets/ with labels: [RoslynAnalyzer] in its .meta file.");
                return;
            }

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
                    _expandedFileNameMap.TryAdd(fileName, file);
                }
                catch (IOException e) { Debug.LogWarning($"[ULinq] Failed to parse expanded file: {file}\n{e.Message}"); }
            }
        }

        private static bool FilesEqual(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
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
