// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    [TestClass]
    public sealed class InMemoryBlobStorageRepositoryTests
    {
        [TestMethod]
        public async Task PrefixOperations_MultipleBlobs_MatchProductionSemantics()
        {
            var repository = new InMemoryBlobStorageRepository();
            await repository.UploadFileAsync("cache/group_old", "old");
            await repository.UploadFileAsync("cache/group_new", "new");
            await repository.UploadFileAsync("memberTypes/group.jsonl", "types");

            var latest = await repository.FindLatestFileAsync("cache/group");
            await repository.DeleteFilesByPrefixAsync("cache/group", excludeLatest: true);

            Assert.AreEqual("cache/group_new", latest.Path);
            CollectionAssert.AreEquivalent(
                new[] { "cache/group_new", "memberTypes/group.jsonl" },
                repository.Paths.ToArray());
        }

        [TestMethod]
        public async Task MembershipStreaming_RoundTrip_PreservesEnvelopeAndMembers()
        {
            var repository = new InMemoryBlobStorageRepository();
            var member = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                SourceGroup = Guid.NewGuid()
            };
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
                RunId = Guid.NewGuid(),
                SyncJobId = Guid.NewGuid(),
                Query = "[{\"type\":\"GroupMembership\"}]"
            };

            await repository.WriteMembershipAsync(
                "membership.json",
                envelope,
                OneMember(member));

            GroupMembership streamedEnvelope = null;
            var streamedMembers = new List<AzureADUser>();
            await foreach (var streamedMember in repository.StreamMembershipAsync(
                "membership.json",
                details => streamedEnvelope = details))
            {
                streamedMembers.Add(streamedMember);
            }

            Assert.IsNotNull(streamedEnvelope);
            Assert.AreEqual(envelope.Destination.ObjectId, streamedEnvelope.Destination.ObjectId);
            Assert.AreEqual(1, streamedMembers.Count);
            Assert.AreEqual(member.ObjectId, streamedMembers[0].ObjectId);
            Assert.AreEqual(member.SourceGroup, streamedMembers[0].SourceGroup);
        }

        [TestMethod]
        public async Task ActivityCallWrapper_ActivityThrows_ProducesTaskFailedException()
        {
            var exception = await Assert.ThrowsExceptionAsync<TaskFailedException>(
                async () => await ActivityCallWrapper.InvokeAsync(
                    "FailingActivity",
                    () => Task.FromException(new InvalidOperationException("failure"))));

            Assert.AreEqual("FailingActivity", exception.TaskName);
            StringAssert.Contains(exception.FailureDetails.ErrorMessage, "failure");
        }

        private static async IAsyncEnumerable<AzureADUser> OneMember(AzureADUser member)
        {
            await Task.CompletedTask;
            yield return member;
        }
    }
}
