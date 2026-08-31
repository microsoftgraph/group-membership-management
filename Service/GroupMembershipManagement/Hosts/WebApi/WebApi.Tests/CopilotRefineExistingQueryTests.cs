// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi;
using Services.WebApi.Contracts;

namespace WebApi.Tests
{
    /// <summary>
    /// US1 (T026): refining an EXISTING working query. Verifies that additive and targeted
    /// refinements keep every pre-existing, unrelated part intact and only touch the intended part.
    /// Targets the pure <see cref="CopilotOperationApplier"/> so behavior is deterministic.
    /// </summary>
    [TestClass]
    public class CopilotRefineExistingQueryTests
    {
        private static Func<string> Ids()
        {
            int n = 0;
            return () => $"minted-{++n}";
        }

        private static CopilotSourcePartResult Part(string id, string filter, string type = "SqlMembership") =>
            new() { PartId = id, SourceType = type, Filter = filter, Title = filter };

        [TestMethod]
        public void Refine_AddToExistingQuery_PreservesOriginalPartsAndAppendsAddition()
        {
            // Arrange: a real existing query the user loaded from a saved destination.
            var working = new List<CopilotSourcePartResult>
            {
                Part("existing-1", "Country = 'USA'"),
                Part("existing-2", "Department = 'Engineering'")
            };
            var ops = new List<EditOperation>
            {
                new() { Op = "add", Part = new CopilotSourcePartResult { SourceType = "SqlMembership", Filter = "JobTitle = 'Manager'", Title = "Managers" } }
            };

            // Act
            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            // Assert: originals untouched (same ids, same filters, same order) + new part appended.
            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(3, result.ResultingQuery.Count);
            Assert.AreEqual("existing-1", result.ResultingQuery[0].PartId);
            Assert.AreEqual("Country = 'USA'", result.ResultingQuery[0].Filter);
            Assert.AreEqual("existing-2", result.ResultingQuery[1].PartId);
            Assert.AreEqual("Department = 'Engineering'", result.ResultingQuery[1].Filter);
            Assert.AreEqual("minted-1", result.ResultingQuery[2].PartId);
            Assert.AreEqual("JobTitle = 'Manager'", result.ResultingQuery[2].Filter);
            Assert.AreEqual(1, result.AddCount);
        }

        [TestMethod]
        public void Refine_ReplaceOnePart_LeavesUnrelatedPartsUnchanged()
        {
            // Arrange
            var working = new List<CopilotSourcePartResult>
            {
                Part("existing-1", "Country = 'USA'"),
                Part("existing-2", "Department = 'Engineering'"),
                Part("existing-3", "Level >= 63")
            };
            var ops = new List<EditOperation>
            {
                new()
                {
                    Op = "replace",
                    PartId = "existing-2",
                    Part = new CopilotSourcePartResult { SourceType = "SqlMembership", Filter = "Department = 'Research'", Title = "Research" }
                }
            };

            // Act
            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            // Assert: unrelated parts byte-for-byte unchanged; targeted part edited in place (id preserved).
            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(3, result.ResultingQuery.Count);
            Assert.AreEqual("existing-1", result.ResultingQuery[0].PartId);
            Assert.AreEqual("Country = 'USA'", result.ResultingQuery[0].Filter);
            Assert.AreEqual("existing-2", result.ResultingQuery[1].PartId);
            Assert.AreEqual("Department = 'Research'", result.ResultingQuery[1].Filter);
            Assert.AreEqual("existing-3", result.ResultingQuery[2].PartId);
            Assert.AreEqual("Level >= 63", result.ResultingQuery[2].Filter);
            Assert.AreEqual(1, result.ReplaceCount);
        }

        [TestMethod]
        public void Refine_RemoveOnePart_KeepsTheRest()
        {
            // Arrange
            var working = new List<CopilotSourcePartResult>
            {
                Part("existing-1", "Country = 'USA'"),
                Part("existing-2", "Department = 'Engineering'")
            };
            var ops = new List<EditOperation>
            {
                new() { Op = "remove", PartId = "existing-1" }
            };

            // Act
            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            // Assert
            Assert.IsNull(result.ErrorCode);
            Assert.IsNull(result.Warning);
            Assert.AreEqual(1, result.ResultingQuery.Count);
            Assert.AreEqual("existing-2", result.ResultingQuery[0].PartId);
            Assert.AreEqual(1, result.RemoveCount);
        }
    }
}
