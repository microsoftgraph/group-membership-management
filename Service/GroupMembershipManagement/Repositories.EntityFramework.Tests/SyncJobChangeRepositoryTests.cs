// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.SyncJobChange;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework.Tests
{
    [TestClass]
    public class SyncJobChangeRepositoryTests
    {
        private static DbContextOptions<GMMContext> OptionsFor(string databaseName) =>
            new DbContextOptionsBuilder<GMMContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

        [DataTestMethod]
        [DataRow(SyncJobChangeReason.ScheduledNow)]
        [DataRow(SyncJobChangeReason.UpdateAutoApproved)]
        public async Task GetRecentConfigChanges_IncludesQueryBearingUpdateReasons(
            SyncJobChangeReason changeReason)
        {
            var databaseName = Guid.NewGuid().ToString();
            var syncJobId = Guid.NewGuid();
            var changeTime = DateTime.UtcNow.AddMinutes(-1);
            var options = OptionsFor(databaseName);
            await using (var seedContext = new GMMContext(options))
            {
                seedContext.SyncJobChanges.Add(new Entities.SyncJobChange
                {
                    Id = Guid.NewGuid(),
                    SyncJobId = syncJobId,
                    ChangeTime = changeTime,
                    ChangeReason = changeReason.ToString(),
                    ChangeDetails = "{\"query\":\"[]\"}",
                    BusinessJustification = string.Empty
                });
                await seedContext.SaveChangesAsync();
            }

            var repository = new SyncJobChangeRepository(
                new GMMContext(options),
                new GMMReadContext(options));

            var changes =
                await repository.GetRecentConfigChangesBySyncJobIdAsync(
                    syncJobId,
                    DateTime.UtcNow,
                    count: 1);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(changeReason.ToString(), changes[0].ChangeReason);
        }

        [TestMethod]
        public async Task GetRecentConfigChanges_ZeroCountReturnsCompleteHistory()
        {
            var databaseName = Guid.NewGuid().ToString();
            var syncJobId = Guid.NewGuid();
            var options = OptionsFor(databaseName);
            await using (var seedContext = new GMMContext(options))
            {
                for (var index = 0; index < 12; index++)
                {
                    seedContext.SyncJobChanges.Add(new Entities.SyncJobChange
                    {
                        Id = Guid.NewGuid(),
                        SyncJobId = syncJobId,
                        ChangeTime = DateTime.UtcNow.AddMinutes(-index),
                        ChangeReason = SyncJobChangeReason.ScheduledNow.ToString(),
                        ChangeDetails = "{\"query\":\"[]\"}",
                        BusinessJustification = string.Empty
                    });
                }

                await seedContext.SaveChangesAsync();
            }

            var repository = new SyncJobChangeRepository(
                new GMMContext(options),
                new GMMReadContext(options));

            var changes = await repository.GetRecentConfigChangesBySyncJobIdAsync(
                syncJobId,
                DateTime.UtcNow,
                count: 0);

            Assert.AreEqual(12, changes.Count);
        }
    }
}
