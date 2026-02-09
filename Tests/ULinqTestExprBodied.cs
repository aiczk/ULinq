using System;
using ULinq;
using UdonSharp;
using UnityEngine;

public class ULinqTestExprBodied : UdonSharpBehaviour
{
    public int[] nums;
    public string[] names;

    private void Start()
    {
        nums = new[] { 1, 2, 3, 4, 5 };
        names = new[] { "alice", "bob", "carol" };

        Debug.Log($"Doubled: {GetDoubled().Length}");
        Debug.Log($"EvenCount: {EvenCount}");
        Debug.Log($"HasLong: {HasLongName}");
        Debug.Log($"FusedCount: {GetFilteredCount()}");
    }

    // expression-bodied method: Select
    int[] GetDoubled() => nums.Select(x => x * 2);

    // expression-bodied method: Where→Count fusion
    int GetFilteredCount() => nums.Where(x => x > 2).Count();

    // expression-bodied property: Count(pred)
    int EvenCount => nums.Count(x => x % 2 == 0);

    // expression-bodied property: Any(pred)
    bool HasLongName => names.Any(s => s.Length > 4);
}
