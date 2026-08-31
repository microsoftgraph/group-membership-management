// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi;
using Services.WebApi.Contracts;

namespace WebApi.Tests
{
    /// <summary>
    /// Invariant tests for <see cref="CopilotOperationApplier"/>: preservation of unsupported parts,
    /// the atomic safety gate, the empty-result warning, validation scope, and partial-query passthrough.
    /// </summary>
    [TestClass]
    public class CopilotOperationInvariantTests
    {
        private static Func<string> Ids()
        {
            int n = 0;
            return () => $"minted-{++n}";
        }

        private static CopilotSourcePartResult Part(string id, string type, string? filter = null) =>
            new() { PartId = id, SourceType = type, Filter = filter ?? string.Empty, Title = id };

        [TestMethod]
        public void Set_PreservesUnsupportedParts()
        {
            var working = new List<CopilotSourcePartResult>
            {
                Part("hr", "SqlMembership", "A = 1"),
                Part("owners", "GroupOwnership"),
                Part("place", "PlaceMembership")
            };
            var ops = new List<EditOperation>
            {
                new() { Op = "set", Parts = new List<CopilotSourcePartResult>
                {
                    new() { SourceType = "SqlMembership", Filter = "B = 2", Title = "B" }
                } }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            // New supported part first, then the two preserved unsupported parts.
            Assert.AreEqual(3, result.ResultingQuery.Count);
            Assert.AreEqual("B = 2", result.ResultingQuery[0].Filter);
            CollectionAssert.AreEquivalent(
                new[] { "owners", "place" },
                result.ResultingQuery.Skip(1).Select(p => p.PartId).ToArray());
            Assert.AreEqual(2, result.PreservedUnsupportedCount);
        }

        [TestMethod]
        public void SafetyGate_RemoveUnknownPartId_RejectsWholeSetAtomically()
        {
            var working = new List<CopilotSourcePartResult> { Part("a", "SqlMembership", "A = 1") };
            var ops = new List<EditOperation>
            {
                new() { Op = "add", Part = new CopilotSourcePartResult { SourceType = "SqlMembership", Filter = "C = 3", Title = "C" } },
                new() { Op = "remove", PartId = "ghost" }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.AreEqual("UnknownPartTarget", result.ErrorCode);
            // Nothing applied: the add did not take effect either.
            Assert.AreEqual(1, result.ResultingQuery.Count);
            Assert.AreEqual("a", result.ResultingQuery[0].PartId);
            Assert.AreEqual(0, result.AddCount);
        }

        [TestMethod]
        public void SafetyGate_ReplaceUnknownPartId_Rejects()
        {
            var working = new List<CopilotSourcePartResult> { Part("a", "SqlMembership", "A = 1") };
            var ops = new List<EditOperation>
            {
                new() { Op = "replace", PartId = "nope", Part = new CopilotSourcePartResult { Filter = "X = 1" } }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.AreEqual("UnknownPartTarget", result.ErrorCode);
            Assert.AreEqual(1, result.ResultingQuery.Count);
            Assert.AreEqual("A = 1", result.ResultingQuery[0].Filter);
        }

        [TestMethod]
        public void EmptyResult_SetsWarningAndSucceeds()
        {
            var working = new List<CopilotSourcePartResult> { Part("a", "SqlMembership", "A = 1") };
            var ops = new List<EditOperation> { new() { Op = "remove", PartId = "a" } };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(0, result.ResultingQuery.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Warning));
        }

        // Regression: a turn with NO applied operations (clarifying question / describe / off-topic)
        // on an empty working query must NOT surface the "no membership criteria" warning — nothing
        // changed, so the alarming banner should not appear.
        [TestMethod]
        public void EmptyWorkingQuery_NoOperations_DoesNotWarn()
        {
            var working = new List<CopilotSourcePartResult>();

            var result = CopilotOperationApplier.Apply(working, new List<EditOperation>(), Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(0, result.ResultingQuery.Count);
            Assert.AreEqual(0, result.AppliedOperations.Count);
            Assert.IsTrue(string.IsNullOrWhiteSpace(result.Warning), "No operations were applied; warning must not be set.");
        }

        // Edge: a `set` with no parts on an already-empty query applied an operation but did not
        // *empty* anything — it was empty to begin with — so the warning must not fire.
        [TestMethod]
        public void SetEmpty_OnEmptyWorkingQuery_DoesNotWarn()
        {
            var working = new List<CopilotSourcePartResult>();
            var ops = new List<EditOperation> { new() { Op = "set", Parts = new List<CopilotSourcePartResult>() } };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(0, result.ResultingQuery.Count);
            Assert.IsTrue(string.IsNullOrWhiteSpace(result.Warning), "Query was already empty; nothing was emptied.");
        }

        [TestMethod]
        public void ValidationScope_UntouchedPartsAreNotAltered()
        {
            var working = new List<CopilotSourcePartResult>
            {
                Part("a", "SqlMembership", "A = 1"),
                Part("owners", "GroupOwnership")
            };
            var ops = new List<EditOperation>
            {
                new() { Op = "add", Part = new CopilotSourcePartResult { SourceType = "SqlMembership", Filter = "B = 2", Title = "B" } }
            };

            var result = CopilotOperationApplier.Apply(working, ops, Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(3, result.ResultingQuery.Count);
            // Untouched parts carried through byte-for-byte.
            Assert.AreEqual("a", result.ResultingQuery[0].PartId);
            Assert.AreEqual("A = 1", result.ResultingQuery[0].Filter);
            Assert.AreEqual("owners", result.ResultingQuery[1].PartId);
            Assert.AreEqual("GroupOwnership", result.ResultingQuery[1].SourceType);
        }

        // T041: a syntactically-valid-but-incomplete working query is treated as working context and
        // passed through unchanged (no invented parts) when there are no operations (e.g., a describe turn).
        [TestMethod]
        public void PartialQuery_NoOperations_PassesThroughUnchanged()
        {
            var working = new List<CopilotSourcePartResult>
            {
                Part("a", "SqlMembership", "A = 1"),
                Part("b", "GroupMembership") // group part with no groupId yet (mid-edit)
            };

            var result = CopilotOperationApplier.Apply(working, new List<EditOperation>(), Ids());

            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(2, result.ResultingQuery.Count);
            Assert.AreEqual("a", result.ResultingQuery[0].PartId);
            Assert.AreEqual("b", result.ResultingQuery[1].PartId);
            Assert.AreEqual(0, result.AppliedOperations.Count);
        }
    }
}
