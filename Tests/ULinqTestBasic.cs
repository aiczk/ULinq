using System;
using ULinq;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

public class ULinqTestBasic : UdonSharpBehaviour
{
    public int[] nums;
    public float[] floats;
    public string[] names;
    public Transform[] transforms;

    private void Start()
    {
        nums = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        floats = new[] { 1.5f, 2.5f, 3.5f };
        names = new[] { "alice", "bob", "carol" };

        // --- ForEach ---
        nums.ForEach(x => Debug.Log(x));

        // --- Select ---
        var doubled = nums.Select(x => x * 2);
        var lengths = names.Select(s => s.Length);

        // --- Where ---
        var evens = nums.Where(x => x % 2 == 0);

        // --- Any / All ---
        var hasLarge = nums.Any(x => x > 5);
        var allPositive = nums.All(x => x > 0);
        var anyExists = nums.Any();

        // --- Count ---
        var evenCount = nums.Count(x => x % 2 == 0);
        var totalCount = nums.Count();

        // --- First / FirstOrDefault ---
        var firstEven = nums.First(x => x % 2 == 0);
        var firstDefault = nums.FirstOrDefault(x => x > 100);
        var firstAny = nums.First();
        var firstOrDefaultAny = nums.FirstOrDefault();

        // --- Last / LastOrDefault ---
        var lastEven = nums.Last(x => x % 2 == 0);
        var lastDefault = nums.LastOrDefault(x => x > 100);
        var lastAny = nums.Last();
        var lastOrDefaultAny = nums.LastOrDefault();

        // --- Single / SingleOrDefault ---
        var singleArr = new[] { 42 };
        var singleVal = singleArr.Single();
        var singleDefault = nums.SingleOrDefault(x => x > 9);

        // --- Sum ---
        var intSum = nums.Sum();
        var floatSum = floats.Sum();

        // --- Min / Max ---
        var intMin = nums.Min();
        var intMax = nums.Max();
        var floatMin = floats.Min();
        var floatMax = floats.Max();

        // --- Average ---
        var intAvg = nums.Average();
        var floatAvg = floats.Average();

        // --- Sum/Min/Max/Average with selector ---
        var sumSel = names.Sum(s => s.Length);
        var minSel = names.Min(s => s.Length);
        var maxSel = names.Max(s => s.Length);
        var avgSel = names.Average(s => s.Length);

        // --- Contains ---
        var has3 = nums.Contains(3);

        // --- Reverse ---
        var reversed = nums.Reverse();

        // --- Take / Skip ---
        var taken = nums.Take(3);
        var skipped = nums.Skip(3);

        // --- TakeWhile / SkipWhile ---
        var takenWhile = nums.TakeWhile(x => x < 5);
        var skippedWhile = nums.SkipWhile(x => x < 5);

        // --- Concat ---
        var concatenated = nums.Concat(new[] { 11, 12 });

        // --- Distinct ---
        var duped = new[] { 1, 2, 2, 3, 3, 3 };
        var distinct = duped.Distinct();

        // --- Append / Prepend ---
        var appended = nums.Append(99);
        var prepended = nums.Prepend(0);

        // --- Aggregate (1-param) ---
        var product = nums.Aggregate((a, b) => a * b);

        // --- Aggregate with seed ---
        var csv = names.Aggregate("", (acc, s) => acc + "," + s);

        // --- SelectMany ---
        var nested = new[] { new[] { 1, 2 }, new[] { 3, 4 } };
        var flat = nested.SelectMany(x => x);

        // --- Block lambda (hoisting) ---
        var processed = nums.Where(x =>
        {
            var threshold = 3;
            return x > threshold;
        });

        // --- ElementAt / ElementAtOrDefault ---
        var elem = nums.ElementAt(2);
        var elemDefault = nums.ElementAtOrDefault(999);

        // --- DefaultIfEmpty ---
        var empty = new int[0];
        var nonEmpty = empty.DefaultIfEmpty();
        var nonEmptyVal = empty.DefaultIfEmpty(42);

        // --- SequenceEqual ---
        var eq = nums.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        // --- Set operations ---
        var a = new[] { 1, 2, 3, 4 };
        var b = new[] { 3, 4, 5, 6 };
        var union = a.Union(b);
        var intersect = a.Intersect(b);
        var except = a.Except(b);

        // --- OrderBy / OrderByDescending ---
        var unsorted = new[] { 3, 1, 4, 1, 5 };
        var sorted = unsorted.OrderBy(x => x);
        var sortedDesc = unsorted.OrderByDescending(x => x);

        // --- Indexed Select / Where ---
        var indexed = names.Select((s, i) => i + ":" + s);
        var indexFiltered = nums.Where((x, i) => i % 2 == 0);

        // --- Zip ---
        var zipped = nums.Zip(floats, (n, f) => n + f);

        // --- Float selector overloads ---
        var floatMinSel = names.Min(s => (float)s.Length);
        var floatMaxSel = names.Max(s => (float)s.Length);
        var floatSumSel = names.Sum(s => (float)s.Length);
        var floatAvgSel = names.Average(s => (float)s.Length);

        Debug.Log("ULinqTestBasic: all array operations completed");

        // ======================
        // DataList Tests
        // ======================
        var dl = new DataList();
        dl.Add(1);
        dl.Add(2);
        dl.Add(3);
        dl.Add(4);
        dl.Add(5);

        // --- Existing methods ---
        dl.ForEach(x => Debug.Log(x));
        var dlFiltered = dl.Where(x => x.Int > 2);
        var dlAnyPred = dl.Any(x => x.Int > 3);
        var dlAllPred = dl.All(x => x.Int > 0);
        var dlCountPred = dl.Count(x => x.Int % 2 == 0);
        var dlFirstOrDefaultPred = dl.FirstOrDefault(x => x.Int > 100);

        // --- Select ---
        var dlSelected = dl.Select(x => new DataToken(x.Int * 10));

        // --- First(predicate) ---
        var dlFirstPred = dl.First(x => x.Int > 2);

        // --- Last(predicate) / LastOrDefault(predicate) ---
        var dlLastPred = dl.Last(x => x.Int < 4);
        var dlLastOrDefaultPred = dl.LastOrDefault(x => x.Int > 100);

        // --- Single(predicate) / SingleOrDefault(predicate) ---
        var dlSinglePred = dl.Single(x => x.Int == 3);
        var dlSingleOrDefaultPred = dl.SingleOrDefault(x => x.Int > 100);

        // --- Aggregate ---
        var dlAgg = dl.Aggregate(new DataToken(0), (acc, x) => new DataToken(acc.Int + x.Int));

        // --- TakeWhile / SkipWhile ---
        var dlTakeWhile = dl.TakeWhile(x => x.Int < 4);
        var dlSkipWhile = dl.SkipWhile(x => x.Int < 4);

        // --- Any() (no predicate) ---
        var dlAnyNoPred = dl.Any();

        // --- First() / FirstOrDefault() ---
        var dlFirst = dl.First();
        var dlFirstOrDefault = dl.FirstOrDefault();

        // --- Last() / LastOrDefault() ---
        var dlLast = dl.Last();
        var dlLastOrDefault = dl.LastOrDefault();

        // --- Single() / SingleOrDefault() ---
        var dlSingleList = new DataList();
        dlSingleList.Add(42);
        var dlSingle = dlSingleList.Single();
        var dlSingleOrDefault = dlSingleList.SingleOrDefault();

        // --- Take / Skip ---
        var dlTaken = dl.Take(3);
        var dlSkipped = dl.Skip(3);

        // --- Concat ---
        var dlOther = new DataList();
        dlOther.Add(6);
        dlOther.Add(7);
        var dlConcat = dl.Concat(dlOther);

        // --- Append / Prepend ---
        var dlAppended = dl.Append(new DataToken(99));
        var dlPrepended = dl.Prepend(new DataToken(0));

        // --- Distinct ---
        var dlDuped = new DataList();
        dlDuped.Add(1);
        dlDuped.Add(2);
        dlDuped.Add(2);
        dlDuped.Add(3);
        var dlDistinct = dlDuped.Distinct();

        // --- SequenceEqual ---
        var dlCopy = new DataList();
        dlCopy.Add(1);
        dlCopy.Add(2);
        dlCopy.Add(3);
        dlCopy.Add(4);
        dlCopy.Add(5);
        var dlSeqEq = dl.SequenceEqual(dlCopy);

        Debug.Log("ULinqTestBasic: all operations completed");
    }
}
