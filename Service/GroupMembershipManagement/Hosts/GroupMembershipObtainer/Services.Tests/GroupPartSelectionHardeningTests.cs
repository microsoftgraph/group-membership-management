// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.FailureModel;
using Tests.Services.Infrastructure;
using static Tests.Services.FailureModel.SelectionPipelineAcceptanceTests;

namespace Tests.Services
{
    /// <summary>
    /// Slice H hardening of the one-type selection pipeline: finalize's saved-delta array sizing and cleanup's host
    /// cancellation. (The activity deadlines and the lookup-capability marker were removed by the owner, 2026-10-21.)
    /// </summary>
    [TestClass]
    public sealed class GroupPartSelectionHardeningTests
    {
        private const string Category = "SelectionHardening";

        // ---------------------------------------------------------------- finalize sizing

        /// <summary>
        /// A saved-delta staging blob that lists one identifier twice publishes each selected identifier once and never
        /// a trailing <see cref="Guid.Empty"/>.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task DeltaLinkFinalize_DuplicateStagingId_PublishesNoEmptyIdentifier()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var person = Member(sourceGroupId);
            var other = Member(sourceGroupId);
            var agent = Member(sourceGroupId);
            pipeline.DirectoryTypes[person.ObjectId] = "user";
            pipeline.DirectoryTypes[other.ObjectId] = "user";
            pipeline.DirectoryTypes[agent.ObjectId] = "agentUser";
            var request = await PipelineRequestAsync(
                storage,
                GroupMembershipReadKind.DeltaLink,
                new[] { "users" },
                new[] { person, other, person, agent },
                sourceGroupId);

            var result = await RunPipelineAsync(pipeline, request);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status, result.Outcome.ToString());
            var ids = await ReadIdsAsync(storage, result.FilePath);
            CollectionAssert.DoesNotContain(ids, Guid.Empty, "The part held an empty identifier.");
            CollectionAssert.AreEquivalent(new[] { person.ObjectId, other.ObjectId }, ids);
        }

        /// <summary>
        /// Slice I: a staging blob that lists one identifier twice reports, for both read kinds, the member count of the
        /// part actually written (unique selected identifiers), never the staging record count, in the result and in the
        /// selection-completed log entry.
        /// </summary>
        [DataTestMethod]
        [DataRow((int)GroupMembershipReadKind.DeltaInitial)]
        [DataRow((int)GroupMembershipReadKind.DeltaLink)]
        [TestCategory(Category)]
        public async Task Finalize_DuplicateStagingId_ReportsWrittenMemberCount(int readKindValue)
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var sourceGroupId = Guid.NewGuid();
            var person = Member(sourceGroupId);
            var other = Member(sourceGroupId);
            var agent = Member(sourceGroupId);
            pipeline.DirectoryTypes[person.ObjectId] = "user";
            pipeline.DirectoryTypes[other.ObjectId] = "user";
            pipeline.DirectoryTypes[agent.ObjectId] = "agentUser";
            var request = await PipelineRequestAsync(
                storage,
                (GroupMembershipReadKind)readKindValue,
                new[] { "users" },
                new[] { person, other, person, agent },
                sourceGroupId);

            var result = await RunPipelineAsync(pipeline, request);

            Assert.AreEqual(SelectionOutcomeStatus.Completed, result.Outcome.Status, result.Outcome.ToString());
            var ids = await ReadIdsAsync(storage, result.FilePath);
            Assert.AreEqual(2, ids.Count, "The part should hold each selected identifier once.");
            Assert.AreEqual(ids.Count, result.MemberCount, "MemberCount differs from the members in the part file.");
            Assert.AreEqual(ids.Count, result.UserCount, "UserCount differs from the members in the part file.");
            var completed = pipeline.FinalizeLogger.Entries.Single(entry => entry.EventId.Id == 150183);
            StringAssert.StartsWith(completed.Message, $"Selected {ids.Count} members", completed.Message);
        }

        /// <summary>Host cancellation of cleanup still propagates (it is not a deadline).</summary>
        [TestMethod]
        [TestCategory(Category)]
        public async Task Cleanup_HostCancellation_Propagates()
        {
            var storage = new InMemoryBlobStorageRepository();
            var pipeline = new SelectionPipelineHarness(storage);
            var member = Member(Guid.NewGuid());
            var request = await PipelineRequestAsync(storage, GroupMembershipReadKind.DeltaInitial, new[] { "users" }, new[] { member }, Guid.NewGuid());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => pipeline.Cleanup.RunAsync(CleanupRequest(request), cancelled.Token));
        }
    }
}
