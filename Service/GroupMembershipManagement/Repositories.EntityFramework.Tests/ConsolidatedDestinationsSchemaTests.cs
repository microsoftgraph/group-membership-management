// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.EntityFramework.Contexts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.EntityFramework.Tests
{
    /// <summary>
    /// Guards the released additive schema, schema-neutral TPT inheritance, polymorphic graph
    /// persistence, and parity between write (<see cref="GMMContext"/>) and read
    /// (<see cref="GMMReadContext"/>) models.
    /// </summary>
    [TestClass]
    public class ConsolidatedDestinationsSchemaTests
    {
        private static GMMContext CreateContext(string db)
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseSqlServer($"Server=(unused);Database={db};Trusted_Connection=True;")
                .Options;
            return new GMMContext(options);
        }

        [TestMethod]
        public void Migration_IsAdditiveOnly_CreatingOnlyTheThreeNewTables()
        {
            var migration = new Contexts.Migrations.add_consolidated_destinations_tables();
            var up = migration.UpOperations;

            var createdTables = up.OfType<CreateTableOperation>().Select(o => o.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "Destinations", "GroupDestinations", "TeamsChannelDestinations" }, createdTables);

            Assert.IsFalse(up.OfType<DropTableOperation>().Any(), "Up must not drop tables.");
            Assert.IsFalse(up.OfType<DropColumnOperation>().Any(), "Up must not drop columns.");
            Assert.IsFalse(up.OfType<AlterColumnOperation>().Any(), "Up must not alter existing columns.");
            Assert.IsFalse(up.OfType<InsertDataOperation>().Any(), "Up must not seed unrelated data.");
            Assert.IsFalse(up.OfType<DeleteDataOperation>().Any(), "Up must not delete unrelated data.");
        }

        [TestMethod]
        public void Backfill_ExcludesNullChannelIdsFromBaseAndDetailInserts()
        {
            var migration = new Contexts.Migrations.backfill_consolidated_destinations();
            var statements = migration.UpOperations.OfType<SqlOperation>().Select(o => o.Sql).ToArray();

            Assert.AreEqual(3, statements.Length);
            StringAssert.Contains(statements[0],
                "EXISTS (SELECT 1 FROM [TeamsChannels] c WHERE c.[SyncJobId] = s.[Id] AND c.[ChannelId] IS NOT NULL)");
            StringAssert.Contains(statements[2], "WHERE c.[ChannelId] IS NOT NULL");
        }

        [TestMethod]
        public void ReadAndWriteContexts_HaveIdenticalModelShape()
        {
            using var write = CreateContext("parity");
            using var read = new GMMReadContext(new DbContextOptionsBuilder<GMMContext>()
                .UseSqlServer("Server=(unused);Database=parity;Trusted_Connection=True;").Options);

            var writeEntities = write.Model.GetEntityTypes().Select(e => e.Name).OrderBy(n => n).ToArray();
            var readEntities = read.Model.GetEntityTypes().Select(e => e.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(writeEntities, readEntities,
                "Read and write contexts expose different entity types.");

            foreach (var writeEntity in write.Model.GetEntityTypes())
            {
                var readEntity = read.Model.FindEntityType(writeEntity.Name);
                Assert.IsNotNull(readEntity, $"Entity {writeEntity.Name} missing from read context.");
                Assert.AreEqual(Fingerprint(writeEntity), Fingerprint(readEntity!),
                    $"Model shape for {writeEntity.Name} differs between read and write contexts.");
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task DestinationNavigations_SaveGraphAndLoadSelectedDetails(bool isChannel)
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var type = isChannel ? MembershipTypes.TeamsChannelMembership : MembershipTypes.GroupMembership;
            var job = new SyncJob
            {
                MembershipType = type.ToString(),
                DestinationRecord = isChannel
                    ? new TeamsChannelDestination
                    {
                        TeamId = Guid.NewGuid(), ChannelId = "channel-id", ChannelName = "Channel A"
                    }
                    : new GroupDestination
                    {
                        GroupId = Guid.NewGuid(), Name = "Group A"
                    }
            };

            using (var write = new GMMContext(options))
            {
                write.MembershipTypes.Add(new MembershipType { Id = Guid.NewGuid(), Name = type });
                using var readContext = new GMMReadContext(options);
                var id = await new DatabaseSyncJobsRepository(write, readContext).CreateSyncJobAsync(job);
                Assert.AreEqual(job.Id, id);
            }

            using var read = new GMMReadContext(options);
            var loaded = await read.SyncJobs
                .Include(j => j.DestinationRecord)
                .SingleAsync(j => j.Id == job.Id);

            Assert.AreNotEqual(Guid.Empty, loaded.Id);
            Assert.IsNotNull(loaded.DestinationRecord);
            Assert.AreEqual(loaded.Id, loaded.DestinationRecord.SyncJobId);
            Assert.AreEqual(type.ToString(), loaded.DestinationRecord.DestinationType);
            if (isChannel)
            {
                var channel = loaded.DestinationRecord as TeamsChannelDestination;
                Assert.IsNotNull(channel);
                Assert.AreEqual(((TeamsChannelDestination)job.DestinationRecord).TeamId, channel.TeamId);
                Assert.AreEqual("channel-id", channel.ChannelId);
                Assert.AreEqual("Channel A", channel.ChannelName);
            }
            else
            {
                var group = loaded.DestinationRecord as GroupDestination;
                Assert.IsNotNull(group);
                Assert.AreEqual(((GroupDestination)job.DestinationRecord).GroupId, group.GroupId);
                Assert.AreEqual("Group A", group.Name);
            }
        }

        [TestMethod]
        public void DestinationType_IsSetBySubtypeAndRejectsConflictingUpdates()
        {
            using var context = CreateContext("inheritance");
            Assert.AreEqual("GroupMembership", new GroupDestination().DestinationType);
            Assert.AreEqual("TeamsChannelMembership", new TeamsChannelDestination().DestinationType);
            Assert.ThrowsException<InvalidOperationException>(() =>
                context.Entry(new GroupDestination()).Property(d => d.DestinationType).CurrentValue = "TeamsChannelMembership");
        }

        [TestMethod]
        public void InheritanceMapping_HasNoSchemaChangesAgainstReleasedCompositionModel()
        {
            using var context = CreateContext("schema-diff");
            var baseline = new Contexts.Migrations.rename_securitygroupnotfound_to_sourcegroupnotfound().TargetModel;
            var initialized = context.GetService<IModelRuntimeInitializer>().Initialize(baseline, designTime: true);
            var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(
                initialized.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

            // Existing model creation uses Guid.NewGuid()/UtcNow for seed data and this default.
            // Those pre-existing differences are unrelated to inheritance; no DDL for any other
            // column, table, key, foreign key, or index is permitted.
            var schemaChanges = differences.Where(op =>
                op is not InsertDataOperation and not DeleteDataOperation and not UpdateDataOperation
                && !(op is AlterColumnOperation column
                    && column.Table == "SyncJobChanges" && column.Name == "ChangeTime"
                    && column.ColumnType == column.OldColumn.ColumnType
                    && column.IsNullable == column.OldColumn.IsNullable
                    && column.DefaultValue is DateTime && column.OldColumn.DefaultValue is DateTime)).ToArray();
            Assert.AreEqual(0, schemaChanges.Length,
                string.Join(", ", schemaChanges.Select(op => op.GetType().Name)));
        }

        [TestMethod]
        public void InheritanceSnapshot_IsRelationallyIdenticalToReleasedCompositionModel()
        {
            using var context = CreateContext("snapshot-diff");
            var initializer = context.GetService<IModelRuntimeInitializer>();
            var baseline = initializer.Initialize(
                new Contexts.Migrations.rename_securitygroupnotfound_to_sourcegroupnotfound().TargetModel,
                designTime: true);
            var snapshot = initializer.Initialize(
                context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
            var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(
                baseline.GetRelationalModel(), snapshot.GetRelationalModel());
            Assert.AreEqual(0, differences.Count,
                string.Join(", ", differences.Select(op => op.GetType().Name)));
        }

        private static string Fingerprint(IEntityType entity)
        {
            var properties = entity.GetProperties()
                .Select(p => $"{p.Name}:{p.GetColumnType()}:nullable={p.IsNullable}:max={p.GetMaxLength()}")
                .OrderBy(x => x);

            var keys = entity.GetKeys()
                .Select(k => "key:" + string.Join(",", k.Properties.Select(p => p.Name).OrderBy(x => x)))
                .OrderBy(x => x);

            var indexes = entity.GetIndexes()
                .Select(i => $"ix:{string.Join(",", i.Properties.Select(p => p.Name))}:unique={i.IsUnique}")
                .OrderBy(x => x);

            var fks = entity.GetForeignKeys()
                .Select(fk => $"fk:{string.Join(",", fk.Properties.Select(p => p.Name))}->" +
                              $"{fk.PrincipalEntityType.Name}({string.Join(",", fk.PrincipalKey.Properties.Select(p => p.Name))}):" +
                              $"delete={fk.DeleteBehavior}")
                .OrderBy(x => x);

            return $"table={entity.GetTableName()}|base={entity.BaseType?.Name}|props={string.Join(";", properties)}" +
                   $"|keys={string.Join(";", keys)}|ix={string.Join(";", indexes)}|fks={string.Join(";", fks)}";
        }
    }
}
