// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class MembershipMergeEngineScaleTests
    {
        [TestMethod]
        public async Task BufferUseDependsOnOpenStreamsAndReadAheadNotMemberCount()
        {
            const int memberCount = 50_000;
            var diagnostics = new MembershipMergeDiagnostics();
            var repository = new Mock<IBlobStorageRepository>();
            var options = new MembershipMergeOptions
            {
                MaxStreamsPerPass = 16,
                ReadAheadSize = 2
            };
            var engine = new MembershipMergeEngine(repository.Object, Options.Create(options));
            var inputs = new[]
            {
                MembershipMergeInput.FromMemberSource(
                    "large-source",
                    MembershipMergeInputKind.Included,
                    cancellationToken => LargeMembers(memberCount, 0, cancellationToken)),
                MembershipMergeInput.FromMemberSource(
                    "large-destination",
                    MembershipMergeInputKind.Destination,
                    cancellationToken => LargeMembers(memberCount, 1, cancellationToken))
            };
            var mergedCount = 0;

            await foreach (var _ in engine.MergeRecordsAsync(inputs, diagnostics))
            {
                mergedCount++;
            }

            Assert.AreEqual(memberCount, mergedCount);
            Assert.IsTrue(diagnostics.PeakBufferedRecords <= inputs.Length * options.ReadAheadSize);
            Assert.AreEqual(inputs.Length, diagnostics.PeakOpenStreams);
        }

        [TestMethod]
        public async Task HighFanInUsesHierarchicalPassesAndMatchesASinglePass()
        {
            const int fanIn = 75;
            var hierarchicalStore = new MembershipMergeTestStore();
            var singlePassStore = new MembershipMergeTestStore();
            var inputs = Enumerable.Range(0, fanIn)
                .Select(index => MembershipMergeInput.FromMembers(
                    $"source-{index}",
                    MembershipMergeInputKind.Included,
                    new[]
                    {
                        new AzureADUser
                        {
                            ObjectId = ParseGuid(index % 25 + 1),
                            SourceGroup = ParseGuid(10_000 + index)
                        }
                    }))
                .ToArray();
            var request = new MembershipMergeRequest(
                inputs,
                "hierarchical-output",
                "run-75",
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Envelope());
            var diagnostics = new MembershipMergeDiagnostics();
            var hierarchical = new MembershipMergeEngine(
                hierarchicalStore.Repository.Object,
                Options.Create(new MembershipMergeOptions
                {
                    MaxStreamsPerPass = 16,
                    ReadAheadSize = 1
                }));
            var singlePass = new MembershipMergeEngine(
                singlePassStore.Repository.Object,
                Options.Create(new MembershipMergeOptions
                {
                    MaxStreamsPerPass = 100,
                    ReadAheadSize = 1
                }));

            var hierarchicalResult = await hierarchical.ExecuteAsync(request, diagnostics);
            var singlePassResult = await singlePass.ExecuteAsync(request);

            Assert.IsTrue(diagnostics.UsedHierarchicalMerge);
            Assert.IsTrue(diagnostics.PassCount >= 2);
            Assert.IsTrue(diagnostics.PeakOpenStreams <= 16);
            Assert.IsTrue(diagnostics.PeakBufferedRecords <= 16);
            Assert.AreEqual(25, hierarchicalResult.SourceMemberCount);
            Assert.AreEqual(25, hierarchicalResult.AddCount);
            Assert.AreEqual(0, hierarchicalResult.RemoveCount);
            Assert.AreEqual(singlePassResult.SourceMemberCount, hierarchicalResult.SourceMemberCount);
            Assert.AreEqual(singlePassResult.AddCount, hierarchicalResult.AddCount);
            Assert.AreEqual(singlePassResult.RemoveCount, hierarchicalResult.RemoveCount);
            CollectionAssert.AreEqual(
                singlePassStore.ReadBytes(singlePassResult.AdditionsPath),
                hierarchicalStore.ReadBytes(hierarchicalResult.AdditionsPath));
            CollectionAssert.AreEqual(
                singlePassStore.ReadBytes(singlePassResult.RemovalsPath),
                hierarchicalStore.ReadBytes(hierarchicalResult.RemovalsPath));
            Assert.IsFalse(hierarchicalStore.Operations.Any(operation =>
                operation.StartsWith("write:", StringComparison.Ordinal)
                && operation.Contains(".intermediate.", StringComparison.Ordinal)
                && !hierarchicalStore.Operations.Contains(operation.Replace("write:", "delete:"))));
            Assert.IsFalse(hierarchicalStore.Operations.Any(operation =>
                operation.StartsWith("write:", StringComparison.Ordinal)
                && operation.Contains(".intermediate.", StringComparison.Ordinal)
                && !operation.EndsWith(".Included.json", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task MaximumDirectFanInSkipsRemovalPassWhenNoRemovalsExist()
        {
            const int fanIn = MembershipMergeOptions.DefaultMaxStreamsPerPass;
            var store = new MembershipMergeTestStore();
            var openCount = 0;
            var inputs = Enumerable.Range(0, fanIn)
                .Select(index => MembershipMergeInput.FromMemberSource(
                    $"source-{index}",
                    MembershipMergeInputKind.Included,
                    cancellationToken =>
                    {
                        Interlocked.Increment(ref openCount);
                        return SingleMember(index + 1, cancellationToken);
                    }))
                .ToArray();
            var diagnostics = new MembershipMergeDiagnostics();

            var result = await new MembershipMergeEngine(
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions())).ExecuteAsync(
                    new MembershipMergeRequest(
                        inputs,
                        "direct-output",
                        "run-direct",
                        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                        Envelope()),
                    diagnostics);

            Assert.AreEqual(fanIn, openCount);
            Assert.AreEqual(fanIn, result.SourceMemberCount);
            Assert.AreEqual(fanIn, result.AddCount);
            Assert.IsFalse(diagnostics.UsedHierarchicalMerge);
        }

        [TestMethod]
        public async Task LargeSerializedPartStreamsAtTheObservedFailureScale()
        {
            const int memberCount = 50_000;
            const int minimumPartBytes = 12_600_000;
            var store = new MembershipMergeTestStore();
            var payload = new string('x', 180);
            var members = Enumerable.Range(1, memberCount)
                .Select(index => new AzureADUser
                {
                    ObjectId = ParseGuid(index),
                    SourceGroup = ParseGuid(900_000),
                    DisplayName = payload
                })
                .ToArray();
            store.AddRaw("large-source", Envelope(), members);
            var request = new MembershipMergeRequest(
                new[]
                {
                    MembershipMergeInput.FromPath(
                        "large-source",
                        MembershipMergeInputKind.Included)
                },
                "large-output",
                "run-large",
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Envelope());

            var result = await new MembershipMergeEngine(
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions())).ExecuteAsync(request);

            Assert.IsTrue(store.GetMembershipLength("large-source") >= minimumPartBytes);
            Assert.AreEqual(memberCount, result.SourceMemberCount);
            Assert.AreEqual(memberCount, result.AddCount);
        }

        [TestMethod]
        public async Task LargestRepresentativeMergeStaysBelowFunctionTimeout()
        {
            const int memberCount = 200_000;
            var store = new MembershipMergeTestStore();
            var request = new MembershipMergeRequest(
                new[]
                {
                    MembershipMergeInput.FromMemberSource(
                        "largest-source",
                        MembershipMergeInputKind.Included,
                        cancellationToken => LargeMembers(
                            memberCount,
                            0,
                            cancellationToken))
                },
                "largest-output",
                "run-largest",
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Envelope());
            var stopwatch = Stopwatch.StartNew();

            var result = await new MembershipMergeEngine(
                store.Repository.Object,
                Options.Create(new MembershipMergeOptions())).ExecuteAsync(request);

            stopwatch.Stop();
            Assert.AreEqual(memberCount, result.SourceMemberCount);
            Assert.AreEqual(memberCount, result.AddCount);
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromMinutes(10),
                $"The merge took {stopwatch.Elapsed}.");
        }

        [TestMethod]
        public void InvalidBufferSettingsAreRejected()
        {
            var repository = new Mock<IBlobStorageRepository>();

            Assert.ThrowsException<OptionsValidationException>(() =>
                new MembershipMergeEngine(
                    repository.Object,
                    Options.Create(new MembershipMergeOptions
                    {
                        MaxStreamsPerPass = 3,
                        ReadAheadSize = 0
                    })));
        }

        private static async IAsyncEnumerable<AzureADUser> LargeMembers(
            int count,
            int offset,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var payload = new string('x', 180);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new AzureADUser
                {
                    ObjectId = ParseGuid(index + 1),
                    SourceGroup = ParseGuid(900_000 + offset),
                    DisplayName = payload
                };
            }

            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<AzureADUser> SingleMember(
            int value,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AzureADUser
            {
                ObjectId = ParseGuid(value),
                SourceGroup = ParseGuid(900_000 + value)
            };

            await Task.CompletedTask;
        }

        private static GroupMembership Envelope() => new GroupMembership
        {
            Destination = new AzureADGroup { ObjectId = ParseGuid(999) },
            RunId = ParseGuid(998),
            SyncJobId = ParseGuid(997),
            SourceMembers = new List<AzureADUser>()
        };

        private static Guid ParseGuid(int value) =>
            Guid.Parse($"00000000-0000-0000-0000-{value:X12}");
    }
}
