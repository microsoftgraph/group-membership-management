// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.EntityFramework.Contexts;
using System.Linq;

namespace Repositories.EntityFramework.Tests
{
    /// <summary>
    /// T011: Schema-invariant tests for the consolidated TPT destination model,
    /// asserted against the EF model metadata (contract: destinations-storage.md
    /// Section Structural postconditions and data-model.md Sections 1-2):
    /// one base row keyed by SyncJobId, exactly one matching per-type row via a shared
    /// key, cascade delete, object-ID indexes, a required DestinationType discriminator
    /// referenced by Name (DB-level FK to the existing MembershipTypes catalog, matching
    /// the SyncJobs.MembershipType standard), and a preserved channel-ID capacity.
    /// </summary>
    [TestClass]
    public class ConsolidatedDestinationsSchemaInvariantTests
    {
        private GMMContext _context = null!;

        [TestInitialize]
        public void Initialize()
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseSqlServer("Server=(unused);Database=schema;Trusted_Connection=True;")
                .Options;
            _context = new GMMContext(options);
        }

        [TestCleanup]
        public void Cleanup() => _context.Dispose();

        private IEntityType Entity(string name)
        {
            var e = _context.Model.FindEntityType(name);
            Assert.IsNotNull(e, $"Entity {name} is not mapped.");
            return e!;
        }

        [TestMethod]
        public void Destinations_KeyedBySyncJobId_WithCascadeFkToSyncJobs()
        {
            var destination = Entity("Models.Destination");

            Assert.AreEqual("Destinations", destination.GetTableName());

            var pk = destination.FindPrimaryKey();
            Assert.IsNotNull(pk);
            Assert.AreEqual(1, pk!.Properties.Count);
            Assert.AreEqual("SyncJobId", pk.Properties[0].Name);

            var syncJobFk = destination.GetForeignKeys()
                .SingleOrDefault(fk => fk.PrincipalEntityType.Name == "Models.SyncJob");
            Assert.IsNotNull(syncJobFk, "Destinations must have a FK to SyncJobs.");
            Assert.AreEqual("SyncJobId", syncJobFk!.Properties.Single().Name);
            Assert.AreEqual(DeleteBehavior.Cascade, syncJobFk.DeleteBehavior);
        }

        [TestMethod]
        public void Destinations_HaveRequiredDestinationTypeReferencedByName()
        {
            var destination = Entity("Models.Destination");

            // Following the SyncJobs.MembershipType standard, the catalog reference is a
            // required string discriminator holding the MembershipTypes Name; the actual
            // foreign key to MembershipTypes.Name is enforced at the database level in the
            // migration (asserted in ConsolidatedDestinationsMigrationTests), not modeled
            // as an EF relationship. So the model must NOT reference by Id.
            var typeProp = destination.FindProperty("DestinationType");
            Assert.IsNotNull(typeProp, "Destinations must have a DestinationType discriminator.");
            Assert.AreEqual(typeof(string), typeProp!.ClrType, "DestinationType is referenced by Name (string), not by Id.");
            Assert.IsFalse(typeProp.IsNullable, "DestinationType is required.");
            Assert.AreEqual(255, typeProp.GetMaxLength(), "DestinationType must match the MembershipTypes.Name capacity.");

            Assert.IsNull(destination.FindProperty("DestinationTypeId"),
                "Destinations must not reference MembershipTypes by Id.");

            Assert.IsFalse(
                destination.GetForeignKeys().Any(fk => fk.PrincipalEntityType.Name == "Models.MembershipType"),
                "The MembershipTypes reference is a DB-level by-Name FK (SyncJobs standard), not a modeled relationship.");

            Assert.IsTrue(
                destination.GetIndexes().Any(i => i.Properties.Count == 1 && i.Properties[0].Name == "DestinationType"),
                "DestinationType must be indexed (matching the SyncJobs.MembershipType index).");
        }

        [TestMethod]
        public void GroupDestinations_ShareKeyCascadeFromDestinations_AndIndexObjectId()
        {
            var group = Entity("Models.GroupDestination");

            Assert.AreEqual("GroupDestinations", group.GetTableName());
            Assert.AreEqual("SyncJobId", group.FindPrimaryKey()!.Properties.Single().Name);

            var fk = group.GetForeignKeys()
                .SingleOrDefault(f => f.PrincipalEntityType.Name == "Models.Destination");
            Assert.IsNotNull(fk, "GroupDestinations must share its key with Destinations.");
            Assert.AreEqual("SyncJobId", fk!.Properties.Single().Name);
            Assert.AreEqual(DeleteBehavior.Cascade, fk.DeleteBehavior);

            Assert.IsFalse(group.FindProperty("GroupId")!.IsNullable, "GroupId is required.");
            Assert.IsTrue(group.FindProperty("Name")!.IsNullable, "Name is a nullable cache.");
            Assert.IsTrue(group.FindProperty("Email")!.IsNullable, "Email is a nullable cache.");

            Assert.IsTrue(
                group.GetIndexes().Any(i => i.Properties.Count == 1 && i.Properties[0].Name == "GroupId"),
                "GroupDestinations must index GroupId for lookup.");
        }

        [TestMethod]
        public void TeamsChannelDestinations_ShareKeyCascade_IndexObjectId_RequireChannel()
        {
            var channel = Entity("Models.TeamsChannelDestination");

            Assert.AreEqual("TeamsChannelDestinations", channel.GetTableName());
            Assert.AreEqual("SyncJobId", channel.FindPrimaryKey()!.Properties.Single().Name);

            var fk = channel.GetForeignKeys()
                .SingleOrDefault(f => f.PrincipalEntityType.Name == "Models.Destination");
            Assert.IsNotNull(fk, "TeamsChannelDestinations must share its key with Destinations.");
            Assert.AreEqual(DeleteBehavior.Cascade, fk!.DeleteBehavior);

            Assert.IsFalse(channel.FindProperty("TeamId")!.IsNullable, "TeamId is required.");

            var channelId = channel.FindProperty("ChannelId")!;
            Assert.IsFalse(channelId.IsNullable, "ChannelId is required and non-empty.");

            Assert.IsTrue(channel.FindProperty("TeamName")!.IsNullable, "TeamName is a nullable cache.");
            Assert.IsTrue(channel.FindProperty("ChannelName")!.IsNullable, "ChannelName is a nullable cache.");

            Assert.IsTrue(
                channel.GetIndexes().Any(i => i.Properties.Count == 1 && i.Properties[0].Name == "TeamId"),
                "TeamsChannelDestinations must index TeamId for lookup.");
        }

        [TestMethod]
        public void TeamsChannelDestinations_PreserveLegacyChannelIdCapacity()
        {
            var legacyChannelIdMax = Entity("Models.Channel").FindProperty("ChannelId")!.GetMaxLength();
            var newChannelIdMax = Entity("Models.TeamsChannelDestination").FindProperty("ChannelId")!.GetMaxLength();

            // Legacy TeamsChannels.ChannelId is nvarchar(255); the migrated column must not
            // truncate valid legacy values, so its capacity must be at least the legacy capacity.
            Assert.AreEqual(255, newChannelIdMax, "Channel-ID capacity must match the confirmed legacy value.");
            if (legacyChannelIdMax.HasValue)
            {
                Assert.IsTrue(newChannelIdMax >= legacyChannelIdMax,
                    "Migrated ChannelId capacity must not be smaller than the legacy capacity.");
            }
        }

        [TestMethod]
        public void PerTypeRows_ShareTheBaseDestinationPrimaryKey()
        {
            // Structural postcondition: exactly one per-type row per base row, selected by the
            // shared SyncJobId key. Both per-type entities key on the same column as the base row.
            var basePk = Entity("Models.Destination").FindPrimaryKey()!.Properties.Single().Name;
            Assert.AreEqual(basePk, Entity("Models.GroupDestination").FindPrimaryKey()!.Properties.Single().Name);
            Assert.AreEqual(basePk, Entity("Models.TeamsChannelDestination").FindPrimaryKey()!.Properties.Single().Name);
        }
    }
}
