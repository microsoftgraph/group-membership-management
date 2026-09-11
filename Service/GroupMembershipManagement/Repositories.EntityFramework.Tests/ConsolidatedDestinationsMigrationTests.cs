// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.EntityFramework.Contexts;
using System.Collections.Generic;
using System.Linq;

namespace Repositories.EntityFramework.Tests
{
    /// <summary>
    /// T013: Migration tests for the additive consolidated-destinations migration.
    /// A relational database is not available in this environment and the historical
    /// migration chain uses SQL Server-specific operations, so these tests assert the
    /// migration content directly from the scaffolded <see cref="Migration.UpOperations"/>
    /// and <see cref="Migration.DownOperations"/>, plus the checked-in model snapshot:
    /// the created schema matches the storage contract (destinations-storage.md), the
    /// change is purely additive (no destructive operations, no unrelated data churn),
    /// and the snapshot captures the tables so a rescaffold/rerun is a no-op.
    /// </summary>
    [TestClass]
    public class ConsolidatedDestinationsMigrationTests
    {
        private static IReadOnlyList<MigrationOperation> UpOperations()
        {
            var migration = new Contexts.Migrations.add_consolidated_destinations_tables();
            return migration.UpOperations;
        }

        private static IReadOnlyList<MigrationOperation> DownOperations()
        {
            var migration = new Contexts.Migrations.add_consolidated_destinations_tables();
            return migration.DownOperations;
        }

        private static CreateTableOperation FindCreateTable(
            IReadOnlyList<MigrationOperation> operations, string table)
        {
            var op = operations.OfType<CreateTableOperation>().SingleOrDefault(o => o.Name == table);
            Assert.IsNotNull(op, $"Migration must create the {table} table.");
            return op!;
        }

        [TestMethod]
        public void Migration_CreatesDestinationsTable_WithKeyAndTypedForeignKeys()
        {
            var table = FindCreateTable(UpOperations(), "Destinations");

            CollectionAssert.AreEquivalent(
                new[] { "SyncJobId", "DestinationType" },
                table.Columns.Select(c => c.Name).ToArray());

            Assert.AreEqual("SyncJobId", table.PrimaryKey!.Columns.Single());

            Assert.IsTrue(table.ForeignKeys.Any(fk =>
                fk.PrincipalTable == "SyncJobs" && fk.Columns.Single() == "SyncJobId" &&
                fk.OnDelete == ReferentialAction.Cascade), "Cascade FK to SyncJobs is required.");

            Assert.IsTrue(table.ForeignKeys.Any(fk =>
                fk.PrincipalTable == "MembershipTypes" && fk.Columns.Single() == "DestinationType" &&
                fk.PrincipalColumns.Single() == "Name"),
                "FK to MembershipTypes must reference the catalog by Name (matching SyncJobs.MembershipType), not by Id.");
        }

        [TestMethod]
        public void Migration_CreatesGroupDestinationsTable_WithSharedKeyAndObjectIdIndex()
        {
            var ops = UpOperations();
            var table = FindCreateTable(ops, "GroupDestinations");

            CollectionAssert.AreEquivalent(
                new[] { "SyncJobId", "GroupId", "Name", "Email" },
                table.Columns.Select(c => c.Name).ToArray());

            Assert.AreEqual("SyncJobId", table.PrimaryKey!.Columns.Single());
            Assert.IsTrue(table.ForeignKeys.Any(fk =>
                fk.PrincipalTable == "Destinations" && fk.OnDelete == ReferentialAction.Cascade),
                "Shared-key cascade FK to Destinations is required.");

            Assert.IsFalse(table.Columns.Single(c => c.Name == "GroupId").IsNullable);
            Assert.IsTrue(table.Columns.Single(c => c.Name == "Name").IsNullable);
            Assert.IsTrue(table.Columns.Single(c => c.Name == "Email").IsNullable);

            Assert.IsTrue(ops.OfType<CreateIndexOperation>().Any(i =>
                i.Table == "GroupDestinations" && i.Columns.Single() == "GroupId"),
                "GroupDestinations.GroupId must be indexed.");
        }

        [TestMethod]
        public void Migration_CreatesTeamsChannelDestinationsTable_PreservingChannelCapacity()
        {
            var ops = UpOperations();
            var table = FindCreateTable(ops, "TeamsChannelDestinations");

            CollectionAssert.AreEquivalent(
                new[] { "SyncJobId", "TeamId", "ChannelId", "TeamName", "ChannelName" },
                table.Columns.Select(c => c.Name).ToArray());

            var channelId = table.Columns.Single(c => c.Name == "ChannelId");
            Assert.IsFalse(channelId.IsNullable, "ChannelId is required.");
            Assert.AreEqual(255, channelId.MaxLength, "Channel-ID capacity must be preserved at 255.");

            Assert.IsTrue(table.Columns.Single(c => c.Name == "TeamName").IsNullable, "TeamName is a nullable cache.");
            Assert.IsTrue(table.Columns.Single(c => c.Name == "ChannelName").IsNullable, "ChannelName is a nullable cache.");

            Assert.IsTrue(ops.OfType<CreateIndexOperation>().Any(i =>
                i.Table == "TeamsChannelDestinations" && i.Columns.Single() == "TeamId"),
                "TeamsChannelDestinations.TeamId must be indexed.");
        }

        [TestMethod]
        public void Migration_IsAdditiveOnly_NoDestructiveOrUnrelatedDataOperations()
        {
            var up = UpOperations();

            // Purely additive: only table/index creation, and only for the three new tables.
            var createdTables = up.OfType<CreateTableOperation>().Select(o => o.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "Destinations", "GroupDestinations", "TeamsChannelDestinations" }, createdTables);

            Assert.IsFalse(up.OfType<DropTableOperation>().Any(), "Up must not drop tables.");
            Assert.IsFalse(up.OfType<DropColumnOperation>().Any(), "Up must not drop columns.");
            Assert.IsFalse(up.OfType<AlterColumnOperation>().Any(),
                "Up must not alter existing columns (no unrelated non-deterministic churn).");
            Assert.IsFalse(up.OfType<InsertDataOperation>().Any(), "Up must not seed unrelated data.");
            Assert.IsFalse(up.OfType<DeleteDataOperation>().Any(), "Up must not delete unrelated data.");
        }

        [TestMethod]
        public void Migration_DownReversesExactlyTheThreeTables()
        {
            var down = DownOperations();

            var droppedTables = down.OfType<DropTableOperation>().Select(o => o.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "Destinations", "GroupDestinations", "TeamsChannelDestinations" }, droppedTables);

            Assert.IsFalse(down.OfType<AlterColumnOperation>().Any(), "Down must not alter existing columns.");
            Assert.IsFalse(down.OfType<InsertDataOperation>().Any(), "Down must not seed unrelated data.");
            Assert.IsFalse(down.OfType<DeleteDataOperation>().Any(), "Down must not delete unrelated data.");
        }

        [TestMethod]
        public void ModelSnapshot_IncludesConsolidatedDestinations_SoRescaffoldIsANoOp()
        {
            var snapshotType = typeof(GMMContext).Assembly
                .GetType("Repositories.EntityFramework.Contexts.Migrations.GMMContextModelSnapshot");
            Assert.IsNotNull(snapshotType, "A model snapshot must exist.");

            var snapshot = (ModelSnapshot)System.Activator.CreateInstance(snapshotType!)!;
            var entityNames = snapshot.Model.GetEntityTypes().Select(e => e.Name).ToArray();

            CollectionAssert.Contains(entityNames, "Models.Destination");
            CollectionAssert.Contains(entityNames, "Models.GroupDestination");
            CollectionAssert.Contains(entityNames, "Models.TeamsChannelDestination");
        }
    }
}
