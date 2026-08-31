// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.Helpers;

namespace Models.Tests
{
    [TestClass]
    public class CanonicalMemberOrderTests
    {
        /// <summary>
        /// Pairs that must sort in the order given. Each isolates one field as the first point of
        /// difference and straddles its signed/unsigned boundary.
        /// </summary>
        private static readonly (string Smaller, string Larger, string Reason)[] _boundaryPairs =
        {
            ("7fffffff-0000-0000-0000-000000000000",
             "80000000-0000-0000-0000-000000000000",
             "first field, signed/unsigned rollover"),

            ("00000000-7fff-0000-0000-000000000000",
             "00000000-8000-0000-0000-000000000000",
             "second field, signed/unsigned rollover"),

            ("00000000-0000-7fff-0000-000000000000",
             "00000000-0000-8000-0000-000000000000",
             "third field, signed/unsigned rollover"),

            ("00000000-0000-0000-7f00-000000000000",
             "00000000-0000-0000-8000-000000000000",
             "first trailing byte, signed/unsigned rollover"),

            ("00000000-0000-0000-0000-00000000007f",
             "00000000-0000-0000-0000-000000000080",
             "last trailing byte, signed/unsigned rollover"),

            ("00000000-0000-0000-0000-000000000000",
             "00000000-0000-0000-0000-000000000001",
             "empty precedes every other identifier"),

            ("fffffffe-ffff-ffff-ffff-ffffffffffff",
             "ffffffff-ffff-ffff-ffff-ffffffffffff",
             "all bits set is the greatest identifier"),

            ("00000001-0000-0000-0000-000000000000",
             "00000002-0000-0000-0000-000000000000",
             "first field decides before any later field"),

            ("00000000-0001-0000-0000-000000000000",
             "00000000-0002-0000-0000-000000000000",
             "second field decides once the first is equal"),
        };

        private static Guid ParseGuid(string value) => Guid.Parse(value);

        private static int CanonicalByString(Guid x, Guid y) =>
            Math.Sign(string.CompareOrdinal(x.ToString("D"), y.ToString("D")));

        private static int Canonical(Guid x, Guid y) =>
            Math.Sign(CanonicalObjectIdComparer.Instance.Compare(x, y));

        [TestMethod]
        public void BoundaryPairsOrderAsSpecified()
        {
            foreach (var (smaller, larger, reason) in _boundaryPairs)
            {
                Assert.AreEqual(-1, Canonical(ParseGuid(smaller), ParseGuid(larger)), $"expected {smaller} < {larger}: {reason}");
                Assert.AreEqual(1, Canonical(ParseGuid(larger), ParseGuid(smaller)), $"expected {larger} > {smaller}: {reason}");
            }
        }

        [TestMethod]
        public void ComparerIsReflexive()
        {
            foreach (var (smaller, larger, _) in _boundaryPairs)
            {
                Assert.AreEqual(0, Canonical(ParseGuid(smaller), ParseGuid(smaller)));
                Assert.AreEqual(0, Canonical(ParseGuid(larger), ParseGuid(larger)));
            }
        }

        [TestMethod]
        public void ComparerIsAntisymmetric()
        {
            var boundaryValues = AllBoundaryValues();

            foreach (var x in boundaryValues)
            {
                foreach (var y in boundaryValues)
                {
                    Assert.AreEqual(Canonical(x, y), -Canonical(y, x), $"antisymmetry broken for {x} and {y}");
                }
            }
        }

        [TestMethod]
        public void ComparerIsTransitive()
        {
            var boundaryValues = AllBoundaryValues();

            foreach (var x in boundaryValues)
            {
                foreach (var y in boundaryValues)
                {
                    foreach (var z in boundaryValues)
                    {
                        if (Canonical(x, y) <= 0 && Canonical(y, z) <= 0)
                        {
                            Assert.IsTrue(Canonical(x, z) <= 0, $"transitivity broken for {x}, {y}, {z}");
                        }
                    }
                }
            }
        }

        // If these fail, Guid.CompareTo no longer matches the specified string order. That is a runtime
        // contract break, not a test to relax.

        [TestMethod]
        public void ImplementationMatchesTheWrittenSpecificationOnBoundaryValues()
        {
            var boundaryValues = AllBoundaryValues();

            foreach (var x in boundaryValues)
            {
                foreach (var y in boundaryValues)
                {
                    Assert.AreEqual(
                        CanonicalByString(x, y),
                        Canonical(x, y),
                        $"Guid.CompareTo disagreed with the lowercase \"D\" string order for {x} and {y}. " +
                        "This is a runtime contract break, not a test to relax.");
                }
            }
        }

        [TestMethod]
        public void ImplementationMatchesTheWrittenSpecificationOnValuesSharingLongPrefixes()
        {
            // Random identifiers are decided by the first field almost every time. A shared prefix
            // pushes the decision into each later field in turn.
            var random = new Random(20260820);
            var template = new byte[16];

            for (var sharedBytes = 0; sharedBytes < 16; sharedBytes++)
            {
                for (var iteration = 0; iteration < 250; iteration++)
                {
                    random.NextBytes(template);

                    var left = (byte[])template.Clone();
                    var right = (byte[])template.Clone();

                    for (var i = sharedBytes; i < 16; i++)
                    {
                        left[i] = (byte)random.Next(256);
                        right[i] = (byte)random.Next(256);
                    }

                    var x = new Guid(left);
                    var y = new Guid(right);

                    Assert.AreEqual(
                        CanonicalByString(x, y),
                        Canonical(x, y),
                        $"Guid.CompareTo disagreed with the lowercase \"D\" string order for {x} and {y}.");
                }
            }
        }

        [TestMethod]
        public void SortingAgreesWithTheWrittenSpecification()
        {
            var random = new Random(20260821);
            var buffer = new byte[16];
            var values = new List<Guid>();

            for (var i = 0; i < 2000; i++)
            {
                random.NextBytes(buffer);
                values.Add(new Guid(buffer));
            }

            var byImplementation = values.OrderBy(v => v, CanonicalObjectIdComparer.Instance).ToList();
            var bySpecification = values.OrderBy(v => v.ToString("D"), StringComparer.Ordinal).ToList();

            CollectionAssert.AreEqual(bySpecification, byImplementation);
        }

        [TestMethod]
        public void GuidRendersLowercaseSoOrdinalStringComparisonIsWellDefined()
        {
            // The specification depends on the rendering being lowercase: ordinal comparison would not
            // agree with the field order otherwise.
            var value = Guid.Parse("F0E1D2C3-B4A5-9687-7869-5A4B3C2D1E0F");

            Assert.AreEqual("f0e1d2c3-b4a5-9687-7869-5a4b3c2d1e0f", value.ToString("D"));
        }

        [TestMethod]
        public void RawByteLayoutIsMixedEndianAndDoesNotMatchTheRenderedOrder()
        {
            var value = Guid.Parse("f0e1d2c3-b4a5-9687-7869-5a4b3c2d1e0f");
            var bytes = value.ToByteArray();

            // The first field is emitted little endian, reversed relative to how it is rendered.
            Assert.AreEqual(0xc3, bytes[0]);
            Assert.AreEqual(0xd2, bytes[1]);
            Assert.AreEqual(0xe1, bytes[2]);
            Assert.AreEqual(0xf0, bytes[3]);
        }

        [TestMethod]
        public void SortingByRawBytesProducesADifferentOrderThanTheCanonicalComparer()
        {
            // Proves the two orders are not interchangeable, so substituting raw bytes cannot pass
            // unnoticed.
            var lower = Guid.Parse("00000001-0000-0000-0000-000000000000");
            var higher = Guid.Parse("00000100-0000-0000-0000-000000000000");

            Assert.AreEqual(-1, Canonical(lower, higher), "canonical order is decided by the rendered field");

            var byRawBytes = CompareByRawBytes(lower, higher);

            Assert.AreEqual(1, byRawBytes, "raw byte order reverses this pair, which is exactly why it is banned");
        }

        [TestMethod]
        public void MemberComparerOrdersByObjectId()
        {
            var first = new AzureADUser { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000001") };
            var second = new AzureADUser { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000002") };

            Assert.AreEqual(-1, Math.Sign(CanonicalMemberComparer<AzureADUser>.Instance.Compare(first, second)));
            Assert.AreEqual(1, Math.Sign(CanonicalMemberComparer<AzureADUser>.Instance.Compare(second, first)));
            Assert.AreEqual(0, CanonicalMemberComparer<AzureADUser>.Instance.Compare(first, first));
        }

        [TestMethod]
        public void MemberComparerAgreesWithTheGuidComparerForEveryBoundaryValue()
        {
            var boundaryValues = AllBoundaryValues();

            foreach (var x in boundaryValues)
            {
                foreach (var y in boundaryValues)
                {
                    var asMembers = Math.Sign(CanonicalMemberComparer<AzureADUser>.Instance.Compare(
                        new AzureADUser { ObjectId = x },
                        new AzureADUser { ObjectId = y }));

                    Assert.AreEqual(Canonical(x, y), asMembers);
                }
            }
        }

        [TestMethod]
        public void MemberComparerSortsNullsFirstWithoutThrowing()
        {
            var member = new AzureADUser { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000001") };

            Assert.AreEqual(-1, Math.Sign(CanonicalMemberComparer<AzureADUser>.Instance.Compare(null, member)));
            Assert.AreEqual(1, Math.Sign(CanonicalMemberComparer<AzureADUser>.Instance.Compare(member, null)));
            Assert.AreEqual(0, CanonicalMemberComparer<AzureADUser>.Instance.Compare(null, null));
        }

        [TestMethod]
        public void MemberComparerOrdersDerivedTeamsUsers()
        {
            // AzureADTeamsUser derives from AzureADUser, so the same comparer covers it. If that
            // relationship is ever broken, this stops compiling.
            var first = new AzureADTeamsUser { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000001") };
            var second = new AzureADTeamsUser { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000002") };

            var ordered = new List<AzureADUser> { second, first }
                .OrderBy(u => u, CanonicalMemberComparer<AzureADUser>.Instance)
                .ToList();

            Assert.AreSame(first, ordered[0]);
            Assert.AreSame(second, ordered[1]);
        }

        // Sites pick the operator by collection ownership: in place when local, OrderBy when the
        // collection belongs to a caller. Stability is deliberately not a guarantee.

        [TestMethod]
        public void InPlaceListSortProducesCanonicalOrder()
        {
            var random = new Random(20260822);
            var buffer = new byte[16];

            var members = new List<AzureADUser>();
            for (var i = 0; i < 500; i++)
            {
                random.NextBytes(buffer);
                members.Add(new AzureADUser { ObjectId = new Guid(buffer) });
            }

            var expected = members.Select(m => m.ObjectId).OrderBy(g => g.ToString("D"), StringComparer.Ordinal).ToList();

            members.Sort(CanonicalMemberComparer<AzureADUser>.Instance);

            CollectionAssert.AreEqual(expected, members.Select(m => m.ObjectId).ToList());
        }

        [TestMethod]
        public void InPlaceArraySortProducesCanonicalOrder()
        {
            var random = new Random(20260823);
            var buffer = new byte[16];

            var ids = new Guid[500];
            for (var i = 0; i < ids.Length; i++)
            {
                random.NextBytes(buffer);
                ids[i] = new Guid(buffer);
            }

            var expected = ids.OrderBy(g => g.ToString("D"), StringComparer.Ordinal).ToArray();

            Array.Sort(ids, CanonicalObjectIdComparer.Instance);

            CollectionAssert.AreEqual(expected, ids);
        }

        [TestMethod]
        public void MemberComparerSortsAListOfDerivedTeamsUsersInPlace()
        {
            // The generic comparer is what makes this compile: List<AzureADTeamsUser>.Sort requires
            // IComparer<AzureADTeamsUser>, which IComparer<AzureADUser> does not satisfy.
            var first = new AzureADTeamsUser { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000001") };
            var second = new AzureADTeamsUser { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000002") };

            var users = new List<AzureADTeamsUser> { second, first };
            users.Sort(CanonicalMemberComparer<AzureADTeamsUser>.Instance);

            Assert.AreSame(first, users[0]);
            Assert.AreSame(second, users[1]);
        }

        [TestMethod]
        public void MemberComparerOrdersGroupsByObjectId()
        {
            // AzureADGroup is the other IAzureADObject implementer, so the generic comparer covers it
            // without a second type.
            var first = new AzureADGroup { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000001") };
            var second = new AzureADGroup { ObjectId = ParseGuid("00000000-0000-0000-0000-000000000002") };

            Assert.AreEqual(-1, Math.Sign(CanonicalMemberComparer<AzureADGroup>.Instance.Compare(first, second)));
        }

        private static List<Guid> AllBoundaryValues()
        {
            var values = new List<Guid>();

            foreach (var (smaller, larger, _) in _boundaryPairs)
            {
                values.Add(ParseGuid(smaller));
                values.Add(ParseGuid(larger));
            }

            return values.Distinct().ToList();
        }

        private static int CompareByRawBytes(Guid x, Guid y)
        {
            var left = x.ToByteArray();
            var right = y.ToByteArray();

            for (var i = 0; i < left.Length; i++)
            {
                var result = left[i].CompareTo(right[i]);
                if (result != 0) return Math.Sign(result);
            }

            return 0;
        }
    }
}
