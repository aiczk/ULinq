using System;
using ULinq;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

public class ULinqTest : UdonSharpBehaviour
{
    private int[] _nums;
    private float[] _floats;
    private string[] _names;

    private int _fail;

    // --- Expression-bodied members (SG converts arrow → block) ---
    private int[] GetDoubled() => _nums.Select(x => x * 2);
    private int GetFilteredCount() => _nums.Where(x => x > 2).Count();
    private int EvenCount => _nums.Count(x => x % 2 == 0);
    private bool HasLongName => _names.Any(s => s.Length > 4);

    private void Start()
    {
        _nums = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _floats = new[] { 1.5f, 2.5f, 3.5f };
        _names = new[] { "alice", "bob", "carol" };
        _fail = 0;

        // === Select / Where ===
        var doubled = _nums.Select(x => x * 2);
        _fail += doubled[0].Eq("Select[0]", 2);
        _fail += doubled[9].Eq("Select[9]", 20);
        _fail += _names.Select(s => s.Length)[1].Eq("Select<s,i>", 3);

        var evens = _nums.Where(x => x % 2 == 0);
        _fail += evens.Length.Eq("Where.Len", 5);
        _fail += evens[0].Eq("Where[0]", 2);

        // === ForEach ===
        var sum = 0;
        _nums.ForEach(x => sum += x);
        _fail += sum.Eq("ForEach sum", 55);

        // === Scalar queries ===
        _fail += _nums.Any(x => x > 5).Eq("Any(pred)T", true);
        _fail += _nums.Any(x => x > 100).Eq("Any(pred)F", false);
        _fail += _nums.All(x => x > 0).Eq("All T", true);
        _fail += _nums.All(x => x > 5).Eq("All F", false);
        _fail += _nums.Count(x => x % 2 == 0).Eq("Count(pred)", 5);
        _fail += _nums.Any().Eq("Any()", true);
        _fail += new int[0].Any().Eq("Any() empty", false);
        _fail += _nums.Count().Eq("Count()", 10);

        // === Element access ===
        _fail += _nums.First().Eq("First()", 1);
        _fail += _nums.First(x => x > 5).Eq("First(pred)", 6);
        _fail += _nums.FirstOrDefault(x => x > 5).Eq("FirstOrDefault found", 6);
        _fail += _nums.FirstOrDefault(x => x > 100).Eq("FirstOrDefault miss", 0);
        _fail += _nums.FirstOrDefault().Eq("FirstOrDefault()", 1);
        _fail += _nums.Last().Eq("Last()", 10);
        _fail += _nums.Last(x => x < 5).Eq("Last(pred)", 4);
        _fail += _nums.LastOrDefault(x => x < 5).Eq("LastOrDefault found", 4);
        _fail += _nums.LastOrDefault(x => x > 100).Eq("LastOrDefault miss", 0);
        _fail += _nums.LastOrDefault().Eq("LastOrDefault()", 10);

        var single = new[] { 42 };
        _fail += single.Single().Eq("Single()", 42);
        _fail += _nums.Single(x => x == 7).Eq("Single(pred)", 7);
        _fail += single.SingleOrDefault().Eq("SingleOrDefault()", 42);
        _fail += _nums.SingleOrDefault(x => x > 9).Eq("SingleOrDefault(pred)", 10);
        _fail += _nums.SingleOrDefault(x => x > 100).Eq("SingleOrDefault miss", 0);

        _fail += _nums.ElementAt(4).Eq("ElementAt", 5);
        _fail += _nums.ElementAtOrDefault(999).Eq("ElementAtOrDefault", 0);

        // === Numeric ===
        _fail += _nums.Sum().Eq("Sum int", 55);
        _fail += _floats.Sum().Eq("Sum float", 7.5f);
        _fail += _nums.Min().Eq("Min int", 1);
        _fail += _floats.Min().Eq("Min float", 1.5f);
        _fail += _nums.Max().Eq("Max int", 10);
        _fail += _floats.Max().Eq("Max float", 3.5f);
        _fail += _nums.Average().Eq("Avg int", 5.5f);
        _fail += _floats.Average().Eq("Avg float", 2.5f);

        // === Numeric selectors (int + float) ===
        _fail += _names.Sum(s => s.Length).Eq("Sum sel int", 13);
        _fail += _names.Sum(s => (float)s.Length).Eq("Sum sel float", 13f);
        _fail += _names.Min(s => s.Length).Eq("Min sel int", 3);
        _fail += _names.Min(s => (float)s.Length).Eq("Min sel float", 3f);
        _fail += _names.Max(s => s.Length).Eq("Max sel int", 5);
        _fail += _names.Max(s => (float)s.Length).Eq("Max sel float", 5f);
        _fail += _names.Average(s => s.Length).Eq("Avg sel int", 13f / 3f);
        _fail += _names.Average(s => (float)s.Length).Eq("Avg sel float", 13f / 3f);

        // === Collection ops ===
        _fail += _nums.Contains(5).Eq("Contains T", true);
        _fail += _nums.Contains(99).Eq("Contains F", false);
        _fail += _nums.Reverse()[0].Eq("Reverse[0]", 10);
        _fail += _nums.Take(3).Length.Eq("Take.Len", 3);
        _fail += _nums.Skip(8)[0].Eq("Skip[0]", 9);
        _fail += _nums.Concat(new[] { 11, 12 }).Length.Eq("Concat.Len", 12);
        _fail += _nums.Append(99)[10].Eq("Append last", 99);
        _fail += _nums.Prepend(0)[0].Eq("Prepend[0]", 0);
        _fail += new[] { 1, 2, 2, 3, 3, 3 }.Distinct().Length.Eq("Distinct", 3);
        _fail += _nums.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }).Eq("SeqEq T", true);
        _fail += _nums.SequenceEqual(new[] { 1, 2, 3 }).Eq("SeqEq F", false);
        _fail += _nums.TakeWhile(x => x < 5).Length.Eq("TakeWhile", 4);
        _fail += _nums.SkipWhile(x => x < 5)[0].Eq("SkipWhile[0]", 5);
        _fail += new int[0].DefaultIfEmpty().Length.Eq("DefaultIfEmpty", 1);
        _fail += new int[0].DefaultIfEmpty(42)[0].Eq("DefaultIfEmpty val", 42);
        _fail += _nums.ToArray().Length.Eq("ToArray.Len", 10);

        // === Aggregate ===
        _fail += _nums.Aggregate((a, b) => a + b).Eq("Agg no seed", 55);
        _fail += _nums.Aggregate(100, (a, b) => a + b).Eq("Agg seed", 155);

        // === SelectMany ===
        var flat = new[] { new[] { 1, 2 }, new[] { 3, 4 } }.SelectMany(x => x);
        _fail += flat.Length.Eq("SelectMany.Len", 4);
        _fail += flat[2].Eq("SelectMany[2]", 3);

        // === Block lambda ===
        var processed = _nums.Where(x =>
        {
            var threshold = 3;
            return x > threshold;
        });
        _fail += processed.Length.Eq("Block lambda", 7);

        // === Set ops ===
        var a = new[] { 1, 2, 3, 4 };
        var b = new[] { 3, 4, 5, 6 };
        _fail += a.Union(b).Length.Eq("Union", 6);
        _fail += a.Intersect(b).Length.Eq("Intersect", 2);
        _fail += a.Except(b).Length.Eq("Except", 2);

        // === Sort (int + float key) ===
        var unsorted = new[] { 3, 1, 4, 1, 5 };
        _fail += unsorted.OrderBy(x => x)[0].Eq("OrderBy[0]", 1);
        _fail += unsorted.OrderByDescending(x => x)[0].Eq("OrderByDesc[0]", 5);
        _fail += unsorted.OrderBy(x => (float)x)[0].Eq("OrderBy float[0]", 1);
        _fail += unsorted.OrderByDescending(x => (float)x)[0].Eq("OrderByDesc float[0]", 5);

        // === Indexed / Zip ===
        var indexed = _names.Select((s, i) => i + ":" + s);
        _fail += indexed[0].Eq("Select indexed", "0:alice");
        _fail += _nums.Where((x, i) => i % 2 == 0).Length.Eq("Where indexed", 5);
        _fail += _nums.Zip(_floats, (n, f) => n + f).Length.Eq("Zip.Len", 3);

        // === Chains ===
        _fail += _nums.Where(x => x > 3).Select(x => x * 10)[0].Eq("W→S[0]", 40);
        _fail += _nums.Select(x => x * 2).Sum().Eq("S→Sum", 110);
        _fail += _nums.Where(x => x > 5).Count().Eq("W→Count", 5);
        _fail += _nums.Select(x => x * 3).Where(x => x > 10)[0].Eq("S→W[0]", 12);
        _fail += _nums.Where(x => x > 2).Where(x => x < 8).Length.Eq("W→W.Len", 5);
        _fail += _nums.Select(x => x + 1).Select(x => x * 2)[0].Eq("S→S[0]", 4);
        _fail += _nums.Select(x => x + 1).Where(x => x > 5).Select(x => x * 10)[0].Eq("3chain[0]", 60);
        _fail += _nums.Distinct().Select(x => x * 2)[0].Eq("D→S[0]", 2);

        // === Expression-bodied members ===
        _fail += GetDoubled().Length.Eq("ExprM Doubled.Len", 10);
        _fail += GetDoubled()[0].Eq("ExprM Doubled[0]", 2);
        _fail += GetFilteredCount().Eq("ExprM FilteredCount", 8);
        _fail += EvenCount.Eq("ExprP EvenCount", 5);
        _fail += HasLongName.Eq("ExprP HasLongName", true);

        // === Short-circuit evaluation ===
        _fail += (_nums.Any(x => x > 5) && _nums.All(x => x > 0)).Eq("&& TT", true);
        _fail += (_nums.Any(x => x > 100) && _nums.All(x => x > 0)).Eq("&& FT", false);
        _fail += (_nums.Any(x => x > 5) || _nums.Any(x => x > 100)).Eq("|| TF", true);
        _fail += (_nums.Any(x => x > 100) || _nums.Any(x => x > 5)).Eq("|| FT", true);
        _fail += (_nums.Any(x => x > 100) || _nums.Any(x => x > 200)).Eq("|| FF", false);
        _fail += (_nums.Any(x => x > 5) && _nums.All(x => x > 0) && _nums.Any(x => x == 10)).Eq("&&& chain", true);
        var scTern = true ? _nums.Count(x => x > 5) : _nums.Count(x => x < 5);
        _fail += scTern.Eq("?: true branch", 5);

        // === DataList ===
        TestDataList();

        if (_fail == 0)
            Debug.Log("<color=green>ULinqTest: ALL PASSED</color>");
        else
            Debug.LogError($"ULinqTest: {_fail} FAILED");
    }

    void TestDataList()
    {
        var dl = new DataList();
        dl.Add(1); dl.Add(2); dl.Add(3); dl.Add(4); dl.Add(5);

        var dlSum = 0;
        dl.ForEach(x => dlSum += x.Int);
        _fail += dlSum.Eq("DL ForEach", 15);
        _fail += dl.Where(x => x.Int > 3).Count.Eq("DL Where", 2);
        _fail += dl.Any(x => x.Int > 4).Eq("DL Any(pred)", true);
        _fail += dl.All(x => x.Int > 0).Eq("DL All(pred)", true);
        _fail += dl.Count(x => x.Int % 2 == 0).Eq("DL Count(pred)", 2);
        _fail += dl.Select(x => new DataToken(x.Int * 10))[0].Int.Eq("DL Select[0]", 10);
        _fail += dl.First(x => x.Int > 2).Int.Eq("DL First(pred)", 3);
        // default(DataToken).Int throws in Udon — skip miss case
        _fail += dl.Last(x => x.Int < 4).Int.Eq("DL Last(pred)", 3);
        // default(DataToken).Int throws in Udon — skip miss case
        _fail += dl.Single(x => x.Int == 3).Int.Eq("DL Single(pred)", 3);
        // default(DataToken).Int throws in Udon — skip miss case
        _fail += dl.Aggregate(new DataToken(0), (acc, x) => new DataToken(acc.Int + x.Int)).Int.Eq("DL Agg", 15);
        _fail += dl.TakeWhile(x => x.Int < 4).Count.Eq("DL TakeWhile", 3);
        _fail += dl.SkipWhile(x => x.Int < 4)[0].Int.Eq("DL SkipWhile[0]", 4);
        _fail += dl.Any().Eq("DL Any()", true);
        _fail += dl.First().Int.Eq("DL First()", 1);
        _fail += dl.FirstOrDefault().Int.Eq("DL FirstOrDefault()", 1);
        _fail += dl.Last().Int.Eq("DL Last()", 5);
        _fail += dl.LastOrDefault().Int.Eq("DL LastOrDefault()", 5);
        var dlSingle = new DataList(); dlSingle.Add(42);
        _fail += dlSingle.Single().Int.Eq("DL Single()", 42);
        _fail += dlSingle.SingleOrDefault().Int.Eq("DL SingleOrDefault()", 42);
        _fail += dl.Take(3).Count.Eq("DL Take", 3);
        _fail += dl.Skip(3)[0].Int.Eq("DL Skip[0]", 4);
        var dl2 = new DataList(); dl2.Add(6); dl2.Add(7);
        _fail += dl.Concat(dl2).Count.Eq("DL Concat", 7);
        _fail += dl.Append(new DataToken(99)).Count.Eq("DL Append", 6);
        _fail += dl.Prepend(new DataToken(0))[0].Int.Eq("DL Prepend[0]", 0);
        var dlDup = new DataList(); dlDup.Add(1); dlDup.Add(2); dlDup.Add(2); dlDup.Add(3);
        _fail += dlDup.Distinct().Count.Eq("DL Distinct", 3);
        var dlCopy = new DataList(); dlCopy.Add(1); dlCopy.Add(2); dlCopy.Add(3); dlCopy.Add(4); dlCopy.Add(5);
        _fail += dl.SequenceEqual(dlCopy).Eq("DL SeqEq", true);
    }
}
