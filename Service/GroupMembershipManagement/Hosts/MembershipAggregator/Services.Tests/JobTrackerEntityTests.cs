// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
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
                FilePath = "/part2.json",
                IsDestinationPart = true
            });

            var duplicateClaim = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/part2.json",
                IsDestinationPart = true
            });

            Assert.IsTrue(firstClaim.IsComplete ^ duplicateClaim.IsComplete,
                "Exactly one caller should observe IsComplete=true (XOR).");
            Assert.IsTrue(firstClaim.IsComplete,
                "First caller at completion boundary should claim it.");
            Assert.IsFalse(duplicateClaim.IsComplete,
                "Subsequent caller must not re-claim completion.");
            Assert.IsFalse(firstClaim.RegistrationRejected);
            Assert.IsFalse(duplicateClaim.RegistrationRejected);
            Assert.IsNull(firstClaim.RegistrationRejectionReason);
            Assert.IsNull(duplicateClaim.RegistrationRejectionReason);
            Assert.AreEqual(2, duplicateClaim.CompletedCount,
                "CompletedCount should remain stable after completion claimed.");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_OutOfRangeLateRegistration_IsRejected()
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
                FilePath = "/part2.json",
                IsDestinationPart = true
            });
            Assert.IsTrue(completion.IsComplete);

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 3,
                TotalParts = 2,
                FilePath = "/part3.json"
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "PartNumber=3");
            var replay = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/part2.json",
                IsDestinationPart = true
            });
            Assert.IsTrue(replay.RegistrationRejected);
            Assert.IsFalse(replay.IsComplete);
            Assert.AreEqual(rejection.RegistrationRejectionReason, replay.RegistrationRejectionReason);
            Assert.AreEqual(2, replay.CompletedCount);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_TotalPartsMismatch_RejectsRunAtomically()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 3,
                FilePath = "/part1.json"
            });

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 99,
                FilePath = "/part2.json"
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "part 2");
            StringAssert.Contains(rejection.RegistrationRejectionReason, "expected 3");
            var replay = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 3,
                FilePath = "/part1.json"
            });
            Assert.IsTrue(replay.RegistrationRejected);
            Assert.IsFalse(replay.IsComplete);
            Assert.AreEqual(rejection.RegistrationRejectionReason, replay.RegistrationRejectionReason);
            Assert.AreEqual(3, replay.TotalParts);
            Assert.AreEqual(1, replay.CompletedCount);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_DifferentPartUsingExistingPath_RejectsRunAtomically()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/source.json"
            });

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/source.json",
                IsDestinationPart = true
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "part 2");
            StringAssert.Contains(rejection.RegistrationRejectionReason, "part 1");
            StringAssert.Contains(rejection.RegistrationRejectionReason, "/source.json");
            var laterPart = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });
            Assert.IsTrue(laterPart.RegistrationRejected);
            Assert.IsFalse(laterPart.IsComplete);
            Assert.AreEqual(rejection.RegistrationRejectionReason, laterPart.RegistrationRejectionReason);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_ExistingStateWithDuplicatePaths_IsRejected()
        {
            var entity = new TestableJobTrackerEntity();
            entity.SetState(new JobState
            {
                TotalParts = 3,
                CompletedParts = new Dictionary<int, string>
                {
                    [1] = "/duplicate.json",
                    [2] = "/duplicate.json"
                }
            });

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 3,
                TotalParts = 3,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "parts 1 and 2");
            StringAssert.Contains(rejection.RegistrationRejectionReason, "/duplicate.json");
            var replay = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 3,
                TotalParts = 3,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });
            Assert.IsTrue(replay.RegistrationRejected);
            Assert.AreEqual(rejection.RegistrationRejectionReason, replay.RegistrationRejectionReason);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_ExistingStateWithNegativeTotalParts_IsRejected()
        {
            await AssertStateRejectedAsync(
                new JobState { TotalParts = -1 },
                "TotalParts=-1");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_UninitializedStateWithLifecycleData_IsRejected()
        {
            await AssertStateRejectedAsync(
                new JobState
                {
                    CompletedParts = new Dictionary<int, string>
                    {
                        [1] = "/source.json"
                    }
                },
                "without an initialized total-parts value");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_CompleteStateWithoutDestination_IsRejected()
        {
            await AssertStateRejectedAsync(
                new JobState
                {
                    TotalParts = 2,
                    CompletedParts = new Dictionary<int, string>
                    {
                        [1] = "/source.json",
                        [2] = "/other-source.json"
                    }
                },
                "without a destination part");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_CompleteStateWithoutClaim_IsRejected()
        {
            await AssertStateRejectedAsync(
                new JobState
                {
                    TotalParts = 2,
                    CompletedParts = new Dictionary<int, string>
                    {
                        [1] = "/source.json",
                        [2] = "/destination.json"
                    },
                    DestinationPart = "/destination.json"
                },
                "completion claim is inconsistent");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_IncompleteStateWithClaim_IsRejected()
        {
            await AssertStateRejectedAsync(
                new JobState
                {
                    TotalParts = 2,
                    CompletedParts = new Dictionary<int, string>
                    {
                        [1] = "/source.json"
                    },
                    CompletionClaimed = true
                },
                "completion claim is inconsistent");
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_ConflictingPartPath_RejectsRunAtomically()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/part1.json"
            });

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/different-part1.json"
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "part 1");
            StringAssert.Contains(rejection.RegistrationRejectionReason, "/part1.json");
            StringAssert.Contains(rejection.RegistrationRejectionReason, "/different-part1.json");
            var replay = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/part1.json"
            });
            Assert.IsTrue(replay.RegistrationRejected);
            Assert.IsFalse(replay.IsComplete);
            Assert.AreEqual(rejection.RegistrationRejectionReason, replay.RegistrationRejectionReason);
            Assert.AreEqual(1, replay.CompletedCount);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_SecondDestinationPart_RejectsRunAtomically()
        {
            var entity = new JobTrackerEntity();

            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/second-destination.json",
                IsDestinationPart = true
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "part 2");
            StringAssert.Contains(rejection.RegistrationRejectionReason, "/destination.json");
            var samePathRejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });
            Assert.IsTrue(samePathRejection.RegistrationRejected);
            Assert.AreEqual(
                rejection.RegistrationRejectionReason,
                samePathRejection.RegistrationRejectionReason);

            var laterSourcePart = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/source.json"
            });
            Assert.IsTrue(laterSourcePart.RegistrationRejected);
            Assert.IsFalse(laterSourcePart.IsComplete);
            Assert.AreEqual(rejection.RegistrationRejectionReason, laterSourcePart.RegistrationRejectionReason);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_ExactDuplicateBeforeCompletion_IsAccepted()
        {
            var entity = new JobTrackerEntity();
            var registration = new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/source.json"
            };

            var first = await entity.RegisterPartAndCheckComplete(registration);
            var replay = await entity.RegisterPartAndCheckComplete(registration);

            Assert.IsFalse(first.IsComplete);
            Assert.IsFalse(replay.IsComplete);
            Assert.IsFalse(first.RegistrationRejected);
            Assert.IsFalse(replay.RegistrationRejected);
            Assert.IsNull(first.RegistrationRejectionReason);
            Assert.IsNull(replay.RegistrationRejectionReason);
            Assert.AreEqual(1, replay.CompletedCount);
            Assert.AreEqual(2, replay.TotalParts);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_ReturnedSnapshotCannotMutateEntityState()
        {
            var entity = new JobTrackerEntity();
            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/source.json"
            });
            var completion = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });

            completion.CompletedParts[1] = "/changed-by-caller.json";

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/different-source.json"
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "/source.json");
            Assert.IsFalse(
                rejection.RegistrationRejectionReason.Contains(
                    "/changed-by-caller.json",
                    StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_AllPartsWithoutDestination_IsRejected()
        {
            var entity = new JobTrackerEntity();
            await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 2,
                FilePath = "/source1.json"
            });

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 2,
                TotalParts = 2,
                FilePath = "/source2.json"
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "destination");
            var correctedRegistration = await entity.RegisterPartAndCheckComplete(
                new JobTrackerRegistration
                {
                    PartNumber = 2,
                    TotalParts = 2,
                    FilePath = "/source2.json",
                    IsDestinationPart = true
                });
            Assert.IsTrue(correctedRegistration.RegistrationRejected);
            Assert.IsFalse(correctedRegistration.IsComplete);
            Assert.AreEqual(
                rejection.RegistrationRejectionReason,
                correctedRegistration.RegistrationRejectionReason);
        }

        [TestMethod]
        public async Task RegisterPartAndCheckComplete_NullRegistration_IsRejected()
        {
            var entity = new JobTrackerEntity();

            var rejection = await entity.RegisterPartAndCheckComplete(null);

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, "registration");
            var laterRegistration = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = 1,
                FilePath = "/destination.json",
                IsDestinationPart = true
            });
            Assert.IsTrue(laterRegistration.RegistrationRejected);
            Assert.IsFalse(laterRegistration.IsComplete);
            Assert.AreEqual(
                rejection.RegistrationRejectionReason,
                laterRegistration.RegistrationRejectionReason);
        }

        private static async Task AssertStateRejectedAsync(JobState state, string expectedMessage)
        {
            var entity = new TestableJobTrackerEntity();
            entity.SetState(state);

            var rejection = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = state.TotalParts > 0 ? state.TotalParts : 1,
                FilePath = "/incoming.json"
            });

            Assert.IsTrue(rejection.RegistrationRejected);
            StringAssert.Contains(rejection.RegistrationRejectionReason, expectedMessage);
            var replay = await entity.RegisterPartAndCheckComplete(new JobTrackerRegistration
            {
                PartNumber = 1,
                TotalParts = state.TotalParts > 0 ? state.TotalParts : 1,
                FilePath = "/incoming.json"
            });
            Assert.IsTrue(replay.RegistrationRejected);
            Assert.IsFalse(replay.IsComplete);
            Assert.AreEqual(rejection.RegistrationRejectionReason, replay.RegistrationRejectionReason);
        }

        private sealed class TestableJobTrackerEntity : JobTrackerEntity
        {
            public void SetState(JobState state)
            {
                State = state;
            }
        }
    }
}
