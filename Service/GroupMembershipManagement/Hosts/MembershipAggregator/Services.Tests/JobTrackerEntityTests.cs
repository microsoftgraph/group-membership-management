// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Services.Tests
{
    /// <summary>
    /// Locks in the core invariants of the atomic register-and-check fix for the
    /// JobTrackerEntity state-loss race. See BUG_FIX_RUBBER_DUCK in the
    /// MembershipAggregator host for the full root-cause analysis.
    /// </summary>
    [TestClass]
    public class JobTrackerEntityTests
    {
        [TestMethod]
        public async Task RegisterPartAndCheckComplete_LastPartArrives_ClaimsCompletion()
        {
            var entity = new JobTrackerEntity();

            var firstHalf = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/part1.json"
            });

            var secondHalf = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/part2.json",
                IsDestinationPart = true
            });

            Assert.IsFalse(firstHalf.IsComplete, "First part should not signal completion.");
            Assert.AreEqual(1, firstHalf.CompletedCount);

            Assert.IsTrue(secondHalf.IsComplete, "Final part should claim completion.");
            Assert.AreEqual(2, secondHalf.CompletedCount);
            Assert.AreEqual(2, secondHalf.TotalParts);
            Assert.AreEqual(2, secondHalf.CompletedParts.Count);
            Assert.AreEqual("/part1.json", secondHalf.CompletedParts[1]);
            Assert.AreEqual("/part2.json", secondHalf.CompletedParts[2]);
            Assert.AreEqual("/part2.json", secondHalf.DestinationPart);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_DestinationArrivesFirst_ClaimsCompletionAfterAllSources()
        {
            var entity = new JobTrackerEntity();

            var destination = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 3,
                TotalParts = 3,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });

            var secondSource = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 3,
                FilePath = "/source2.json"
            });

            var firstSource = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 3,
                FilePath = "/source1.json"
            });

            Assert.IsFalse(destination.IsComplete);
            Assert.IsFalse(secondSource.IsComplete);
            Assert.IsTrue(firstSource.IsComplete);
            Assert.AreEqual(3, firstSource.CompletedParts.Count);
            Assert.AreEqual("/source1.json", firstSource.CompletedParts[1]);
            Assert.AreEqual("/source2.json", firstSource.CompletedParts[2]);
            Assert.AreEqual("/destination.json", firstSource.CompletedParts[3]);
            Assert.AreEqual("/destination.json", firstSource.DestinationPart);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_DuplicateFinalPart_OnlyOneCallerClaimsCompletion()
        {
            // Critical invariant: even if the same part arrives twice at the completion
            // boundary (retry, replay, or two orchestrators racing), exactly one caller
            // observes IsComplete=true. This guards against double-dispatch of
            // MembershipSubOrchestrator + TopicMessageSender.

            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/part1.json"
            });

            var firstClaim = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/part2.json"
            });

            var duplicateClaim = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/part2.json"
            });

            Assert.IsTrue(firstClaim.IsComplete ^ duplicateClaim.IsComplete,
                "Exactly one caller should observe IsComplete=true (XOR).");
            Assert.IsTrue(firstClaim.IsComplete,
                "First caller at completion boundary should claim it.");
            Assert.IsFalse(duplicateClaim.IsComplete,
                "Subsequent caller must not re-claim completion.");
            Assert.AreEqual(2, duplicateClaim.CompletedCount,
                "CompletedCount should remain stable after completion claimed.");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_LateRegistration_DoesNotReClaimCompletion()
        {
            // Once CompletionClaimed=true, any further registration (including a new
            // PartNumber that pushes count beyond TotalParts) must not re-trigger
            // downstream dispatch.

            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/part1.json"
            });
            var completion = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/part2.json"
            });
            Assert.IsTrue(completion.IsComplete);

            var lateArrival = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 3,
                TotalParts = 2,
                FilePath = "/part3.json"
            });

            Assert.IsFalse(lateArrival.IsComplete,
                "Post-completion registrations must not re-claim completion.");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_TotalPartsOnlySetOnce()
        {
            // D3 invariant: a later caller that disagrees on TotalParts must not
            // overwrite the entity's view. First-writer wins.

            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 3,
                FilePath = "/part1.json"
            });

            var second = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 99,
                FilePath = "/part2.json"
            });

            Assert.AreEqual(3, second.TotalParts,
                "TotalParts must remain at its first-set value.");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_NullRegistration_ReturnsCurrentStateSafely()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/part1.json"
            });

            var result = await entity.RegisterPartAndCheckComplete(null);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(1, result.CompletedCount);
            Assert.AreEqual(2, result.TotalParts);
            Assert.AreEqual(0, result.CompletedParts.Count);
            Assert.IsNull(result.DestinationPart);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_CompletionSnapshot_IsIndependentOfLaterRegistrations()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/part1.json"
            });

            var completion = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/part2.json",
                IsDestinationPart = true
            });

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 3,
                TotalParts = 2,
                FilePath = "/part3.json"
            });

            Assert.AreEqual(2, completion.CompletedParts.Count);
            Assert.IsFalse(completion.CompletedParts.ContainsKey(3));
            Assert.AreEqual("/part2.json", completion.DestinationPart);
        }
    }
}
