using System;
using System.Collections.Generic;
using FstLib.Core;

namespace FstLib.Lookup
{
    /// <summary>
    /// Pattern-based lookups using FST arc traversal.
    /// 
    /// Efficiently finds all keys matching a pattern by:
    /// 1. Traversing the FST following pattern characters
    /// 2. Enumerating descendants from the matched node
    /// 
    /// Complexity: O(m + k) where m = pattern length, k = results
    /// </summary>
    internal sealed partial class FstLookup
    {
        /// <summary>
        /// Enumerates all keys that start with the given pattern (word*).
        /// Uses arc traversal: O(m + k) instead of O(n) full enumeration.
        /// </summary>
        internal IEnumerable<(string Key, long Value)> EnumerateStartsWith(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (pattern.Length == 0) throw new ArgumentException("Pattern cannot be empty");

            long? nodeAddr = TraversePattern(pattern);
            if (nodeAddr == null) yield break;

            var pathLabels = new List<int>();
            foreach (var label in EncodeKey(pattern))
                pathLabels.Add(label);

            foreach (var result in EnumerateDescendants(nodeAddr.Value, pathLabels, 0))
                yield return result;
        }

        /// <summary>
        /// Enumerates all keys that end with the given pattern (*word).
        /// Uses reverse FST for efficiency if available, otherwise uses efficient traversal.
        /// </summary>
        internal IEnumerable<(string Key, long Value)> EnumerateEndsWith(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (pattern.Length == 0) throw new ArgumentException("Pattern cannot be empty");

            if (_reverseLookup == null)
            {
                // If this IS the reverse FST (no _reverseLookup), we cannot efficiently find keys ending with a pattern
                // because we don't have a reverse-of-reverse FST. Fall back to full enumeration.
                foreach (var (key, value) in EnumerateAll())
                {
                    if (key.EndsWith(pattern))
                        yield return (key, value);
                }
                yield break;
            }

            string reversedPattern = ReverseString(pattern);
            long? nodeAddr = _reverseLookup.TraversePattern(reversedPattern);
            if (nodeAddr == null) yield break;

            var pathLabels = new List<int>();
            foreach (var label in _reverseLookup.EncodeKey(reversedPattern))
                pathLabels.Add(label);

            foreach (var (key, value) in _reverseLookup.EnumerateDescendants(nodeAddr.Value, pathLabels, 0))
                yield return (ReverseString(key), value);
        }

        /// <summary>
        /// Enumerates all keys that contain the given pattern (*word*).
        /// 
        /// Enumerates all keys and filters those containing the pattern.
        /// While this is O(n), it's necessary for substring matching since FSTs
        /// are optimized for prefix/suffix queries, not arbitrary substring matching.
        /// 
        /// Complexity: O(n) where n = total keys
        /// </summary>
        internal IEnumerable<(string Key, long Value)> EnumerateContains(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (pattern.Length == 0) throw new ArgumentException("Pattern cannot be empty");

            // Enumerate all keys and filter those containing the pattern
            foreach (var (key, value) in EnumerateAll())
            {
                if (key.Contains(pattern))
                    yield return (key, value);
            }
        }

        private static string ReverseString(string s)
        {
            var chars = s.ToCharArray();
            System.Array.Reverse(chars);
            return new string(chars);
        }
    }
}
