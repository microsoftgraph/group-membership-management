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

        private static CopilotSourcePartResult Part(string id, string filter, string type = "SqlMembership") =>
            new() { PartId = id, SourceType = type, Filter = filter, Title = filter };

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
    }
}
