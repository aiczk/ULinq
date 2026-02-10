using ULinq;
using UnityEngine;

public static class TestAssert
{
    [Inline]
    public static int Eq<T>(this T actual, string label, T expected)
    {
        if (actual.Equals(expected))
            return 0;

        Debug.LogError($"[FAIL] {label}: expected {expected}, got {actual}");
        return 1;
    }
}
