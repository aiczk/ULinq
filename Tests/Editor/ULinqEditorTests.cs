using System.Reflection;
using NUnit.Framework;
using UdonSharp;

[TestFixture]
public class ULinqEditorTests
{
    static readonly FieldInfo AssemblyErrorField = FindAssemblyErrorField();

    [Test] public void ULinqTestBasic_Compiles() => AssertScriptCompiles("ULinqTest");

    static void AssertScriptCompiles(string scriptName)
    {
        Assert.IsNotNull(AssemblyErrorField, "assemblyError field not found");
        foreach (var asset in UdonSharpProgramAsset.GetAllUdonSharpPrograms())
        {
            if (asset.sourceCsScript == null || asset.sourceCsScript.name != scriptName)
                continue;
            var error = (string)AssemblyErrorField.GetValue(asset);
            Assert.IsTrue(string.IsNullOrEmpty(error), $"{scriptName}: {error}");
            return;
        }
        Assert.Fail($"UdonSharpProgramAsset for '{scriptName}' not found");
    }

    static FieldInfo FindAssemblyErrorField()
    {
        for (var t = typeof(UdonSharpProgramAsset); t != null; t = t.BaseType)
        {
            var f = t.GetField("assemblyError",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
}
