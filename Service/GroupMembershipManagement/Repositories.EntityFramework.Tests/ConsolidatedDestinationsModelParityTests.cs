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
    /// T010: Model-parity tests. These fail if a table, key, relationship, or property
    /// exists in only one of <see cref="GMMContext"/> / <see cref="GMMReadContext"/>.
    /// Writes use GMMContext; reads use GMMReadContext, and the two model shapes must be
    /// identical so read and write mappings cannot drift (data-model.md Section 7).
    /// </summary>
    [TestClass]
    public class ConsolidatedDestinationsModelParityTests
    {
        private static GMMContext CreateWriteContext()
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseSqlServer("Server=(unused);Database=parity;Trusted_Connection=True;")
                .Options;
            return new GMMContext(options);
        }

        private static GMMReadContext CreateReadContext()
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseSqlServer("Server=(unused);Database=parity;Trusted_Connection=True;")
                .Options;
            return new GMMReadContext(options);
        }

        [TestMethod]
        public void ReadAndWriteContexts_ExposeTheSameEntityTypes()
        {
            using var write = CreateWriteContext();
            using var read = CreateReadContext();

            var writeEntities = write.Model.GetEntityTypes().Select(e => e.Name).OrderBy(n => n).ToArray();
            var readEntities = read.Model.GetEntityTypes().Select(e => e.Name).OrderBy(n => n).ToArray();

            CollectionAssert.AreEqual(writeEntities, readEntities,
                "Read and write contexts expose different entity types.");
        }

        [TestMethod]
        public void ReadAndWriteContexts_ExposeConsolidatedDestinationEntities()
        {
            using var read = CreateReadContext();
            var names = read.Model.GetEntityTypes().Select(e => e.Name).ToArray();

            CollectionAssert.Contains(names, "Models.Destination");
            CollectionAssert.Contains(names, "Models.GroupDestination");
            CollectionAssert.Contains(names, "Models.TeamsChannelDestination");
        }

        [TestMethod]
        public void ReadAndWriteContexts_HaveIdenticalTableKeyPropertyAndRelationshipShapes()
        {
            using var write = CreateWriteContext();
            using var read = CreateReadContext();

            foreach (var writeEntity in write.Model.GetEntityTypes())
            {
                var readEntity = read.Model.FindEntityType(writeEntity.Name);
                Assert.IsNotNull(readEntity, $"Entity {writeEntity.Name} missing from read context.");

                Assert.AreEqual(Fingerprint(writeEntity), Fingerprint(readEntity),
                    $"Model shape for {writeEntity.Name} differs between read and write contexts.");
            }
        }

        private static string Fingerprint(IEntityType entity)
        {
            var table = entity.GetTableName();

            var properties = entity.GetProperties()
                .Select(p => $"{p.Name}:{p.GetColumnType()}:nullable={p.IsNullable}:max={p.GetMaxLength()}")
                .OrderBy(x => x);

            var keys = entity.GetKeys()
                .Select(k => "PK/AK:" + string.Join(",", k.Properties.Select(p => p.Name).OrderBy(x => x)))
                .OrderBy(x => x);

            var indexes = entity.GetIndexes()
                .Select(i => $"IX:{string.Join(",", i.Properties.Select(p => p.Name))}:unique={i.IsUnique}")
                .OrderBy(x => x);

            var fks = entity.GetForeignKeys()
                .Select(fk => $"FK:{string.Join(",", fk.Properties.Select(p => p.Name))}->" +
                              $"{fk.PrincipalEntityType.Name}({string.Join(",", fk.PrincipalKey.Properties.Select(p => p.Name))}):" +
                              $"delete={fk.DeleteBehavior}")
                .OrderBy(x => x);

            return $"table={table}|props={string.Join(";", properties)}|keys={string.Join(";", keys)}" +
                   $"|ix={string.Join(";", indexes)}|fks={string.Join(";", fks)}";
        }
    }
}
