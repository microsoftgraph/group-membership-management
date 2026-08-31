// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi;
using Services.WebApi.Contracts;

namespace WebApi.Tests
{
    /// <summary>
    /// US2 (T031): describing / asking a read-only question about the working query. When the model
    /// answers WITHOUT proposing changes it emits an empty operations list; the working query must
    /// come back as the resulting query completely unchanged, with no operations and no warning.
    /// </summary>
    [TestClass]
    public class CopilotDescribeQueryTests
    {
        private static CopilotSourcePartResult Part(string id, string filter, string type = "SqlMembership") =>
            new() { PartId = id, SourceType = type, Filter = filter, Title = filter };

        [TestMethod]
        public void Describe_NoOperations_ReturnsWorkingQueryUnchanged()
        {
            // Arrange
            var working = new List<CopilotSourcePartResult>
            {
                Part("existing-1", "Country = 'USA'"),
                Part("existing-2", "Department = 'Engineering'", "GroupMembership")
            };

            // Act: describe = zero operations.
            var result = CopilotOperationApplier.Apply(working, new List<EditOperation>());

            // Assert: identical ids, filters, order; nothing applied; no warning.
            Assert.IsNull(result.ErrorCode);
            Assert.IsNull(result.Warning);
            Assert.AreEqual(0, result.AppliedOperations.Count);
            Assert.AreEqual(2, result.ResultingQuery.Count);
            Assert.AreEqual("existing-1", result.ResultingQuery[0].PartId);
            Assert.AreEqual("Country = 'USA'", result.ResultingQuery[0].Filter);
            Assert.AreEqual("existing-2", result.ResultingQuery[1].PartId);
            Assert.AreEqual("Department = 'Engineering'", result.ResultingQuery[1].Filter);
        }

        [TestMethod]
        public void Describe_EmptyWorkingQuery_ReturnsEmptyWithoutOperations()
        {
            // Act
            var result = CopilotOperationApplier.Apply(new List<CopilotSourcePartResult>(), new List<EditOperation>());

            // Assert: describing an empty query is a no-op; the empty-result warning is expected
            // because the resulting query genuinely has no membership criteria.
            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(0, result.AppliedOperations.Count);
            Assert.AreEqual(0, result.ResultingQuery.Count);
        }
    }
}
