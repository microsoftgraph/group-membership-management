// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework.Tests
{
    [TestClass]
    public class DatabaseSyncJobsRepositoryChannelLookupTests
    {
        private static DbContextOptions<GMMContext> OptionsFor(string dbName) =>
            new DbContextOptionsBuilder<GMMContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .EnableSensitiveDataLogging()
                .Options;

        private static DatabaseSyncJobsRepository CreateRepo(string dbName)
        {
            var options = OptionsFor(dbName);
            // Read and write contexts share the same in-memory store (same database name).
            return new DatabaseSyncJobsRepository(new GMMContext(options), new GMMReadContext(options));
        }

        private static SyncJob NewChannelJob(Guid groupId, string channelId, SyncStatus status = SyncStatus.Idle)
        {
            var jobId = Guid.NewGuid();
            return new SyncJob
            {
                Id = jobId,
                MembershipType = "TeamsChannelMembership",
                Status = status.ToString(),
                Channel = new Channel { SyncJobId = jobId, GroupId = groupId, ChannelId = channelId }
            };
        }

        private static async Task SeedAsync(string dbName, params SyncJob[] jobs)
        {
            using var seed = new GMMContext(OptionsFor(dbName));
            seed.SyncJobs.AddRange(jobs);
            await seed.SaveChangesAsync();
        }

        [TestMethod]
        public async Task ReturnsJob_ForExactGroupAndChannelPair()
        {
            var dbName = Guid.NewGuid().ToString();
            var groupId = Guid.NewGuid();
            await SeedAsync(dbName, NewChannelJob(groupId, "19:channel-A"), NewChannelJob(groupId, "19:channel-B"));

            var result = await CreateRepo(dbName).GetSyncJobByTeamIdAndChannelIdAsync(groupId, "19:channel-A");

            Assert.IsNotNull(result);
            Assert.AreEqual("19:channel-A", result.Channel.ChannelId);
        }

        [TestMethod]
        public async Task ReturnsNull_ForSameGroupDifferentChannel_NoFalsePositive()
        {
            var dbName = Guid.NewGuid().ToString();
            var groupId = Guid.NewGuid();
            await SeedAsync(dbName, NewChannelJob(groupId, "19:channel-A"));

            var result = await CreateRepo(dbName).GetSyncJobByTeamIdAndChannelIdAsync(groupId, "19:channel-DOES-NOT-EXIST");

            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ReturnsJob_EvenWhenCustomerPaused_PausedDuplicateStillDetected()
        {
            var dbName = Guid.NewGuid().ToString();
            var groupId = Guid.NewGuid();
            await SeedAsync(dbName, NewChannelJob(groupId, "19:paused-channel", SyncStatus.CustomerPaused));

            var result = await CreateRepo(dbName).GetSyncJobByTeamIdAndChannelIdAsync(groupId, "19:paused-channel");

            Assert.IsNotNull(result, "A paused duplicate destination must still be detected (spec FR-003).");
        }

        [TestMethod]
        public async Task ReturnsNull_ForNullOrEmptyChannelId()
        {
            var dbName = Guid.NewGuid().ToString();
            var repo = CreateRepo(dbName);

            Assert.IsNull(await repo.GetSyncJobByTeamIdAndChannelIdAsync(Guid.NewGuid(), null));
            Assert.IsNull(await repo.GetSyncJobByTeamIdAndChannelIdAsync(Guid.NewGuid(), string.Empty));
        }
    }
}
