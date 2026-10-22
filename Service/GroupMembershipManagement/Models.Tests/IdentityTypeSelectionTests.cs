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
        public void GroupDefault_IsExactlyUsersAndAgents()
        {
            Assert.AreEqual(
                IdentityTypeSet.Of(IdentityType.Users, IdentityType.Agents),
                IdentityTypeSet.GroupDefault);
            CollectionAssert.AreEqual(
                new[] { "users", "agents" },
                IdentityTypeSet.GroupDefault.ToTypeNames().ToArray());
        }

        [TestMethod]
        public void IdentityTypes_AllowedSetRejectsOtherwiseSupportedValues()
        {
            var usersOnlyAllowed = IdentityTypeSet.UsersOnly;

            Assert.IsTrue(IdentityTypeSet.TryResolve(
                new[] { "users" },
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var users,
                out var usersError));
            Assert.AreEqual(IdentityTypeSet.UsersOnly, users);
            Assert.IsNull(usersError);

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                new[] { "agents" },
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var refused,
                out var refusedError));
            Assert.AreEqual(default, refused);
            Assert.IsTrue(refusedError?.Contains("'users'", StringComparison.Ordinal));
            Assert.IsFalse(refusedError?.Contains("'agents'", StringComparison.Ordinal));
        }

        [TestMethod]
        public void IdentityTypes_EmptyAllowedSetRejectsExplicitValuesWithReadableErrors()
        {
            var noTypesAllowed = IdentityTypeSet.Of();
            const string usersJson =
                """{"type":"GroupMembership","identityTypes":["users"]}""";

            Assert.IsTrue(IdentityTypeSet.TryResolve(
                null,
                IdentityTypeSet.GroupDefault,
                noTypesAllowed,
                out var omitted,
                out var omittedError));
            Assert.AreEqual(IdentityTypeSet.GroupDefault, omitted);
            Assert.IsNull(omittedError);

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                Array.Empty<string>(),
                IdentityTypeSet.GroupDefault,
                noTypesAllowed,
                out var empty,
                out var emptyError));
            Assert.AreEqual(default, empty);
            Assert.AreEqual(
                "identityTypes must not be empty; supported values are none.",
                emptyError);

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                new[] { "users" },
                IdentityTypeSet.GroupDefault,
                noTypesAllowed,
                out var explicitSelection,
                out var explicitError));
            Assert.AreEqual(default, explicitSelection);
            Assert.AreEqual(
                "identityTypes entry at index 0 is not supported; no explicit identity types are allowed.",
                explicitError);

            Assert.IsFalse(IdentityTypeSelection.TryParse(
                usersJson,
                IdentityTypeSet.GroupDefault,
                noTypesAllowed,
                out var parsedFromText,
                out var parsedFromTextError));
            Assert.AreEqual(default, parsedFromText);

            using var usersDocument = JsonDocument.Parse(usersJson);
            Assert.IsFalse(IdentityTypeSelection.TryParse(
                usersDocument.RootElement,
                IdentityTypeSet.GroupDefault,
                noTypesAllowed,
                out var parsedFromElement,
                out var parsedFromElementError));
            Assert.AreEqual(default, parsedFromElement);
            Assert.IsFalse(IdentityTypeSelection.TryParseProperty(
                usersDocument.RootElement.GetProperty(IdentityTypeSelection.PropertyName),
                noTypesAllowed,
                out var parsedFromProperty,
                out var parsedFromPropertyError));
            Assert.AreEqual(default, parsedFromProperty);
            Assert.AreEqual(explicitError, parsedFromTextError);
            Assert.AreEqual(explicitError, parsedFromElementError);
            Assert.AreEqual(explicitError, parsedFromPropertyError);
        }

        [DataTestMethod]
        [DataRow("Users")]
        [DataRow("devices")]
        public void IdentityTypes_UnrestrictedOverloadsPreserveUnsupportedNameError(string typeName)
        {
            const string expectedError =
                "identityTypes entry at index 0 is not supported; supported values are 'users', 'agents', lower case.";

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                new[] { typeName },
                IdentityTypeSet.UsersOnly,
                out var resolved,
                out var resolveError));
            Assert.AreEqual(default, resolved);
            Assert.AreEqual(expectedError, resolveError);

            var queryPartJson =
                $"{{\"type\":\"SqlMembership\",\"identityTypes\":[\"{typeName}\"]}}";
            Assert.IsFalse(IdentityTypeSelection.TryParse(
                queryPartJson,
                IdentityTypeSet.UsersOnly,
                out var parsed,
                out var parseError));
            Assert.AreEqual(default, parsed);
            Assert.AreEqual(expectedError, parseError);
        }

        [TestMethod]
        public void IdentityTypes_UnrestrictedOverloadsPreserveAcceptedAndRejectedContracts()
        {
            const string agentsJson =
                """{"type":"SqlMembership","identityTypes":["agents"]}""";

            Assert.IsTrue(IdentityTypeSelection.TryParse(
                agentsJson,
                IdentityTypeSet.UsersOnly,
                out var agentsFromText,
                out var agentsFromTextError));
            Assert.AreEqual(IdentityTypeSet.Of(IdentityType.Agents), agentsFromText);
            Assert.IsNull(agentsFromTextError);

            using var agentsDocument = JsonDocument.Parse(agentsJson);
            Assert.IsTrue(IdentityTypeSelection.TryParse(
                agentsDocument.RootElement,
                IdentityTypeSet.UsersOnly,
                out var agentsFromElement,
                out var agentsFromElementError));
            Assert.AreEqual(agentsFromText, agentsFromElement);
            Assert.IsNull(agentsFromElementError);
            Assert.IsTrue(IdentityTypeSelection.TryParseProperty(
                agentsDocument.RootElement.GetProperty(IdentityTypeSelection.PropertyName),
                out var agentsFromProperty,
                out var agentsFromPropertyError));
            Assert.AreEqual(agentsFromText, agentsFromProperty);
            Assert.IsNull(agentsFromPropertyError);

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                Array.Empty<string>(),
                IdentityTypeSet.UsersOnly,
                out var empty,
                out var emptyError));
            Assert.AreEqual(default, empty);
            Assert.AreEqual(
                "identityTypes must not be empty; supported values are 'users', 'agents'.",
                emptyError);

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                new[] { "users", "users" },
                IdentityTypeSet.UsersOnly,
                out var duplicate,
                out var duplicateError));
            Assert.AreEqual(default, duplicate);
            Assert.AreEqual(
                "identityTypes entry at index 1 repeats a value; each supported value may appear once.",
                duplicateError);
        }

        [TestMethod]
        public void IdentityTypes_AllowedSetHandlesReverseMixedAndEmptyLists()
        {
            var agentsOnlyAllowed = IdentityTypeSet.Of(IdentityType.Agents);

            Assert.IsTrue(IdentityTypeSet.TryResolve(
                new[] { "agents" },
                IdentityTypeSet.GroupDefault,
                agentsOnlyAllowed,
                out var agents,
                out var agentsError));
            Assert.AreEqual(agentsOnlyAllowed, agents);
            Assert.IsNull(agentsError);

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                new[] { "users" },
                IdentityTypeSet.GroupDefault,
                agentsOnlyAllowed,
                out var users,
                out var usersError));
            Assert.AreEqual(default, users);
            Assert.IsTrue(usersError?.Contains("'agents'", StringComparison.Ordinal));
            Assert.IsFalse(usersError?.Contains("'users'", StringComparison.Ordinal));

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                new[] { "users", "agents" },
                IdentityTypeSet.GroupDefault,
                IdentityTypeSet.UsersOnly,
                out var mixed,
                out var mixedError));
            Assert.AreEqual(default, mixed);
            Assert.IsTrue(mixedError?.Contains("index 1", StringComparison.Ordinal));
            Assert.IsTrue(mixedError?.Contains("'users'", StringComparison.Ordinal));
            Assert.IsFalse(mixedError?.Contains("'agents'", StringComparison.Ordinal));

            Assert.IsFalse(IdentityTypeSet.TryResolve(
                Array.Empty<string>(),
                IdentityTypeSet.GroupDefault,
                IdentityTypeSet.UsersOnly,
                out var empty,
                out var emptyError));
            Assert.AreEqual(default, empty);
            Assert.AreEqual(
                "identityTypes must not be empty; supported values are 'users'.",
                emptyError);
        }

        [TestMethod]
        public void IdentityTypes_GroupParserUsesGroupDefaultAndAllowedSet()
        {
            Assert.IsTrue(IdentityTypeSelection.TryParse(
                """{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}""",
                IdentityTypeSet.GroupDefault,
                IdentityTypeSet.GroupDefault,
                out var omitted,
                out var omittedError));
            Assert.AreEqual(IdentityTypeSet.GroupDefault, omitted);
            Assert.IsNull(omittedError);

            Assert.IsTrue(IdentityTypeSelection.TryParse(
                """{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","identityTypes":["agents"]}""",
                IdentityTypeSet.GroupDefault,
                IdentityTypeSet.GroupDefault,
                out var agents,
                out var agentsError));
            Assert.AreEqual(IdentityTypeSet.Of(IdentityType.Agents), agents);
            Assert.IsNull(agentsError);
        }

        [TestMethod]
        public void IdentityTypes_AllowedSelectionOverloadsAgree()
        {
            const string omittedJson =
                """{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}""";
            const string usersJson =
                """{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","identityTypes":["users"]}""";
            const string agentsJson =
                """{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","identityTypes":["agents"]}""";
            var usersOnlyAllowed = IdentityTypeSet.UsersOnly;

            Assert.IsTrue(IdentityTypeSelection.TryParse(
                omittedJson,
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var omittedFromText,
                out var omittedFromTextError));
            Assert.AreEqual(IdentityTypeSet.GroupDefault, omittedFromText);
            Assert.IsNull(omittedFromTextError);

            using var omittedDocument = JsonDocument.Parse(omittedJson);
            Assert.IsTrue(IdentityTypeSelection.TryParse(
                omittedDocument.RootElement,
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var omittedFromElement,
                out var omittedFromElementError));
            Assert.AreEqual(IdentityTypeSet.GroupDefault, omittedFromElement);
            Assert.IsNull(omittedFromElementError);

            Assert.IsTrue(IdentityTypeSelection.TryParse(
                usersJson,
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var usersFromText,
                out var usersFromTextError));
            Assert.AreEqual(IdentityTypeSet.UsersOnly, usersFromText);
            Assert.IsNull(usersFromTextError);

            using var usersDocument = JsonDocument.Parse(usersJson);
            Assert.IsTrue(IdentityTypeSelection.TryParse(
                usersDocument.RootElement,
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var usersFromElement,
                out var usersFromElementError));
            Assert.AreEqual(IdentityTypeSet.UsersOnly, usersFromElement);
            Assert.IsNull(usersFromElementError);
            Assert.IsTrue(IdentityTypeSelection.TryParseProperty(
                usersDocument.RootElement.GetProperty(IdentityTypeSelection.PropertyName),
                usersOnlyAllowed,
                out var usersFromProperty,
                out var usersFromPropertyError));
            Assert.AreEqual(IdentityTypeSet.UsersOnly, usersFromProperty);
            Assert.IsNull(usersFromPropertyError);

            Assert.IsFalse(IdentityTypeSelection.TryParse(
                agentsJson,
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var agentsFromText,
                out var agentsFromTextError));
            Assert.AreEqual(default, agentsFromText);
            Assert.AreEqual(
                "identityTypes entry at index 0 is not supported; supported values are 'users', lower case.",
                agentsFromTextError);

            using var agentsDocument = JsonDocument.Parse(agentsJson);
            Assert.IsFalse(IdentityTypeSelection.TryParse(
                agentsDocument.RootElement,
                IdentityTypeSet.GroupDefault,
                usersOnlyAllowed,
                out var agentsFromElement,
                out var agentsFromElementError));
            Assert.AreEqual(default, agentsFromElement);
            Assert.IsFalse(IdentityTypeSelection.TryParseProperty(
                agentsDocument.RootElement.GetProperty(IdentityTypeSelection.PropertyName),
                usersOnlyAllowed,
                out var agentsFromProperty,
                out var agentsFromPropertyError));
            Assert.AreEqual(default, agentsFromProperty);
            Assert.AreEqual(agentsFromTextError, agentsFromElementError);
            Assert.AreEqual(agentsFromTextError, agentsFromPropertyError);
        }

        [TestMethod]
        public void IdentityTypes_TextPlacementCheckHandlesEveryInputKind()
        {
            var validInputs = new[]
            {
                null,
                string.Empty,
                " ",
                "[]",
                "{}",
                """{"identityTypes":["users"]}""",
            };
            var invalidInputs = new[]
            {
                "{",
                """{"IdentityTypes":["users"]}""",
                """{"source":{"identityTypes":["users"]}}""",
                """{"identityTypes":["users"],"identityTypes":["agents"]}""",
                """{"\uD800":["users"]}""",
            };

            foreach (var validInput in validInputs)
            {
                Assert.IsFalse(IdentityTypeSelection.IsMisplaced(validInput), validInput);
            }

            foreach (var invalidInput in invalidInputs)
            {
                Assert.IsTrue(IdentityTypeSelection.IsMisplaced(invalidInput), invalidInput);
            }

            foreach (var comparableInput in validInputs
                .OfType<string>()
                .Where(input => !string.IsNullOrWhiteSpace(input))
                .Concat(invalidInputs.Skip(1)))
            {
                using var document = JsonDocument.Parse(comparableInput);
                Assert.AreEqual(
                    IdentityTypeSelection.IsMisplaced(document.RootElement),
                    IdentityTypeSelection.IsMisplaced(comparableInput),
                    comparableInput);
            }
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
            Assert.IsFalse(IdentityTypeSelection.IsMisplaced(withinLimit));

            Assert.IsFalse(IdentityTypeSelection.TryParse(pastLimit, IdentityTypeSet.UsersOnly, out _, out error));
            Assert.AreEqual("A query part is not valid JSON or is nested more than 64 levels deep.", error);
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(pastLimit));
            Assert.IsTrue(IdentityTypeSelection.IsMisplaced(pastLimit));
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
