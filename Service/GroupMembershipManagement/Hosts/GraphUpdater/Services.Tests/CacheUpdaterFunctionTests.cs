// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GraphUpdater;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class CacheUpdaterFunctionTests
    {
        [TestMethod]
        public async Task UpdateCacheAsync_PreviousReleaseCache_PreservesRecordedTypesBlob()
        {
            var sourceGroupId = Guid.NewGuid();
            var firstMemberId = Guid.NewGuid();
            var secondMemberId = Guid.NewGuid();
            var recordedTypesPath = $"memberTypes/{sourceGroupId}.jsonl";
            var recordedTypesContent =
                $"{{\"id\":\"{firstMemberId}\",\"type\":\"user\"}}" +
                Environment.NewLine +
                $"{{\"id\":\"{secondMemberId}\",\"type\":\"agentUser\"}}" +
                Environment.NewLine;
            var firstCachePath = CacheFileNaming.BuildCacheFileName(
                sourceGroupId,
                new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc));
            var firstUpdatedCachePath = CacheFileNaming.BuildCacheFileName(
                sourceGroupId,
                new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc));
            var secondUpdatedCachePath = CacheFileNaming.BuildCacheFileName(
                sourceGroupId,
                new DateTime(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc));
            var storage = new MockBlobStorageRepository();
            await storage.UploadFileAsync(
                firstCachePath,
                string.Join(
                    Environment.NewLine,
                    firstMemberId,
                    secondMemberId) + Environment.NewLine);
            await storage.UploadFileAsync(recordedTypesPath, recordedTypesContent);

            var previousReleaseMembers = await storage.ReadValuesFromBlobAsync(
                firstCachePath,
                Guid.Parse);
            Assert.AreEqual(2, previousReleaseMembers.Count);

            var function = new CacheUpdaterFunction(
                NullLogger<CacheUpdaterFunction>.Instance,
                storage);
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Group = new Group { GroupId = Guid.NewGuid() }
            };

            await function.UpdateCacheAsync(new CacheUpdaterRequest
            {
                CacheFilePath = firstCachePath,
                GroupId = sourceGroupId,
                SyncJob = syncJob,
                Timestamp = new DateTime(
                    2026,
                    10,
                    2,
                    1,
                    0,
                    0,
                    DateTimeKind.Utc),
                UserIds = new HashSet<Guid> { firstMemberId }
            });

            Assert.IsFalse(storage.Files.ContainsKey(firstCachePath));
            Assert.IsTrue(storage.Files.ContainsKey(firstUpdatedCachePath));
            Assert.AreEqual(recordedTypesContent, storage.Files[recordedTypesPath]);
            var firstUpdatedMembers = await storage.ReadValuesFromBlobAsync(
                firstUpdatedCachePath,
                Guid.Parse);
            CollectionAssert.AreEqual(
                new[] { secondMemberId },
                firstUpdatedMembers.ToArray());

            await function.UpdateCacheAsync(new CacheUpdaterRequest
            {
                CacheFilePath = firstUpdatedCachePath,
                GroupId = sourceGroupId,
                SyncJob = syncJob,
                Timestamp = new DateTime(
                    2026,
                    10,
                    3,
                    1,
                    0,
                    0,
                    DateTimeKind.Utc),
                UserIds = new HashSet<Guid>()
            });

            Assert.IsFalse(storage.Files.ContainsKey(firstUpdatedCachePath));
            Assert.IsTrue(storage.Files.ContainsKey(secondUpdatedCachePath));
            Assert.AreEqual(recordedTypesContent, storage.Files[recordedTypesPath]);
            var secondUpdatedMembers = await storage.ReadValuesFromBlobAsync(
                secondUpdatedCachePath,
                Guid.Parse);
            CollectionAssert.AreEqual(
                new[] { secondMemberId },
                secondUpdatedMembers.ToArray());
        }
    }
}
