// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using GraphUpdater.Activity.JobTracker;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Services.Tests
{
    /// <summary>
    /// Locks in the core invariants of the atomic register-and-check fix for the GraphUpdater
    /// JobTrackerEntity state-loss false-Error. The entity previously lost its accumulated user
    /// state when it idled past the extended-session timeout during a Graph-throttle stall between
    /// a split GetState and SetState, causing the terminal message to under-count and stamp a
    /// false Error. Mirrors the MembershipAggregator fix (RegisterPartAndCheckComplete).
    /// </summary>
    [TestClass]
    public class JobTrackerEntityTests
    {
        private static JobTrackerMessageRegistration Message(int messageIndex, int totalMessageCount, int membersAdded = 0, int membersRemoved = 0)
        {
            return new JobTrackerMessageRegistration
            {
                MessageIndex = messageIndex,
                TotalMessageCount = totalMessageCount,
                MembersToAdd = membersAdded,
                MembersToRemove = membersRemoved,
                MembersAdded = membersAdded,
                MembersRemoved = membersRemoved
            };
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_LastMessageArrives_ClaimsCompletion()
        {
            var entity = new JobTrackerEntity();

            var first = await entity.RegisterMessageAndCheckComplete(Message(1, 2));
            var second = await entity.RegisterMessageAndCheckComplete(Message(2, 2));

            Assert.IsFalse(first.IsComplete, "First message should not signal completion.");
            Assert.AreEqual(1, first.MessagesProcessed);

            Assert.IsTrue(second.IsComplete, "Final message should claim completion.");
            Assert.AreEqual(2, second.MessagesProcessed);
            Assert.AreEqual(2, second.TotalMessageCount);
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_DuplicateFinalMessage_OnlyOneCallerClaimsCompletion()
        {
            // Critical invariant: even if the same message arrives twice at the completion boundary
            // (retry, replay, or redelivery), exactly one caller observes IsComplete=true. This
            // guards against double finalization of the job.

            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 2));
            var firstClaim = await entity.RegisterMessageAndCheckComplete(Message(2, 2));
            var duplicateClaim = await entity.RegisterMessageAndCheckComplete(Message(2, 2));

            Assert.IsTrue(firstClaim.IsComplete ^ duplicateClaim.IsComplete,
                "Exactly one caller should observe IsComplete=true (XOR).");
            Assert.IsTrue(firstClaim.IsComplete, "First caller at the completion boundary should claim it.");
            Assert.IsFalse(duplicateClaim.IsComplete, "Subsequent caller must not re-claim completion.");
            Assert.AreEqual(2, duplicateClaim.MessagesProcessed,
                "Idempotent accumulation: a duplicate message index must not inflate the processed count.");
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_LateRegistration_DoesNotReClaimCompletion()
        {
            // Once completion is claimed, any further registration (including a new index that
            // pushes the count beyond TotalMessageCount) must not re-trigger finalization.

            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 2));
            var completion = await entity.RegisterMessageAndCheckComplete(Message(2, 2));
            Assert.IsTrue(completion.IsComplete);

            var lateArrival = await entity.RegisterMessageAndCheckComplete(Message(3, 2));

            Assert.IsFalse(lateArrival.IsComplete, "Post-completion registrations must not re-claim completion.");
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_TotalMessageCountOnlySetOnce()
        {
            // First-writer wins: a later caller that disagrees on TotalMessageCount must not
            // overwrite the entity's view.

            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 3));
            var second = await entity.RegisterMessageAndCheckComplete(Message(2, 99));

            Assert.AreEqual(3, second.TotalMessageCount, "TotalMessageCount must remain at its first-set value.");
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_NullRegistration_ReturnsCurrentStateSafely()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 2));
            var result = await entity.RegisterMessageAndCheckComplete(null);

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(1, result.MessagesProcessed);
            Assert.AreEqual(2, result.TotalMessageCount);
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_AccumulatesMemberTotalsAcrossMessages()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 3, membersAdded: 400, membersRemoved: 10));
            await entity.RegisterMessageAndCheckComplete(Message(2, 3, membersAdded: 400, membersRemoved: 0));
            var last = await entity.RegisterMessageAndCheckComplete(Message(3, 3, membersAdded: 200, membersRemoved: 5));

            Assert.IsTrue(last.IsComplete);
            Assert.AreEqual(1000, last.State.TotalMembersAdded);
            Assert.AreEqual(1000, last.State.TotalMembersToAdd);
            Assert.AreEqual(15, last.State.TotalMembersRemoved);
            Assert.AreEqual(15, last.State.TotalMembersToRemove);
            Assert.AreEqual(3, last.State.MessagesProcessed);
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_DuplicateMessage_DoesNotDoubleCountTotals()
        {
            // A redelivered message must contribute to the totals exactly once.
            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 2, membersAdded: 400));
            await entity.RegisterMessageAndCheckComplete(Message(1, 2, membersAdded: 400));

            var state = await entity.GetState();

            Assert.AreEqual(400, state.TotalMembersAdded, "Duplicate message index must not double-count member totals.");
            Assert.AreEqual(1, state.MessagesProcessed);
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_ReturnedStateIsSnapshot_NotAliased()
        {
            // The result carries a clone so a caller inspecting an earlier result never observes a
            // later mutation (matches production entity->orchestrator serialization semantics).
            var entity = new JobTrackerEntity();

            var first = await entity.RegisterMessageAndCheckComplete(Message(1, 2, membersAdded: 400));
            await entity.RegisterMessageAndCheckComplete(Message(2, 2, membersAdded: 400));

            Assert.AreEqual(400, first.State.TotalMembersAdded, "Earlier result snapshot must not reflect later accumulation.");
            Assert.AreEqual(1, first.State.MessagesProcessed);
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_IndexBeyondDeclaredTotal_IsRejected()
        {
            // A malformed message whose index exceeds its own declared total must be ignored so it
            // can never inflate the processed count or trip the completion gate early.
            var entity = new JobTrackerEntity();

            var result = await entity.RegisterMessageAndCheckComplete(Message(5, 3));

            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(0, result.MessagesProcessed, "An out-of-range index must not be counted.");
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_NonPositiveIndexOrTotal_IsRejected()
        {
            var entity = new JobTrackerEntity();

            var zeroIndex = await entity.RegisterMessageAndCheckComplete(Message(0, 3));
            var zeroTotal = await entity.RegisterMessageAndCheckComplete(Message(1, 0));

            Assert.IsFalse(zeroIndex.IsComplete);
            Assert.IsFalse(zeroTotal.IsComplete);
            Assert.AreEqual(0, zeroTotal.MessagesProcessed, "Self-inconsistent registrations must not be counted.");
        }

        [TestMethod]
        public async Task RegisterMessageAndCheckComplete_IndexBeyondEstablishedTotal_DoesNotClaimEarly()
        {
            // Once the run size is established at 2, a later message that disagrees (claims total 5,
            // index 4) must not be folded in — otherwise two arrivals could reach the completion
            // threshold while a genuine part (index 2) is still missing.
            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 2));
            var outOfRange = await entity.RegisterMessageAndCheckComplete(Message(4, 5));

            Assert.IsFalse(outOfRange.IsComplete, "An index beyond the established run size must not claim completion.");
            Assert.AreEqual(1, outOfRange.MessagesProcessed, "Only the valid in-range part should be counted.");
            Assert.AreEqual(2, outOfRange.TotalMessageCount, "The first-established total must stand.");
        }

        [TestMethod]
        public async Task SetIsValidGroupIfUnset_FirstWriterWins_AndPreservesAccumulatedTotals()
        {
            // Establishing group validity must mutate only IsValidGroup: the running message totals
            // accumulated by RegisterMessageAndCheckComplete must survive untouched (the regression a
            // whole-object SetState from a stale snapshot could reintroduce).
            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 2, membersAdded: 400, membersRemoved: 5));

            var firstSet = await entity.SetIsValidGroupIfUnset(true);
            var secondSet = await entity.SetIsValidGroupIfUnset(false);

            Assert.IsTrue(firstSet, "First writer establishes validity.");
            Assert.IsTrue(secondSet, "A later disagreeing writer must not flip the established value.");

            var state = await entity.GetState();
            Assert.AreEqual(400, state.TotalMembersAdded, "Setting validity must not clobber accumulated totals.");
            Assert.AreEqual(5, state.TotalMembersRemoved);
            Assert.AreEqual(1, state.MessagesProcessed);
            Assert.IsTrue(state.IsValidGroup.Value);
        }

        [TestMethod]
        public async Task GetIsValidGroup_ReturnsNullUntilEstablished()
        {
            var entity = new JobTrackerEntity();

            Assert.IsNull(await entity.GetIsValidGroup(), "Validity is unknown until first set.");

            await entity.SetIsValidGroupIfUnset(false);

            Assert.IsFalse((await entity.GetIsValidGroup()).Value);
        }

        [TestMethod]
        public async Task TryMarkCompletionSent_ClaimsOnce_AndPreservesAccumulatedTotals()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterMessageAndCheckComplete(Message(1, 2, membersAdded: 400));

            // The first caller claims the completion send; every later caller is rejected, so the
            // signal is sent at most once even under concurrent finalizers.
            Assert.IsTrue(await entity.TryMarkCompletionSent(), "The first caller must claim the completion send.");
            Assert.IsFalse(await entity.TryMarkCompletionSent(), "A second caller must not re-claim the completion send.");

            var state = await entity.GetState();
            Assert.IsTrue(state.CompletionSent);
            Assert.AreEqual(400, state.TotalMembersAdded, "Claiming completion-sent must not clobber accumulated totals.");
            Assert.AreEqual(1, state.MessagesProcessed);
        }
    }
}
