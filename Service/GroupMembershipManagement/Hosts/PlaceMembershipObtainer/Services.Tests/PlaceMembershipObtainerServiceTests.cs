// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Services;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public class PlaceMembershipObtainerServiceTests
    {
        private static readonly string[] _conformanceCorpus =
        {
            "f0e1d2c3-b4a5-9687-7869-5a4b3c2d1e0f",
            "00000100-0000-0000-0000-000000000000",
            "00000001-0000-0000-0000-000000000000",
            "80000000-0000-0000-0000-000000000000",
            "7fffffff-ffff-ffff-ffff-ffffffffffff",
            "00000000-8000-0000-0000-000000000000",
            "00000000-7fff-0000-0000-000000000000",
            "00000000-0000-8000-0000-000000000000",
            "00000000-0000-7fff-0000-000000000000",
            "00000000-0000-0000-8000-000000000000",
            "00000000-0000-0000-7f00-000000000000",
            "00000000-0000-0000-0000-000080000000",
            "00000000-0000-0000-0000-00007f000000",
            "00000000-0000-0000-0000-000000000080",
            "00000000-0000-0000-0000-00000000007f",
            "ffffffff-ffff-ffff-ffff-ffffffffffff",
            "00000000-0000-0000-0000-000000000000",
        };

        [TestMethod]
        public async Task SendMembershipAsync_StagesMembersInCanonicalOrder()
        {
            var blobStorageRepository = new Mock<IBlobStorageRepository>();
            var dryRun = new Mock<IDryRunValue>();
            dryRun.SetupGet(x => x.DryRunEnabled).Returns(false);

            GroupMembership staged = null;
            blobStorageRepository
                .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Callback<string, string, Dictionary<string, string>>((path, content, metadata) =>
                {
                    staged = JsonSerializer.Deserialize<GroupMembership>(content);
                })
                .Returns(Task.CompletedTask);

            var service = new PlaceMembershipObtainerService(
                                new Mock<IGraphGroupRepository>().Object,
                                blobStorageRepository.Object,
                                new Mock<ISyncJobStatusService>().Object,
                                new MockDestinationResolver(
                                    new Mock<IDatabaseGroupsRepository>().Object,
                                    new Mock<IDatabaseChannelsRepository>().Object),
                                dryRun.Object);

            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Query = "[]" };

            // Deliberately unsorted input, so a missing sort cannot pass by accident.
            var users = _conformanceCorpus.Select((id, index) => new AzureADUser
            {
                ObjectId = Guid.Parse(id),
                DisplayName = $"member-{index}",
                Properties = new Dictionary<string, object> { ["marker"] = index },
                MembershipAction = MembershipAction.Add,
                SourceGroup = new Guid(index + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                SourceGroups = new List<Guid> { new Guid(index + 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) }
            }).ToList();
            var expected = users.OrderBy(user => user.ObjectId.ToString("D"), StringComparer.Ordinal)
                                .Select(user => JsonSerializer.Serialize(user))
                                .ToList();

            await service.SendMembershipAsync(syncJob, Guid.NewGuid(), users, 1, false);

            var actual = staged.SourceMembers.Select(user => JsonSerializer.Serialize(user)).ToList();

            CollectionAssert.AreEqual(expected, actual,
                "PlaceMembershipObtainer changed member order or content while staging.");

        }
    }
}
