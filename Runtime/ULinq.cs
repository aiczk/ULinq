using System;
using UdonLambda;
using VRC.SDK3.Data;

namespace ULinq
{
    public static class ArrayExtensions
    {
        // --- Lambda-based operators ---

        /// <summary>Executes an action for each element. Equivalent to LINQ <c>ForEach</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="action">The action to execute on each element.</param>
        [Inline]
        public static void ForEach<T>(this T[] array, Action<T> action)
        {
            foreach (var t in array)
                action(t);
        }

        /// <summary>Projects each element into a new form. Equivalent to LINQ <c>Select</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="func">A transform function applied to each element.</param>
        /// <returns>An array of transformed elements.</returns>
        [Inline]
        public static TResult[] Select<T, TResult>(this T[] array, Func<T, TResult> func)
        {
            var result = new TResult[array.Length];
            for (var i = 0; i < array.Length; i++)
                result[i] = func(array[i]);
            return result;
        }

        /// <summary>Filters elements by a predicate. Equivalent to LINQ <c>Where</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="predicate">A function that returns <c>true</c> for elements to include.</param>
        /// <returns>An array containing only elements that satisfy the predicate.</returns>
        [Inline]
        public static T[] Where<T>(this T[] array, Func<T, bool> predicate)
        {
            var temp = new T[array.Length];
            var count = 0;
            foreach (var t in array)
            {
                if (!predicate(t)) continue;
                temp[count] = t;
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        /// <summary>Determines whether any element satisfies a condition. Equivalent to LINQ <c>Any</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns><c>true</c> if at least one element satisfies the predicate; otherwise <c>false</c>.</returns>
        [Inline]
        public static bool Any<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = false;
            foreach (var t in array)
            {
                if (!predicate(t)) continue;
                result = true;
                break;
            }
            return result;
        }

        /// <summary>Determines whether all elements satisfy a condition. Equivalent to LINQ <c>All</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns><c>true</c> if every element satisfies the predicate; otherwise <c>false</c>.</returns>
        [Inline]
        public static bool All<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = true;
            foreach (var t in array)
            {
                if (predicate(t)) continue;
                result = false;
                break;
            }
            return result;
        }

        /// <summary>Counts elements that satisfy a condition. Equivalent to LINQ <c>Count</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>The number of elements that satisfy the predicate.</returns>
        [Inline]
        public static int Count<T>(this T[] array, Func<T, bool> predicate)
        {
            var count = 0;
            foreach (var t in array)
            {
                if (!predicate(t)) continue;
                count++;
            }
            return count;
        }

        /// <summary>Returns the first element that satisfies a condition, or <c>default(T)</c> if none found. Equivalent to LINQ <c>FirstOrDefault</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>The first matching element, or <c>default(T)</c> if no match.</returns>
        [Inline]
        public static T FirstOrDefault<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = default(T);
            foreach (var t in array)
            {
                if (!predicate(t)) continue;
                result = t;
                break;
            }
            return result;
        }

        /// <summary>Returns the last element that satisfies a condition, or <c>default(T)</c> if none found. Equivalent to LINQ <c>LastOrDefault</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>The last matching element, or <c>default(T)</c> if no match.</returns>
        [Inline]
        public static T LastOrDefault<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = default(T);
            for (var i = array.Length - 1; i >= 0; i--)
            {
                if (!predicate(array[i])) continue;
                result = array[i];
                break;
            }
            return result;
        }

        /// <summary>Applies an accumulator function over the array. Equivalent to LINQ <c>Aggregate</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="func">An accumulator function taking the current result and next element.</param>
        /// <returns>The final accumulated value.</returns>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays. Uses the first element as the initial value.</remarks>
        [Inline]
        public static T Aggregate<T>(this T[] array, Func<T, T, T> func)
        {
            var result = array[0];
            for (var i = 1; i < array.Length; i++)
                result = func(result, array[i]);
            return result;
        }

        /// <summary>Applies an accumulator function with a seed value. Equivalent to LINQ <c>Aggregate</c> with seed.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="seed">The initial accumulator value.</param>
        /// <param name="func">An accumulator function taking the current result and next element.</param>
        /// <returns>The final accumulated value.</returns>
        [Inline]
        public static TResult Aggregate<T, TResult>(this T[] array, TResult seed, Func<TResult, T, TResult> func)
        {
            var result = seed;
            foreach (var t in array)
                result = func(result, t);

            return result;
        }

        /// <summary>Projects each element to an array and flattens the results. Equivalent to LINQ <c>SelectMany</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="func">A function that returns an array for each element.</param>
        /// <returns>A flattened array of all projected elements.</returns>
        [Inline]
        public static TResult[] SelectMany<T, TResult>(this T[] array, Func<T, TResult[]> func)
        {
            var temps = new TResult[array.Length][];
            var totalCount = 0;
            for (var i = 0; i < array.Length; i++)
            {
                temps[i] = func(array[i]);
                totalCount += temps[i].Length;
            }
            var result = new TResult[totalCount];
            var offset = 0;
            for (var i = 0; i < array.Length; i++)
            {
                for (var j = 0; j < temps[i].Length; j++)
                    result[offset + j] = temps[i][j];
                offset += temps[i].Length;
            }
            return result;
        }

        // --- Numeric specializations ---

        /// <summary>Computes the sum of an <c>int</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The sum of all elements.</returns>
        [Inline]
        public static int Sum(this int[] array)
        {
            var result = 0;
            foreach (var t in array)
                result += t;
            return result;
        }

        /// <summary>Computes the sum of a <c>float</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The sum of all elements.</returns>
        [Inline]
        public static float Sum(this float[] array)
        {
            var result = 0f;
            foreach (var t in array)
                result += t;
            return result;
        }

        /// <summary>Returns the minimum value in an <c>int</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The minimum element.</returns>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static int Min(this int[] array)
        {
            var result = array[0];
            for (var i = 1; i < array.Length; i++)
            {
                if (array[i] >= result) continue;
                result = array[i];
            }
            return result;
        }

        /// <summary>Returns the minimum value in a <c>float</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The minimum element.</returns>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static float Min(this float[] array)
        {
            var result = array[0];
            for (var i = 1; i < array.Length; i++)
            {
                if (array[i] >= result) continue;
                result = array[i];
            }
            return result;
        }

        /// <summary>Returns the maximum value in an <c>int</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The maximum element.</returns>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static int Max(this int[] array)
        {
            var result = array[0];
            for (var i = 1; i < array.Length; i++)
            {
                if (array[i] <= result) continue;
                result = array[i];
            }
            return result;
        }

        /// <summary>Returns the maximum value in a <c>float</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The maximum element.</returns>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static float Max(this float[] array)
        {
            var result = array[0];
            for (var i = 1; i < array.Length; i++)
            {
                if (array[i] <= result) continue;
                result = array[i];
            }
            return result;
        }

        /// <summary>Computes the average of an <c>int</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The arithmetic mean as <c>float</c>.</returns>
        /// <remarks>Throws <c>DivideByZeroException</c> on empty arrays.</remarks>
        [Inline]
        public static float Average(this int[] array)
        {
            var sum = 0;
            foreach (var t in array)
                sum += t;
            var result = (float)sum / array.Length;
            return result;
        }

        /// <summary>Computes the average of a <c>float</c> array.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The arithmetic mean.</returns>
        /// <remarks>Throws <c>DivideByZeroException</c> on empty arrays.</remarks>
        [Inline]
        public static float Average(this float[] array)
        {
            var sum = 0f;
            foreach (var t in array)
                sum += t;
            var result = sum / array.Length;
            return result;
        }

        // --- Generic operators ---

        /// <summary>Determines whether the array contains a specific value. Equivalent to LINQ <c>Contains</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="value">The value to locate.</param>
        /// <returns><c>true</c> if the value is found; otherwise <c>false</c>.</returns>
        /// <remarks>Uses <c>.Equals()</c> for comparison. Throws <c>NullReferenceException</c> if an element is <c>null</c>.</remarks>
        [Inline]
        public static bool Contains<T>(this T[] array, T value)
        {
            var result = false;
            foreach (var t in array)
            {
                if (!t.Equals(value)) continue;
                result = true;
                break;
            }
            return result;
        }

        /// <summary>Returns a new array with elements in reverse order. Equivalent to LINQ <c>Reverse</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>A new reversed array.</returns>
        [Inline]
        public static T[] Reverse<T>(this T[] array)
        {
            var result = new T[array.Length];
            for (var i = 0; i < array.Length; i++)
                result[i] = array[array.Length - 1 - i];
            return result;
        }

        /// <summary>Returns the first <paramref name="count"/> elements. Equivalent to LINQ <c>Take</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="count">The number of elements to take.</param>
        /// <returns>A new array with the first <paramref name="count"/> elements.</returns>
        /// <remarks>Unlike LINQ, throws if <paramref name="count"/> exceeds <c>array.Length</c>. Negative values cause <c>OverflowException</c> on array allocation.</remarks>
        [Inline]
        public static T[] Take<T>(this T[] array, int count)
        {
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = array[i];
            return result;
        }

        /// <summary>Skips the first <paramref name="count"/> elements. Equivalent to LINQ <c>Skip</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="count">The number of elements to skip.</param>
        /// <returns>A new array without the first <paramref name="count"/> elements.</returns>
        /// <remarks>Unlike LINQ, throws if <paramref name="count"/> exceeds <c>array.Length</c>. Negative values cause <c>OverflowException</c> on array allocation.</remarks>
        [Inline]
        public static T[] Skip<T>(this T[] array, int count)
        {
            var len = array.Length - count;
            var result = new T[len];
            for (var i = 0; i < len; i++)
                result[i] = array[count + i];
            return result;
        }

        /// <summary>Concatenates two arrays. Equivalent to LINQ <c>Concat</c>.</summary>
        /// <param name="array">The first array.</param>
        /// <param name="other">The second array.</param>
        /// <returns>A new array containing elements from both arrays.</returns>
        [Inline]
        public static T[] Concat<T>(this T[] array, T[] other)
        {
            var result = new T[array.Length + other.Length];
            for (var i = 0; i < array.Length; i++)
                result[i] = array[i];
            for (var i = 0; i < other.Length; i++)
                result[array.Length + i] = other[i];
            return result;
        }

        /// <summary>Returns the last element. Equivalent to LINQ <c>Last</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>The last element.</returns>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static T Last<T>(this T[] array)
        {
            var result = array[array.Length - 1];
            return result;
        }

        /// <summary>Adds an element to the end. Equivalent to LINQ <c>Append</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="value">The value to append.</param>
        /// <returns>A new array with the value appended.</returns>
        [Inline]
        public static T[] Append<T>(this T[] array, T value)
        {
            var result = new T[array.Length + 1];
            for (var i = 0; i < array.Length; i++)
                result[i] = array[i];
            result[array.Length] = value;
            return result;
        }

        /// <summary>Adds an element to the beginning. Equivalent to LINQ <c>Prepend</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <param name="value">The value to prepend.</param>
        /// <returns>A new array with the value prepended.</returns>
        [Inline]
        public static T[] Prepend<T>(this T[] array, T value)
        {
            var result = new T[array.Length + 1];
            result[0] = value;
            for (var i = 0; i < array.Length; i++)
                result[i + 1] = array[i];
            return result;
        }

        /// <summary>Returns distinct elements using <c>Equals</c>. Equivalent to LINQ <c>Distinct</c>.</summary>
        /// <param name="array">The source array.</param>
        /// <returns>A new array with duplicate elements removed.</returns>
        /// <remarks>Uses O(n^2) comparison via <c>.Equals()</c>. Throws <c>NullReferenceException</c> if an element is <c>null</c>. Suitable for small arrays typical in VRChat.</remarks>
        [Inline]
        public static T[] Distinct<T>(this T[] array)
        {
            var temp = new T[array.Length];
            var count = 0;
            foreach (var t in array)
            {
                var found = false;
                for (var j = 0; j < count; j++)
                {
                    if (!temp[j].Equals(t)) continue;
                    found = true;
                    break;
                }
                if (found) continue;
                temp[count] = t;
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        /// <summary>Determines whether two arrays are equal element-by-element. Equivalent to LINQ <c>SequenceEqual</c>.</summary>
        /// <param name="array">The first array.</param>
        /// <param name="other">The second array to compare.</param>
        /// <returns><c>true</c> if both arrays have the same length and equal elements; otherwise <c>false</c>.</returns>
        /// <remarks>Uses <c>.Equals()</c> for comparison. Throws <c>NullReferenceException</c> if an element is <c>null</c>.</remarks>
        [Inline]
        public static bool SequenceEqual<T>(this T[] array, T[] other)
        {
            var result = true;
            if (array.Length != other.Length)
            {
                result = false;
            }
            else
            {
                for (var i = 0; i < array.Length; i++)
                {
                    if (array[i].Equals(other[i])) continue;
                    result = false;
                    break;
                }
            }
            return result;
        }

        // --- No-predicate overloads ---

        /// <summary>Determines whether the array contains any elements.</summary>
        [Inline]
        public static bool Any<T>(this T[] array)
        {
            var result = array.Length > 0;
            return result;
        }

        /// <summary>Returns the number of elements in the array.</summary>
        [Inline]
        public static int Count<T>(this T[] array)
        {
            var result = array.Length;
            return result;
        }

        /// <summary>Returns the first element.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static T First<T>(this T[] array)
        {
            var result = array[0];
            return result;
        }

        /// <summary>Returns the first element that satisfies a condition.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if no element matches.</remarks>
        [Inline]
        public static T First<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = default(T);
            var found = false;
            foreach (var t in array)
            {
                if (!predicate(t)) continue;
                result = t;
                found = true;
                break;
            }
            if (!found) result = array[array.Length];
            return result;
        }

        /// <summary>Returns the first element, or <c>default(T)</c> if empty.</summary>
        [Inline]
        public static T FirstOrDefault<T>(this T[] array)
        {
            var result = default(T);
            if (array.Length > 0) result = array[0];
            return result;
        }

        /// <summary>Returns the last element that satisfies a condition.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if no element matches.</remarks>
        [Inline]
        public static T Last<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = default(T);
            var found = false;
            for (var i = array.Length - 1; i >= 0; i--)
            {
                if (!predicate(array[i])) continue;
                result = array[i];
                found = true;
                break;
            }
            if (!found) result = array[array.Length];
            return result;
        }

        /// <summary>Returns the last element, or <c>default(T)</c> if empty.</summary>
        [Inline]
        public static T LastOrDefault<T>(this T[] array)
        {
            var result = default(T);
            if (array.Length > 0) result = array[array.Length - 1];
            return result;
        }

        // --- Single series ---

        /// <summary>Returns the single element of the array.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if empty or more than one element.</remarks>
        [Inline]
        public static T Single<T>(this T[] array)
        {
            if (array.Length > 1) { var e = array[array.Length]; }
            var result = array[0];
            return result;
        }

        /// <summary>Returns the single element that satisfies a condition.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if no match or more than one match.</remarks>
        [Inline]
        public static T Single<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = default(T);
            var count = 0;
            foreach (var t in array)
            {
                if (!predicate(t)) continue;
                result = t;
                count++;
            }
            if (count != 1) result = array[array.Length];
            return result;
        }

        /// <summary>Returns the single element, or <c>default(T)</c> if empty.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if more than one element.</remarks>
        [Inline]
        public static T SingleOrDefault<T>(this T[] array)
        {
            if (array.Length > 1) { var e = array[array.Length]; }
            var result = default(T);
            if (array.Length > 0) result = array[0];
            return result;
        }

        /// <summary>Returns the single element that satisfies a condition, or <c>default(T)</c>.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if more than one match.</remarks>
        [Inline]
        public static T SingleOrDefault<T>(this T[] array, Func<T, bool> predicate)
        {
            var result = default(T);
            var count = 0;
            foreach (var t in array)
            {
                if (!predicate(t)) continue;
                result = t;
                count++;
            }
            if (count > 1) result = array[array.Length];
            return result;
        }

        // --- Element access & utility ---

        /// <summary>Returns the element at a specified index.</summary>
        [Inline]
        public static T ElementAt<T>(this T[] array, int index)
        {
            var result = array[index];
            return result;
        }

        /// <summary>Returns the element at a specified index, or <c>default(T)</c> if out of range.</summary>
        [Inline]
        public static T ElementAtOrDefault<T>(this T[] array, int index)
        {
            var result = default(T);
            if (index >= 0 && index < array.Length) result = array[index];
            return result;
        }

        /// <summary>Returns the array, or a single-element array with <c>default(T)</c> if empty.</summary>
        [Inline]
        public static T[] DefaultIfEmpty<T>(this T[] array)
        {
            var result = array;
            if (array.Length == 0) result = new T[1];
            return result;
        }

        /// <summary>Returns the array, or a single-element array with the specified value if empty.</summary>
        [Inline]
        public static T[] DefaultIfEmpty<T>(this T[] array, T defaultValue)
        {
            var result = array;
            if (array.Length == 0)
            {
                result = new T[1];
                result[0] = defaultValue;
            }
            return result;
        }

        /// <summary>Creates a copy of the array.</summary>
        [Inline]
        public static T[] ToArray<T>(this T[] array)
        {
            var result = new T[array.Length];
            for (var i = 0; i < array.Length; i++)
                result[i] = array[i];
            return result;
        }

        // --- Conditional Take/Skip ---

        /// <summary>Returns elements while the predicate is true.</summary>
        [Inline]
        public static T[] TakeWhile<T>(this T[] array, Func<T, bool> predicate)
        {
            var temp = new T[array.Length];
            var count = 0;
            foreach (var t in array)
            {
                if (!predicate(t)) break;
                temp[count] = t;
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        /// <summary>Skips elements while the predicate is true, then returns the rest.</summary>
        [Inline]
        public static T[] SkipWhile<T>(this T[] array, Func<T, bool> predicate)
        {
            var temp = new T[array.Length];
            var count = 0;
            var skipping = true;
            foreach (var t in array)
            {
                if (skipping && predicate(t)) continue;
                skipping = false;
                temp[count] = t;
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        // --- Indexed overloads ---

        /// <summary>Projects each element with its index into a new form.</summary>
        [Inline]
        public static TResult[] Select<T, TResult>(this T[] array, Func<T, int, TResult> func)
        {
            var result = new TResult[array.Length];
            for (var i = 0; i < array.Length; i++)
                result[i] = func(array[i], i);
            return result;
        }

        /// <summary>Filters elements by a predicate that receives the element index.</summary>
        [Inline]
        public static T[] Where<T>(this T[] array, Func<T, int, bool> predicate)
        {
            var temp = new T[array.Length];
            var count = 0;
            for (var i = 0; i < array.Length; i++)
            {
                if (!predicate(array[i], i)) continue;
                temp[count] = array[i];
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        // --- Zip ---

        /// <summary>Merges two arrays element-by-element using a function.</summary>
        [Inline]
        public static TResult[] Zip<T, TSecond, TResult>(this T[] array, TSecond[] other, Func<T, TSecond, TResult> func)
        {
            var len = array.Length;
            if (other.Length < len) len = other.Length;
            var result = new TResult[len];
            for (var i = 0; i < len; i++)
                result[i] = func(array[i], other[i]);
            return result;
        }

        // --- Selector overloads ---

        /// <summary>Returns the minimum value by an int selector.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static int Min<T>(this T[] array, Func<T, int> selector)
        {
            var result = selector(array[0]);
            for (var i = 1; i < array.Length; i++)
            {
                var v = selector(array[i]);
                if (v >= result) continue;
                result = v;
            }
            return result;
        }

        /// <summary>Returns the minimum value by a float selector.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static float Min<T>(this T[] array, Func<T, float> selector)
        {
            var result = selector(array[0]);
            for (var i = 1; i < array.Length; i++)
            {
                var v = selector(array[i]);
                if (v >= result) continue;
                result = v;
            }
            return result;
        }

        /// <summary>Returns the maximum value by an int selector.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static int Max<T>(this T[] array, Func<T, int> selector)
        {
            var result = selector(array[0]);
            for (var i = 1; i < array.Length; i++)
            {
                var v = selector(array[i]);
                if (v <= result) continue;
                result = v;
            }
            return result;
        }

        /// <summary>Returns the maximum value by a float selector.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty arrays.</remarks>
        [Inline]
        public static float Max<T>(this T[] array, Func<T, float> selector)
        {
            var result = selector(array[0]);
            for (var i = 1; i < array.Length; i++)
            {
                var v = selector(array[i]);
                if (v <= result) continue;
                result = v;
            }
            return result;
        }

        /// <summary>Computes the sum by an int selector.</summary>
        [Inline]
        public static int Sum<T>(this T[] array, Func<T, int> selector)
        {
            var result = 0;
            foreach (var t in array)
                result += selector(t);
            return result;
        }

        /// <summary>Computes the sum by a float selector.</summary>
        [Inline]
        public static float Sum<T>(this T[] array, Func<T, float> selector)
        {
            var result = 0f;
            foreach (var t in array)
                result += selector(t);
            return result;
        }

        /// <summary>Computes the average by an int selector.</summary>
        /// <remarks>Throws <c>DivideByZeroException</c> on empty arrays.</remarks>
        [Inline]
        public static float Average<T>(this T[] array, Func<T, int> selector)
        {
            var sum = 0;
            foreach (var t in array)
                sum += selector(t);
            var result = (float)sum / array.Length;
            return result;
        }

        /// <summary>Computes the average by a float selector.</summary>
        /// <remarks>Throws <c>DivideByZeroException</c> on empty arrays.</remarks>
        [Inline]
        public static float Average<T>(this T[] array, Func<T, float> selector)
        {
            var sum = 0f;
            foreach (var t in array)
                sum += selector(t);
            var result = sum / array.Length;
            return result;
        }

        // --- Set operations ---

        /// <summary>Produces the set union of two arrays.</summary>
        /// <remarks>Uses O(n*m) comparison via <c>.Equals()</c>.</remarks>
        [Inline]
        public static T[] Union<T>(this T[] array, T[] other)
        {
            var temp = new T[array.Length + other.Length];
            var count = 0;
            foreach (var t in array)
            {
                var found = false;
                for (var j = 0; j < count; j++)
                {
                    if (!temp[j].Equals(t)) continue;
                    found = true;
                    break;
                }
                if (found) continue;
                temp[count] = t;
                count++;
            }
            foreach (var t in other)
            {
                var found = false;
                for (var j = 0; j < count; j++)
                {
                    if (!temp[j].Equals(t)) continue;
                    found = true;
                    break;
                }
                if (found) continue;
                temp[count] = t;
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        /// <summary>Produces the set intersection of two arrays.</summary>
        /// <remarks>Uses O(n*m) comparison via <c>.Equals()</c>.</remarks>
        [Inline]
        public static T[] Intersect<T>(this T[] array, T[] other)
        {
            var temp = new T[array.Length];
            var count = 0;
            foreach (var t in array)
            {
                var inOther = false;
                foreach (var u in other)
                {
                    if (!t.Equals(u)) continue;
                    inOther = true;
                    break;
                }
                if (!inOther) continue;
                var dup = false;
                for (var j = 0; j < count; j++)
                {
                    if (!temp[j].Equals(t)) continue;
                    dup = true;
                    break;
                }
                if (dup) continue;
                temp[count] = t;
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        /// <summary>Produces the set difference of two arrays.</summary>
        /// <remarks>Uses O(n*m) comparison via <c>.Equals()</c>.</remarks>
        [Inline]
        public static T[] Except<T>(this T[] array, T[] other)
        {
            var temp = new T[array.Length];
            var count = 0;
            foreach (var t in array)
            {
                var inOther = false;
                foreach (var u in other)
                {
                    if (!t.Equals(u)) continue;
                    inOther = true;
                    break;
                }
                if (inOther) continue;
                var dup = false;
                for (var j = 0; j < count; j++)
                {
                    if (!temp[j].Equals(t)) continue;
                    dup = true;
                    break;
                }
                if (dup) continue;
                temp[count] = t;
                count++;
            }
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = temp[i];
            return result;
        }

        // --- Sorting ---

        /// <summary>Sorts by an int key in ascending order (stable insertion sort).</summary>
        [Inline]
        public static T[] OrderBy<T>(this T[] array, Func<T, int> keySelector)
        {
            var result = new T[array.Length];
            var keys = new int[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                result[i] = array[i];
                keys[i] = keySelector(array[i]);
            }
            for (var i = 1; i < result.Length; i++)
            {
                var key = keys[i];
                var val = result[i];
                var j = i - 1;
                for (; j >= 0 && keys[j] > key; j--)
                {
                    keys[j + 1] = keys[j];
                    result[j + 1] = result[j];
                }
                keys[j + 1] = key;
                result[j + 1] = val;
            }
            return result;
        }

        /// <summary>Sorts by a float key in ascending order (stable insertion sort).</summary>
        [Inline]
        public static T[] OrderBy<T>(this T[] array, Func<T, float> keySelector)
        {
            var result = new T[array.Length];
            var keys = new float[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                result[i] = array[i];
                keys[i] = keySelector(array[i]);
            }
            for (var i = 1; i < result.Length; i++)
            {
                var key = keys[i];
                var val = result[i];
                var j = i - 1;
                for (; j >= 0 && keys[j] > key; j--)
                {
                    keys[j + 1] = keys[j];
                    result[j + 1] = result[j];
                }
                keys[j + 1] = key;
                result[j + 1] = val;
            }
            return result;
        }

        /// <summary>Sorts by an int key in descending order (stable insertion sort).</summary>
        [Inline]
        public static T[] OrderByDescending<T>(this T[] array, Func<T, int> keySelector)
        {
            var result = new T[array.Length];
            var keys = new int[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                result[i] = array[i];
                keys[i] = keySelector(array[i]);
            }
            for (var i = 1; i < result.Length; i++)
            {
                var key = keys[i];
                var val = result[i];
                var j = i - 1;
                for (; j >= 0 && keys[j] < key; j--)
                {
                    keys[j + 1] = keys[j];
                    result[j + 1] = result[j];
                }
                keys[j + 1] = key;
                result[j + 1] = val;
            }
            return result;
        }

        /// <summary>Sorts by a float key in descending order (stable insertion sort).</summary>
        [Inline]
        public static T[] OrderByDescending<T>(this T[] array, Func<T, float> keySelector)
        {
            var result = new T[array.Length];
            var keys = new float[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                result[i] = array[i];
                keys[i] = keySelector(array[i]);
            }
            for (var i = 1; i < result.Length; i++)
            {
                var key = keys[i];
                var val = result[i];
                var j = i - 1;
                for (; j >= 0 && keys[j] < key; j--)
                {
                    keys[j + 1] = keys[j];
                    result[j + 1] = result[j];
                }
                keys[j + 1] = key;
                result[j + 1] = val;
            }
            return result;
        }
    }

    public static class DataListExtensions
    {
        /// <summary>Executes an action for each element.</summary>
        [Inline]
        public static void ForEach(this DataList list, Action<DataToken> action)
        {
            for (var __i = 0; __i < list.Count; __i++)
                action(list[__i]);
        }

        /// <summary>Filters elements by a predicate.</summary>
        [Inline]
        public static DataList Where(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = new DataList();
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) continue;
                result.Add(t);
            }
            return result;
        }

        /// <summary>Determines whether any element satisfies a condition.</summary>
        [Inline]
        public static bool Any(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = false;
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) continue;
                result = true;
                break;
            }
            return result;
        }

        /// <summary>Determines whether all elements satisfy a condition.</summary>
        [Inline]
        public static bool All(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = true;
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (predicate(t)) continue;
                result = false;
                break;
            }
            return result;
        }

        /// <summary>Counts elements that satisfy a condition.</summary>
        [Inline]
        public static int Count(this DataList list, Func<DataToken, bool> predicate)
        {
            var count = 0;
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) continue;
                count++;
            }
            return count;
        }

        /// <summary>Returns the first element that satisfies a condition, or <c>default</c>.</summary>
        [Inline]
        public static DataToken FirstOrDefault(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = default(DataToken);
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) continue;
                result = t;
                break;
            }
            return result;
        }

        // --- Lambda-based operators (added) ---

        /// <summary>Projects each element into a new form.</summary>
        [Inline]
        public static DataList Select(this DataList list, Func<DataToken, DataToken> func)
        {
            var result = new DataList();
            for (var __i = 0; __i < list.Count; __i++)
                result.Add(func(list[__i]));
            return result;
        }

        /// <summary>Returns the first element that satisfies a condition.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if no element matches.</remarks>
        [Inline]
        public static DataToken First(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = default(DataToken);
            var found = false;
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) continue;
                result = t;
                found = true;
                break;
            }
            if (!found) result = list[list.Count];
            return result;
        }

        /// <summary>Returns the last element that satisfies a condition.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if no element matches.</remarks>
        [Inline]
        public static DataToken Last(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = default(DataToken);
            var found = false;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (!predicate(list[i])) continue;
                result = list[i];
                found = true;
                break;
            }
            if (!found) result = list[list.Count];
            return result;
        }

        /// <summary>Returns the last element that satisfies a condition, or <c>default</c>.</summary>
        [Inline]
        public static DataToken LastOrDefault(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = default(DataToken);
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (!predicate(list[i])) continue;
                result = list[i];
                break;
            }
            return result;
        }

        /// <summary>Returns the single element that satisfies a condition.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if no match or more than one match.</remarks>
        [Inline]
        public static DataToken Single(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = default(DataToken);
            var count = 0;
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) continue;
                result = t;
                count++;
            }
            if (count != 1) result = list[list.Count];
            return result;
        }

        /// <summary>Returns the single element that satisfies a condition, or <c>default</c>.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if more than one match.</remarks>
        [Inline]
        public static DataToken SingleOrDefault(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = default(DataToken);
            var count = 0;
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) continue;
                result = t;
                count++;
            }
            if (count > 1) result = list[list.Count];
            return result;
        }

        /// <summary>Applies an accumulator function with a seed value.</summary>
        [Inline]
        public static DataToken Aggregate(this DataList list, DataToken seed, Func<DataToken, DataToken, DataToken> func)
        {
            var result = seed;
            for (var __i = 0; __i < list.Count; __i++)
                result = func(result, list[__i]);
            return result;
        }

        /// <summary>Returns elements while the predicate is true.</summary>
        [Inline]
        public static DataList TakeWhile(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = new DataList();
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (!predicate(t)) break;
                result.Add(t);
            }
            return result;
        }

        /// <summary>Skips elements while the predicate is true, then returns the rest.</summary>
        [Inline]
        public static DataList SkipWhile(this DataList list, Func<DataToken, bool> predicate)
        {
            var result = new DataList();
            var skipping = true;
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t = list[__i];
                if (skipping && predicate(t)) continue;
                skipping = false;
                result.Add(t);
            }
            return result;
        }

        // --- Non-lambda operators (added) ---

        /// <summary>Determines whether the list contains any elements.</summary>
        [Inline]
        public static bool Any(this DataList list)
        {
            var result = list.Count > 0;
            return result;
        }

        /// <summary>Returns the first element.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty lists.</remarks>
        [Inline]
        public static DataToken First(this DataList list)
        {
            var result = list[0];
            return result;
        }

        /// <summary>Returns the first element, or <c>default</c> if empty.</summary>
        [Inline]
        public static DataToken FirstOrDefault(this DataList list)
        {
            var result = default(DataToken);
            if (list.Count > 0) result = list[0];
            return result;
        }

        /// <summary>Returns the last element.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> on empty lists.</remarks>
        [Inline]
        public static DataToken Last(this DataList list)
        {
            var result = list[list.Count - 1];
            return result;
        }

        /// <summary>Returns the last element, or <c>default</c> if empty.</summary>
        [Inline]
        public static DataToken LastOrDefault(this DataList list)
        {
            var result = default(DataToken);
            if (list.Count > 0) result = list[list.Count - 1];
            return result;
        }

        /// <summary>Returns the single element of the list.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if empty or more than one element.</remarks>
        [Inline]
        public static DataToken Single(this DataList list)
        {
            if (list.Count > 1) { var e = list[list.Count]; }
            var result = list[0];
            return result;
        }

        /// <summary>Returns the single element, or <c>default</c> if empty.</summary>
        /// <remarks>Throws <c>IndexOutOfRangeException</c> if more than one element.</remarks>
        [Inline]
        public static DataToken SingleOrDefault(this DataList list)
        {
            if (list.Count > 1) { var e = list[list.Count]; }
            var result = default(DataToken);
            if (list.Count > 0) result = list[0];
            return result;
        }

        /// <summary>Returns the first <paramref name="count"/> elements.</summary>
        [Inline]
        public static DataList Take(this DataList list, int count)
        {
            var result = new DataList();
            for (var i = 0; i < count; i++)
                result.Add(list[i]);
            return result;
        }

        /// <summary>Skips the first <paramref name="count"/> elements.</summary>
        [Inline]
        public static DataList Skip(this DataList list, int count)
        {
            var result = new DataList();
            for (var i = count; i < list.Count; i++)
                result.Add(list[i]);
            return result;
        }

        /// <summary>Concatenates two lists.</summary>
        [Inline]
        public static DataList Concat(this DataList list, DataList other)
        {
            var result = new DataList();
            for (var __i = 0; __i < list.Count; __i++)
                result.Add(list[__i]);
            for (var __i = 0; __i < other.Count; __i++)
                result.Add(other[__i]);
            return result;
        }

        /// <summary>Adds an element to the end.</summary>
        [Inline]
        public static DataList Append(this DataList list, DataToken value)
        {
            var result = new DataList();
            for (var __i = 0; __i < list.Count; __i++)
                result.Add(list[__i]);
            result.Add(value);
            return result;
        }

        /// <summary>Adds an element to the beginning.</summary>
        [Inline]
        public static DataList Prepend(this DataList list, DataToken value)
        {
            var result = new DataList();
            result.Add(value);
            for (var __i = 0; __i < list.Count; __i++)
                result.Add(list[__i]);
            return result;
        }

        /// <summary>Returns distinct elements.</summary>
        [Inline]
        public static DataList Distinct(this DataList list)
        {
            var result = new DataList();
            for (var __i = 0; __i < list.Count; __i++)
            {
                var t1 = list[__i];
                var found = false;
                for (var __j = 0; __j < result.Count; __j++)
                {
                    if (!t1.Equals(result[__j])) continue;
                    found = true;
                    break;
                }
                if (found) continue;
                result.Add(t1);
            }
            return result;
        }

        /// <summary>Determines whether two lists are equal element-by-element.</summary>
        [Inline]
        public static bool SequenceEqual(this DataList list, DataList other)
        {
            var result = true;
            if (list.Count != other.Count)
            {
                result = false;
            }
            else
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].Equals(other[i])) continue;
                    result = false;
                    break;
                }
            }
            return result;
        }
    }
}
