// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Models.Helpers
{
    /// <summary>
    /// Orders identifiers as their lowercase 36-character "D" strings compare ordinally.
    /// </summary>
    /// <remarks>
    /// Implemented with <see cref="Guid.CompareTo(Guid)"/>, which yields that order without allocating.
    /// Never substitute <see cref="Guid.ToByteArray()"/> ordering: the first three fields are
    /// little endian there, so it produces a different sequence.
    /// </remarks>
    public sealed class CanonicalObjectIdComparer : IComparer<Guid>
    {
        public static readonly CanonicalObjectIdComparer Instance = new CanonicalObjectIdComparer();

        private CanonicalObjectIdComparer()
        {
        }

        public int Compare(Guid x, Guid y) => x.CompareTo(y);
    }

    /// <summary>
    /// Orders any <see cref="IAzureADObject"/> by <see cref="IAzureADObject.ObjectId"/>, nulls first.
    /// </summary>
    /// <typeparam name="T">The member type, for example <see cref="AzureADUser"/>.</typeparam>
    public sealed class CanonicalMemberComparer<T> : IComparer<T> where T : class, IAzureADObject
    {
        public static readonly CanonicalMemberComparer<T> Instance = new CanonicalMemberComparer<T>();

        private CanonicalMemberComparer()
        {
        }

        public int Compare(T x, T y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return x.ObjectId.CompareTo(y.ObjectId);
        }
    }

    /// <summary>
    /// Sorts member lists by <see cref="CanonicalMemberComparer{T}"/>.
    /// </summary>
    public static class CanonicalMemberSort
    {
        /// <summary>
        /// Sorts <paramref name="members"/> in place in canonical order. The sort is stable: members with equal
        /// identifiers keep their relative order, so "first occurrence wins" deduplication downstream keeps the same
        /// record it kept before the sort. A null list, or one already in order, is left unchanged.
        /// </summary>
        public static void SortStable<T>(List<T> members) where T : class, IAzureADObject
        {
            if (members == null || members.Count < 2)
            {
                return;
            }

            var comparer = CanonicalMemberComparer<T>.Instance;
            var sorted = true;
            for (var i = 1; i < members.Count; i++)
            {
                if (comparer.Compare(members[i - 1], members[i]) > 0)
                {
                    sorted = false;
                    break;
                }
            }

            if (sorted)
            {
                return;
            }

            // Order by (member, original index): a total order, so the unstable Array.Sort is stable here.
            var indexed = new KeyValuePair<T, int>[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                indexed[i] = new KeyValuePair<T, int>(members[i], i);
            }

            Array.Sort(indexed, (x, y) =>
            {
                var byMember = comparer.Compare(x.Key, y.Key);
                return byMember != 0 ? byMember : x.Value.CompareTo(y.Value);
            });

            for (var i = 0; i < indexed.Length; i++)
            {
                members[i] = indexed[i].Key;
            }
        }
    }
}
