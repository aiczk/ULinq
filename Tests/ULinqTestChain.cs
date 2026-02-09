using System;
using ULinq;
using UdonSharp;
using UnityEngine;

public class ULinqTestChain : UdonSharpBehaviour
{
    public int[] nums;
    public float[] floats;
    public string[] names;

    private void Start()
    {
        nums = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        floats = new[] { 1.5f, 2.5f, 3.5f, 4.5f };
        names = new[] { "alice", "bob", "carol", "dave" };

        // ===== Group A: Producer → Producer =====

        // Where→Select
        var whereSelect = nums.Where(x => x > 3).Select(x => x * 10);

        // Where→Where
        var whereWhere = nums.Where(x => x > 2).Where(x => x < 8);

        // Select→Select
        var selectSelect = nums.Select(x => x + 1).Select(x => x * 2);

        // Select→Where
        var selectWhere = nums.Select(x => x * 3).Where(x => x > 10);

        // ===== Group B: Where → no-lambda Terminal =====

        // Where→Any()
        var whereAny0 = nums.Where(x => x > 100).Any();

        // Where→Count()
        var whereCount0 = nums.Where(x => x > 5).Count();

        // Where→First()
        var whereFirst = nums.Where(x => x > 3).First();

        // Where→FirstOrDefault()
        var whereFirstDefault = nums.Where(x => x > 100).FirstOrDefault();

        // Where→Last()
        var whereLast = nums.Where(x => x < 8).Last();

        // Where→LastOrDefault()
        var whereLastDefault = nums.Where(x => x > 100).LastOrDefault();

        // Where→Sum()
        var whereSum = nums.Where(x => x > 5).Sum();

        // Where→Min()
        var whereMin = nums.Where(x => x > 3).Min();

        // Where→Max()
        var whereMax = nums.Where(x => x < 8).Max();

        // ===== Group C: Where → lambda Terminal =====

        // Where→ForEach(action)
        nums.Where(x => x > 7).ForEach(x => Debug.Log(x));

        // Where→Any(pred)
        var whereAny1 = nums.Where(x => x > 3).Any(x => x % 2 == 0);

        // Where→All(pred)
        var whereAll1 = nums.Where(x => x > 0).All(x => x < 100);

        // Where→Count(pred)
        var whereCount1 = nums.Where(x => x > 3).Count(x => x % 2 == 0);

        // Where→FirstOrDefault(pred)
        var whereFirstDefault1 = nums.Where(x => x > 3).FirstOrDefault(x => x % 2 == 0);

        // ===== Group D: Select → no-lambda Terminal =====

        // Select→Sum()
        var selectSum = nums.Select(x => x * 2).Sum();

        // Select→Min()
        var selectMin = nums.Select(x => x + 100).Min();

        // Select→Max()
        var selectMax = nums.Select(x => x - 1).Max();

        // Select→Average()
        var selectAvg = nums.Select(x => x * 3).Average();

        // Select→Any() (degenerate — length unchanged)
        var selectAny0 = nums.Select(x => x * 2).Any();

        // Select→Count() (degenerate — length unchanged)
        var selectCount0 = nums.Select(x => x * 2).Count();

        // ===== Group E: Select → lambda Terminal =====

        // Select→ForEach(action)
        nums.Select(x => x.ToString()).ForEach(s => Debug.Log(s));

        // ===== Fallback: non-fused chain =====

        // Distinct→Select
        var distinctSelect = nums.Distinct().Select(x => x * 2);

        // ===== 3-chain =====

        // Select→Where→Select
        var threeChain = nums.Select(x => x + 1).Where(x => x > 5).Select(x => x * 10);

        Debug.Log("ULinqTestChain: all chain operations completed");
    }
}
