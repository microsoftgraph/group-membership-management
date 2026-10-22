// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi;
using Services.WebApi.Contracts;

namespace WebApi.Tests
{
    /// <summary>
    /// Contract tests for the four operations of <see cref="CopilotOperationApplier"/>:
    /// new-query on empty workingQuery, add, remove, replace, and set. Uses a deterministic
    /// id factory so server-minted partIds are assertable.
    /// </summary>
    [TestClass]
    public class CopilotOperationApplyTests
    {
        private static Func<string> Ids()
        {
            int n = 0;
            return () => $"minted-{++n}";
        }

        private static CopilotSourcePartResult Part(
            string id,
            string filter,
            string type = "SqlMembership",
            params string[] identityTypes) =>
            new()
            {
                PartId = id,
                SourceType = type,
                Filter = filter,
                Title = filter,
                IdentityTypes = identityTypes.Length == 0
                    ? null
                    : new List<string>(identityTypes)
            };

        private static CopilotSourcePartResult GroupPart(
            string id,
            string groupId,
            bool isExclusion = false,
            params string[] identityTypes) =>
            new()
            {
                PartId = id,
                SourceType = "GroupMembership",
                GroupId = groupId,
                GroupName = groupId,
                Title = groupId,
                IsExclusion = isExclusion,
                IdentityTypes = identityTypes.Length == 0 ? null : identityTypes.ToList(),
            };

        [TestMethod]
        public void Set_OnEmptyWorkingQuery_CreatesNewQuery()
        {
            var ops = new List<EditOperation>
            {
                new() { Op = "set", Parts = new List<CopilotSourcePartResult>
                {
                    new() { SourceType = "SqlMembership", Filter = "Country = 'USA'", Title = "US" }
                } }
            };

            var result = CopilotOperationApplier.Apply(new List<CopilotSourcePartResult>(), ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(1, result.ResultingQuery.Count);
            Assert.AreEqual("minted-1", result.ResultingQuery[0].PartId);
            Assert.AreEqual("Country = 'USA'", result.ResultingQuery[0].Filter);
        }

        [TestMethod]
        public void Add_AppendsPartWithServerMintedId()
        {
            var working = new List<CopilotSourcePartResult>
            {
                Part("a", "A = 1"),
                Part("b", "B = 2")
            };
            var ops = new List<EditOperation>
            {
                new() { Op = "add", Part = new CopilotSourcePartResult { SourceType = "SqlMembership", Filter = "C = 3", Title = "C" } }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(3, result.ResultingQuery.Count);
            Assert.AreEqual("a", result.ResultingQuery[0].PartId);
            Assert.AreEqual("b", result.ResultingQuery[1].PartId);
            Assert.AreEqual("minted-1", result.ResultingQuery[2].PartId);
            Assert.AreEqual(1, result.AddCount);
        }

        [TestMethod]
        public void Remove_DropsOnlyTheTargetPart()
        {
            var working = new List<CopilotSourcePartResult> { Part("a", "A = 1"), Part("b", "B = 2") };
            var ops = new List<EditOperation> { new() { Op = "remove", PartId = "a" } };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(1, result.ResultingQuery.Count);
            Assert.AreEqual("b", result.ResultingQuery[0].PartId);
            Assert.AreEqual(1, result.RemoveCount);
        }

        [TestMethod]
        public void Remove_CaseVariantPartId_ReportsCanonicalId()
        {
            var working = new List<CopilotSourcePartResult>
            {
                Part("hr-local", "Country = 'USA'")
            };
            var ops = new List<EditOperation>
            {
                new() { Op = "remove", PartId = "HR-LOCAL" }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(0, result.ResultingQuery.Count);
            Assert.AreEqual("hr-local", result.AppliedOperations.Single().PartId);
        }

        [TestMethod]
        public void Replace_UpdatesOnlyTargetAndKeepsPartId()
        {
            var working = new List<CopilotSourcePartResult> { Part("a", "A = 1"), Part("b", "B = 2") };
            var ops = new List<EditOperation>
            {
                new() { Op = "replace", PartId = "a", Part = new CopilotSourcePartResult { SourceType = "SqlMembership", Filter = "A = 99", Title = "A2" } }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(2, result.ResultingQuery.Count);
            Assert.AreEqual("a", result.ResultingQuery[0].PartId, "replace keeps the same stable id");
            Assert.AreEqual("A = 99", result.ResultingQuery[0].Filter);
            Assert.AreEqual("B = 2", result.ResultingQuery[1].Filter);
            Assert.AreEqual(1, result.ReplaceCount);
        }

        [TestMethod]
        public void Replace_CaseVariantPartId_PreservesExistingPartId()
        {
            var working = new List<CopilotSourcePartResult> { Part("hr-local", "Country = 'USA'") };
            var ops = new List<EditOperation>
            {
                new()
                {
                    Op = "replace",
                    PartId = "HR-LOCAL",
                    Part = new CopilotSourcePartResult
                    {
                        SourceType = "SqlMembership",
                        Filter = "Country = 'Canada'",
                        Title = null
                    }
                }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual("hr-local", result.ResultingQuery[0].PartId);
            Assert.AreEqual("hr-local", result.AppliedOperations[0].PartId);
            Assert.AreEqual("Country = 'Canada'", result.ResultingQuery[0].Filter);
        }

        [TestMethod]
        public void Set_ReplacesSupportedParts()
        {
            var working = new List<CopilotSourcePartResult> { Part("a", "A = 1"), Part("b", "B = 2") };
            var ops = new List<EditOperation>
            {
                new() { Op = "set", Parts = new List<CopilotSourcePartResult>
                {
                    new() { SourceType = "SqlMembership", Filter = "New = 1", Title = "New" }
                } }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(1, result.ResultingQuery.Count);
            Assert.AreEqual("New = 1", result.ResultingQuery[0].Filter);
            Assert.AreEqual("minted-1", result.ResultingQuery[0].PartId);
        }

        // T042: a large many-part working query round-trips with every part represented and none truncated.
        [TestMethod]
        public void Add_OnLargeManyPartQuery_RoundTripsEveryPart()
        {
            var working = new List<CopilotSourcePartResult>();
            for (int i = 0; i < 60; i++)
            {
                working.Add(Part($"p{i}", $"F{i} = {i}"));
            }
            var ops = new List<EditOperation>
            {
                new() { Op = "add", Part = new CopilotSourcePartResult { SourceType = "SqlMembership", Filter = "Extra = 1", Title = "Extra" } }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(61, result.ResultingQuery.Count);
            for (int i = 0; i < 60; i++)
            {
                Assert.AreEqual($"p{i}", result.ResultingQuery[i].PartId);
                Assert.AreEqual($"F{i} = {i}", result.ResultingQuery[i].Filter);
            }
            Assert.AreEqual("minted-1", result.ResultingQuery[60].PartId);
        }

        [TestMethod]
        public void NoOperation_PreservesExistingGroupSelection()
        {
            var working = new List<CopilotSourcePartResult>
            {
                GroupPart("group-a", "11111111-1111-4111-8111-111111111111", false, "agents", "users"),
            };

            var result = CopilotOperationApplier.Apply(working, [], Ids());

            CollectionAssert.AreEqual(
                new[] { "agents", "users" },
                result.ResultingQuery[0].IdentityTypes);
        }

        [TestMethod]
        public void Replace_SameSourceType_PreservesExactSelection()
        {
            var working = new List<CopilotSourcePartResult>
            {
                GroupPart("group-a", "11111111-1111-4111-8111-111111111111", false, "agents", "users"),
            };
            var operations = new List<EditOperation>
            {
                new()
                {
                    Op = "replace",
                    PartId = "group-a",
                    Part = GroupPart(
                        "",
                        "22222222-2222-4222-8222-222222222222",
                        false,
                        "users"),
                },
            };

            var result = CopilotOperationApplier.Apply(working, operations, Ids());

            Assert.AreEqual("group-a", result.ResultingQuery[0].PartId);
            CollectionAssert.AreEqual(
                new[] { "agents", "users" },
                result.ResultingQuery[0].IdentityTypes);
        }

        [TestMethod]
        public void Replace_ChangedSourceType_DropsSelection()
        {
            var working = new List<CopilotSourcePartResult>
            {
                GroupPart("group-a", "11111111-1111-4111-8111-111111111111", false, "agents"),
            };
            var operations = new List<EditOperation>
            {
                new()
                {
                    Op = "replace",
                    PartId = "group-a",
                    Part = Part("", "Country = 'USA'"),
                },
            };

            var result = CopilotOperationApplier.Apply(working, operations, Ids());

            Assert.IsNull(result.ResultingQuery[0].IdentityTypes);
        }

        [TestMethod]
        public void Replace_ChangedSourceTypeFromSqlToGroup_DropsSelection()
        {
            var working = new List<CopilotSourcePartResult>
            {
                Part(
                    "sql-a",
                    "Country = 'USA'",
                    identityTypes: new[] { "users" }),
            };
            var operations = new List<EditOperation>
            {
                new()
                {
                    Op = "replace",
                    PartId = "sql-a",
                    Part = GroupPart(
                        "",
                        "11111111-1111-4111-8111-111111111111",
                        false,
                        "agents"),
                },
            };

            var result = CopilotOperationApplier.Apply(working, operations, Ids());

            Assert.IsNull(result.ResultingQuery[0].IdentityTypes);
        }

        [TestMethod]
        public void Set_MatchedGroupPartsAgreeAsSets_CopiesEarliestStoredOrder()
        {
            var groupId = "11111111-1111-4111-8111-111111111111";
            var working = new List<CopilotSourcePartResult>
            {
                GroupPart("group-a", groupId, false, "agents", "users"),
                GroupPart("group-b", groupId.ToUpperInvariant(), false, "users", "agents"),
            };
            var operations = new List<EditOperation>
            {
                new()
                {
                    Op = "set",
                    Parts = new List<CopilotSourcePartResult>
                    {
                        GroupPart("", groupId, false, "users"),
                    },
                },
            };

            var result = CopilotOperationApplier.Apply(working, operations, Ids());

            Assert.IsNull(result.Warning);
            CollectionAssert.AreEqual(
                new[] { "agents", "users" },
                result.ResultingQuery[0].IdentityTypes);
        }

        [TestMethod]
        public void Set_MatchedGroupPartsDisagree_DropsSelectionWithExactWarning()
        {
            var groupId = "11111111-1111-4111-8111-111111111111";
            var working = new List<CopilotSourcePartResult>
            {
                GroupPart("group-a", groupId),
                GroupPart("group-b", groupId, false, "users", "agents"),
            };
            var operations = new List<EditOperation>
            {
                new()
                {
                    Op = "set",
                    Parts = new List<CopilotSourcePartResult>
                    {
                        GroupPart("", groupId),
                    },
                },
            };

            var result = CopilotOperationApplier.Apply(working, operations, Ids());

            Assert.IsNull(result.ResultingQuery[0].IdentityTypes);
            Assert.AreEqual(
                CopilotOperationApplier.IdentitySelectionConflictWarning,
                result.Warning);
        }

        [TestMethod]
        public void Set_DifferentGroupOrExclusionSetting_CreatesSelectionFreePartsWithoutWarning()
        {
            var working = new List<CopilotSourcePartResult>
            {
                GroupPart("group-a", "11111111-1111-4111-8111-111111111111", false, "agents"),
            };
            var operations = new List<EditOperation>
            {
                new()
                {
                    Op = "set",
                    Parts = new List<CopilotSourcePartResult>
                    {
                        GroupPart(
                            "",
                            "22222222-2222-4222-8222-222222222222",
                            false,
                            "users"),
                        GroupPart(
                            "",
                            "11111111-1111-4111-8111-111111111111",
                            true,
                            "agents"),
                    },
                },
            };

            var result = CopilotOperationApplier.Apply(working, operations, Ids());

            Assert.IsNull(result.Warning);
            Assert.IsTrue(result.ResultingQuery.All(part => part.IdentityTypes == null));
        }

        [TestMethod]
        public void Add_AndRemoveThenAdd_CreateSelectionFreePartsWithoutWarning()
        {
            var groupId = "11111111-1111-4111-8111-111111111111";
            var working = new List<CopilotSourcePartResult>
            {
                GroupPart("group-a", groupId, false, "agents"),
            };
            var operations = new List<EditOperation>
            {
                new() { Op = "remove", PartId = "group-a" },
                new() { Op = "add", Part = GroupPart("", groupId, false, "users") },
            };

            var result = CopilotOperationApplier.Apply(working, operations, Ids());

            Assert.IsNull(result.Warning);
            Assert.AreEqual(1, result.ResultingQuery.Count);
            Assert.IsNull(result.ResultingQuery[0].IdentityTypes);
        }
    }
}
