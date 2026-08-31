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
}
