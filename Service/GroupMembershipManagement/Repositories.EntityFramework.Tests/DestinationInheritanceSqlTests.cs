// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Contracts.DestinationResolution;
using Repositories.EntityFramework.Contexts;
using System;
using System.Threading.Tasks;

namespace Repositories.EntityFramework.Tests
{
    [TestClass]
    [TestCategory("SqlServer")]
    public class DestinationInheritanceSqlTests
    {
        private DbContextOptions<GMMContext> _options = null!;

        [TestInitialize]
        public async Task CreateDatabase()
        {
            var connectionString = Environment.GetEnvironmentVariable("GMM_DESTINATION_TEST_CONNECTION");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.Inconclusive("Set GMM_DESTINATION_TEST_CONNECTION to a disposable SQL Server/LocalDB server connection.");
            }

            // Never operate on the supplied database: every test owns a uniquely named database.
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = $"GmmDestinationInheritance_{Guid.NewGuid():N}",
                AttachDBFilename = string.Empty
            };
            _options = new DbContextOptionsBuilder<GMMContext>().UseSqlServer(builder.ConnectionString).Options;
            using var context = new GMMContext(_options);
            await context.Database.EnsureCreatedAsync();

            // Recreate destinations with the actual released migration, including its by-name
            // MembershipTypes FK (the historical model does not describe that FK/alternate key).
            await context.Database.ExecuteSqlRawAsync(@"
DROP TABLE [TeamsChannelDestinations];
DROP TABLE [GroupDestinations];
DROP TABLE [Destinations];
ALTER TABLE [MembershipTypes] ALTER COLUMN [Name] nvarchar(255) NOT NULL;
ALTER TABLE [MembershipTypes] ADD CONSTRAINT [AK_MembershipTypes_Name] UNIQUE ([Name]);");
            var migration = new Contexts.Migrations.add_consolidated_destinations_tables();
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate(
                migration.UpOperations, context.GetService<IDesignTimeModel>().Model);
            foreach (var command in commands)
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            context.MembershipTypes.AddRange(
                new MembershipType { Id = Guid.NewGuid(), Name = MembershipTypes.GroupMembership },
                new MembershipType { Id = Guid.NewGuid(), Name = MembershipTypes.TeamsChannelMembership });
            await context.SaveChangesAsync();
        }

        [TestCleanup]
        public async Task DeleteDatabase()
        {
            if (_options != null)
            {
                using var context = new GMMContext(_options);
                await context.Database.EnsureDeletedAsync();
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task PolymorphicGraph_PersistsAndLoadsAgainstReleasedSchema(bool isChannel)
        {
            var objectId = Guid.NewGuid();
            var job = new SyncJob
            {
                TargetOfficeGroupId = objectId,
                MembershipType = isChannel ? "TeamsChannelMembership" : "GroupMembership",
                Destination = "unchanged-legacy-json",
                Group = isChannel ? null : new Group { GroupId = objectId },
                Channel = isChannel ? new Channel { GroupId = objectId, ChannelId = "19:test" } : null,
                DestinationRecord = isChannel
                    ? new TeamsChannelDestination
                    {
                        TeamId = objectId, ChannelId = "19:test", TeamName = "Team", ChannelName = "Channel"
                    }
                    : new GroupDestination { GroupId = objectId, Name = "Group", Email = "group@contoso.com" }
            };
            using (var write = new GMMContext(_options))
            {
                using var readContext = new GMMReadContext(_options);
                await new DatabaseSyncJobsRepository(write, readContext).CreateSyncJobAsync(job);
            }

            using var read = new GMMReadContext(_options);
            var loaded = await read.SyncJobs.Include(j => j.DestinationRecord)
                .Include(j => j.Group).Include(j => j.Channel).SingleAsync(j => j.Id == job.Id);
            Assert.AreEqual(job.Id, loaded.DestinationRecord.SyncJobId);
            Assert.AreEqual(job.MembershipType, loaded.DestinationRecord.DestinationType);
            Assert.AreEqual(job.Destination, loaded.Destination);
            var resolved = await new DestinationEntityResolver(read).ResolveAsync(loaded);
            if (isChannel)
            {
                Assert.IsInstanceOfType<TeamsChannelDestination>(loaded.DestinationRecord);
                var channel = (TeamsChannelDestination)loaded.DestinationRecord;
                Assert.AreEqual(loaded.Channel.GroupId, channel.TeamId);
                Assert.AreEqual(loaded.Channel.ChannelId, channel.ChannelId);
                Assert.AreEqual("Team", channel.TeamName);
                Assert.AreEqual("Channel", channel.ChannelName);
                Assert.IsInstanceOfType<ResolvedTeamsChannelDestination>(resolved);
                Assert.AreEqual(objectId, ((ResolvedTeamsChannelDestination)resolved).TeamObjectId);
                Assert.AreEqual("19:test", ((ResolvedTeamsChannelDestination)resolved).ChannelId);
            }
            else
            {
                Assert.IsInstanceOfType<GroupDestination>(loaded.DestinationRecord);
                var group = (GroupDestination)loaded.DestinationRecord;
                Assert.AreEqual(loaded.Group.GroupId, group.GroupId);
                Assert.AreEqual("Group", group.Name);
                Assert.AreEqual("group@contoso.com", group.Email);
                Assert.IsInstanceOfType<ResolvedGroupDestination>(resolved);
                Assert.AreEqual(objectId, ((ResolvedGroupDestination)resolved).ObjectId);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Materialization_RejectsStoredTypeThatConflictsWithClrSubtype(bool isChannel)
        {
            using var write = new GMMContext(_options);
            var job = new SyncJob
            {
                DestinationRecord = isChannel
                    ? new TeamsChannelDestination { TeamId = Guid.NewGuid(), ChannelId = "19:test" }
                    : new GroupDestination { GroupId = Guid.NewGuid() }
            };
            write.SyncJobs.Add(job);
            await write.SaveChangesAsync();
            var wrongType = isChannel ? "GroupMembership" : "TeamsChannelMembership";
            await write.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Destinations] SET [DestinationType] = {wrongType} WHERE [SyncJobId] = {job.Id}");

            using var read = new GMMReadContext(_options);
            Assert.IsNull(await new DestinationEntityResolver(read).ResolveAsync(job));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                read.Destinations.AsNoTracking().SingleAsync(d => d.SyncJobId == job.Id));
        }

        [DataTestMethod]
        [DataRow("missing-base")]
        [DataRow("missing-details")]
        [DataRow("wrong-group-type")]
        [DataRow("wrong-channel-type")]
        [DataRow("both-details")]
        [DataRow("empty-channel")]
        public async Task Resolver_RejectsMalformedRowsWithoutMaterializingEntities(string structure)
        {
            using var write = new GMMContext(_options);
            var job = new SyncJob { MembershipType = "GroupMembership" };
            write.SyncJobs.Add(job);
            await write.SaveChangesAsync();
            var type = structure is "wrong-channel-type" or "empty-channel"
                ? "TeamsChannelMembership" : "GroupMembership";
            if (structure != "missing-base")
            {
                await write.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [Destinations] ([SyncJobId], [DestinationType]) VALUES ({job.Id}, {type})");
            }
            if (structure is "wrong-channel-type" or "both-details")
            {
                await write.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [GroupDestinations] ([SyncJobId], [GroupId]) VALUES ({job.Id}, {Guid.NewGuid()})");
            }
            if (structure is "wrong-group-type" or "both-details" or "empty-channel")
            {
                var channelId = structure == "empty-channel" ? "" : "19:test";
                await write.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [TeamsChannelDestinations] ([SyncJobId], [TeamId], [ChannelId]) VALUES ({job.Id}, {Guid.NewGuid()}, {channelId})");
            }

            using var read = new GMMReadContext(_options);
            Assert.IsNull(await new DestinationEntityResolver(read).ResolveAsync(job));
            Assert.AreEqual(0, read.ChangeTracker.Entries().Count());
        }
    }
}
