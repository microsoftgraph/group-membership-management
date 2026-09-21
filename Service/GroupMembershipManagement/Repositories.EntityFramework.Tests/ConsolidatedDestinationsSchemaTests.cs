// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.EntityFramework.Contexts;
using System.Linq;

namespace Repositories.EntityFramework.Tests
{
    /// <summary>
    /// Guards the two invariants that matter for the additive, dormant consolidated-destinations
    /// slice: (1) the migration is purely additive so it is safe to deploy ahead of any read/write
    /// cutover, and (2) the write (<see cref="GMMContext"/>) and read (<see cref="GMMReadContext"/>)
    /// models stay identical so their mappings cannot drift.
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

            return $"table={entity.GetTableName()}|props={string.Join(";", properties)}" +
                   $"|keys={string.Join(";", keys)}|ix={string.Join(";", indexes)}|fks={string.Join(";", fks)}";
        }
    }
}
