// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Models.Tests
{
    /// <summary>
    /// Tests which identityTypes values are accepted or rejected, and how the list is read from a query part.
    /// </summary>
    [TestClass]
    public sealed class IdentityTypeSelectionTests
    {
        private static readonly (string[] TypeNames, bool IncludesUsers, bool IncludesAgents)[] _supportedSets =
        {
            (new[] { "users" }, true, false),
            (new[] { "agents" }, false, true),
            (new[] { "users", "agents" }, true, true),
            (new[] { "agents", "users" }, true, true),
        };

        private static readonly string[][] _invalidSets =
        {
            Array.Empty<string>(),
            new[] { "users", "users" },
            new[] { "agents", "agents" },
            new[] { "Users" },
            new[] { "AGENTS" },
            new[] { "devices" },
            new[] { "users", "devices" },
            new[] { "users", string.Empty },
            new[] { "users", null! },
        };

        [TestMethod]
        public void IdentityTypes_AcceptsSupportedSets()
        {
            foreach (var (typeNames, includesUsers, includesAgents) in _supportedSets)
            {
                var resolved = IdentityTypeSet.TryResolve(
                    typeNames,
                    IdentityTypeSet.UsersOnly,
                    out var selection,
                    out var error);

                Assert.IsTrue(resolved, error);
                Assert.IsNull(error);
                Assert.AreEqual(includesUsers, selection.Includes(IdentityType.Users));
                Assert.AreEqual(includesAgents, selection.Includes(IdentityType.Agents));
            }
        }

        [TestMethod]
        public void IdentityTypes_AcceptsBothOrdersAsTheSameSet()
        {
            Assert.IsTrue(IdentityTypeSet.TryResolve(
                new[] { "users", "agents" },
                IdentityTypeSet.UsersOnly,
                out var usersThenAgents,
                out _));
            Assert.IsTrue(IdentityTypeSet.TryResolve(
                new[] { "agents", "users" },
                IdentityTypeSet.UsersOnly,
                out var agentsThenUsers,
                out _));

            Assert.AreEqual(usersThenAgents, agentsThenUsers);
            CollectionAssert.AreEqual(
                new[] { "users", "agents" },
                usersThenAgents.ToTypeNames().ToArray());
        }

        [TestMethod]
        public void IdentityTypes_OmissionUsesTheCallerDefault()
        {
            Assert.IsTrue(IdentityTypeSet.TryResolve(
                null,
                IdentityTypeSet.UsersOnly,
                out var usersDefault,
                out var usersError));
            Assert.IsTrue(IdentityTypeSet.TryResolve(
                null,
                IdentityTypeSet.Of(IdentityType.Agents),
                out var agentsDefault,
                out var agentsError));

            Assert.IsNull(usersError);
            Assert.IsNull(agentsError);
            Assert.AreEqual(IdentityTypeSet.UsersOnly, usersDefault);
            Assert.IsFalse(agentsDefault.Includes(IdentityType.Users));
            Assert.IsTrue(agentsDefault.Includes(IdentityType.Agents));
        }

        [TestMethod]
        public void IdentityTypes_RejectsInvalidSets()
        {
            foreach (var typeNames in _invalidSets)
            {
                var resolved = IdentityTypeSet.TryResolve(
                    typeNames,
                    IdentityTypeSet.UsersOnly,
                    out var selection,
                    out var error);

                Assert.IsFalse(resolved);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
                Assert.AreEqual(default, selection);
            }
        }

        [TestMethod]
        public void IdentityTypes_EveryDeclaredTypeHasAUniqueLowercaseName()
        {
            var declaredTypes = Enum.GetValues<IdentityType>();

            CollectionAssert.AreEquivalent(
                declaredTypes,
                IdentityTypeSet.SupportedTypes.ToArray());
            Assert.AreEqual(
                IdentityTypeSet.SupportedTypeNames.Count,
                IdentityTypeSet.SupportedTypeNames.Distinct(StringComparer.Ordinal).Count());

            foreach (var typeName in IdentityTypeSet.SupportedTypeNames)
            {
                Assert.AreEqual(typeName.ToLowerInvariant(), typeName);
            }

            foreach (var identityType in declaredTypes)
            {
                Assert.IsTrue(IdentityTypeSet.All.Includes(identityType));
            }
        }

        [TestMethod]
        public void IdentityTypes_RejectsUnknownEnumValues()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => IdentityTypeSet.Of((IdentityType)999));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => IdentityTypeSet.UsersOnly.Includes((IdentityType)999));
        }

        [TestMethod]
        public void IdentityTypes_KeyWithDifferentLetterCaseIsRefusedAtAnyDepth()
        {
            var queryParts = new[]
            {
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"IdentityTypes":["agents"]}""",
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"IDENTITYTYPES":["users"]}""",
                """{"type":"SqlMembership","source":{"manager":{"id":1},"identitytypes":["agents"]}}""",
                """{"type":"SqlMembership","source":{"rules":[{"IdentityTypes":["agents"]}]}}""",
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["users"],"IdentityTypes":["agents"]}""",
            };

            foreach (var queryPart in queryParts)
            {
                var parsed = IdentityTypeSelection.TryParse(
                    queryPart,
                    IdentityTypeSet.UsersOnly,
                    out var selection,
                    out var error);

                Assert.IsFalse(parsed, queryPart);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error), queryPart);
                Assert.AreEqual(default, selection, queryPart);
                Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(queryPart), queryPart);

                using var document = JsonDocument.Parse(queryPart);
                Assert.IsTrue(IdentityTypeSelection.IsMisplaced(document.RootElement), queryPart);
            }
        }

        [TestMethod]
        public void IdentityTypes_SimilarlyNamedKeysAreNotTreatedAsASelection()
        {
            var queryPart =
                """{"type":"SqlMembership","source":{"manager":{"id":1},"identityType":"x"},"identityTypesNote":"x"}""";

            var parsed = IdentityTypeSelection.TryParse(
                queryPart,
                IdentityTypeSet.UsersOnly,
                out var selection,
                out var error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(IdentityTypeSet.UsersOnly, selection);
            Assert.IsFalse(IdentityTypeSelection.IsExplicitlySelected(queryPart));
        }

        [TestMethod]
        public void IdentityTypes_KeyWrittenTwiceIsRefusedWhicheverCopyIsValid()
        {
            var queryParts = new[]
            {
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":[],"identityTypes":["agents"]}""",
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["agents"],"identityTypes":[]}""",
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":["users"],"identityTypes":["agents"]}""",
                """{"type":"SqlMembership","identityTypes":["users"],"source":{"manager":{"id":1}},"identityTypes":["users"]}""",
            };

            foreach (var queryPart in queryParts)
            {
                var parsed = IdentityTypeSelection.TryParse(
                    queryPart,
                    IdentityTypeSet.UsersOnly,
                    out var selection,
                    out var error);

                Assert.IsFalse(parsed, queryPart);
                Assert.AreEqual("identityTypes must appear only once in a query part.", error, queryPart);
                Assert.AreEqual(default, selection, queryPart);
                Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(queryPart), queryPart);

                using var document = JsonDocument.Parse(queryPart);
                Assert.IsTrue(IdentityTypeSelection.IsMisplaced(document.RootElement), queryPart);
            }
        }

        [TestMethod]
        public void IdentityTypes_KeyNestedInsideTheListItselfIsMisplaced()
        {
            var queryParts = new[]
            {
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":[{"identityTypes":["agents"]}]}""",
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":[{"IdentityTypes":["agents"]}]}""",
                """{"type":"SqlMembership","source":{"manager":{"id":1}},"identityTypes":[{"\uD800":["agents"]}]}""",
            };

            foreach (var queryPart in queryParts)
            {
                var parsed = IdentityTypeSelection.TryParse(
                    queryPart,
                    IdentityTypeSet.UsersOnly,
                    out var selection,
                    out var error);

                Assert.IsFalse(parsed, queryPart);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error), queryPart);
                Assert.AreEqual(default, selection, queryPart);

                using var document = JsonDocument.Parse(queryPart);
                Assert.IsTrue(IdentityTypeSelection.IsMisplaced(document.RootElement), queryPart);
            }
        }

        [TestMethod]
        public void IdentityTypes_TextNestedPastTheReadLimitIsRefusedWithAClearMessage()
        {
            // The part object is one level, so 63 arrays inside it is exactly 64 levels.
            var withinLimit = PartWithNestedArrays(63);
            var pastLimit = PartWithNestedArrays(64);

            Assert.IsTrue(
                IdentityTypeSelection.TryParse(withinLimit, IdentityTypeSet.UsersOnly, out var selection, out var error),
                error);
            Assert.AreEqual(IdentityTypeSet.UsersOnly, selection);
            Assert.IsFalse(IdentityTypeSelection.IsExplicitlySelected(withinLimit));

            Assert.IsFalse(IdentityTypeSelection.TryParse(pastLimit, IdentityTypeSet.UsersOnly, out _, out error));
            Assert.AreEqual("A query part is not valid JSON or is nested more than 64 levels deep.", error);
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(pastLimit));
        }

        [TestMethod]
        public void IdentityTypes_DeeplyNestedParsedPartIsSearchedWithoutOverflowingTheStack()
        {
            const int nestingDepth = 50_000;
            var options = new JsonDocumentOptions { MaxDepth = nestingDepth + 10 };
            var withoutSelection = PartWithNestedArrays(nestingDepth);
            var withNestedSelection =
                "{\"type\":\"SqlMembership\",\"source\":" +
                new string('[', nestingDepth) + "{\"identityTypes\":[\"agents\"]}" + new string(']', nestingDepth) +
                "}";

            using var cleanDocument = JsonDocument.Parse(withoutSelection, options);
            using var nestedDocument = JsonDocument.Parse(withNestedSelection, options);

            Assert.IsFalse(IdentityTypeSelection.IsMisplaced(cleanDocument.RootElement));
            Assert.IsFalse(IdentityTypeSelection.IsExplicitlySelected(cleanDocument.RootElement));
            Assert.IsTrue(IdentityTypeSelection.IsMisplaced(nestedDocument.RootElement));
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(nestedDocument.RootElement));
        }

        private static string PartWithNestedArrays(int arrayCount) =>
            "{\"type\":\"SqlMembership\",\"source\":" +
            new string('[', arrayCount) + new string(']', arrayCount) +
            "}";
    }
}
