// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Moq;
using Repositories.Contracts;
using Repositories.Mocks;
using System.Net.Http.Json;
using System.Text.Json;
using TeamsChannel.Service;
using TeamsChannel.Service.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelServiceTests
    {
        private TeamsChannelService _service;
        private ChannelSyncInfo _syncInfo;
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository;
        private Mock<IBlobStorageRepository> _mockBlobStorageRepository;
        private Mock<IHttpClientFactory> _mockHttpClientFactory;

        private Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>> _mockChannels = new Dictionary<AzureADTeamsChannel, List<AzureADTeamsUser>>
        {
            { new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "first guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "second guy" } } },
            { new AzureADTeamsChannel { ObjectId = Guid.Parse("00000000-0000-0000-0000-000000000001"), ChannelId = "another channel" },
                new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "third guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), TeamsId = "fourth guy" } } }
        };
        const string ExpectedFilename = "/00000000-0000-0000-0000-000000000042/03281995-010203_00000000-0000-0000-0000-000000000012_TeamsChannel_1.json";


        [TestInitialize]
        public void SetUp()
        {
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _mockTeamsChannelRepository.Setup<Task<List<AzureADTeamsUser>>>(repo => repo.ReadUsersFromChannelAsync(It.IsIn<AzureADTeamsChannel>(_mockChannels.Keys), It.IsAny<Guid>()))
                .ReturnsAsync((AzureADTeamsChannel c, Guid g) => _mockChannels[c]);
            _mockTeamsChannelRepository.Setup<Task<string>>(repo => repo.GetChannelTypeAsync(It.IsIn<AzureADTeamsChannel>(_mockChannels.Keys), It.IsAny<Guid>()))
                .ReturnsAsync((AzureADTeamsChannel tc, Guid _) => tc.ChannelId == "some channel" ? "Private" : "Standard");
                
                _mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            var mockSyncJobRepository = new MockSyncJobRepository();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();

            _service = new TeamsChannelService(_mockTeamsChannelRepository.Object, _mockBlobStorageRepository.Object, _mockHttpClientFactory.Object, mockSyncJobRepository, new MockLoggingRepository());

            _syncInfo = new ChannelSyncInfo
            {
                CurrentPart = 1,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            };
        }

        [TestMethod]
        public async Task VerifyRejectsNonDestinationPrivateChannels()
        {
            var badSyncInfo = new ChannelSyncInfo
            {
                CurrentPart = 2,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            };

            var verification = await _service.VerifyChannelAsync(badSyncInfo);

            Assert.IsFalse(verification.isGood);
            Assert.AreEqual(SyncStatus.TeamsChannelNotPrivate.ToString(), badSyncInfo.SyncJob.Status);
        }


        [TestMethod]
        public async Task VerifyAcceptsGoodSync()
        {
            var verification = await _service.VerifyChannelAsync(_syncInfo);

            Assert.IsTrue(verification.isGood);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _syncInfo.SyncJob.Status);
        }

        [TestMethod]
        public async Task VerifyRejectsNonPrivateChannels()
        {
            var badSyncInfo = new ChannelSyncInfo
            {
                CurrentPart = 1,
                IsDestinationPart = false,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            };

            var verification = await _service.VerifyChannelAsync(badSyncInfo);

            Assert.IsFalse(verification.isGood);
            Assert.AreEqual(SyncStatus.PrivateChannelNotDestination.ToString(), badSyncInfo.SyncJob.Status);
        }

        [TestMethod]
        public async Task GetsUsersFromTeam()
        {
            var sourceChannel = new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" };
            var userList = await _service.GetUsersFromTeamAsync(sourceChannel, _syncInfo.SyncJob.RunId.Value);


            Assert.IsTrue(userList.SequenceEqual(_mockChannels[sourceChannel]));
            Assert.AreEqual(2, userList.Count);
            _mockTeamsChannelRepository.Verify(m => m.ReadUsersFromChannelAsync(sourceChannel, _syncInfo.SyncJob.RunId.Value));
        }

        [TestMethod]
        public async Task CanUploadMembership()
        {
            var sourceChannel = new AzureADTeamsChannel { ObjectId = Guid.Empty, ChannelId = "some channel" };
            var sourceMembers = _mockChannels[sourceChannel];

            var filePath = await _service.UploadMembershipAsync(sourceMembers, _syncInfo, false);

            Assert.AreEqual(ExpectedFilename, filePath);
            _mockBlobStorageRepository.Verify(mock => mock.UploadFileAsync(ExpectedFilename, It.IsNotNull<string>(), It.IsAny<Dictionary<string, string>>()));
        }

        [TestMethod]
        public async Task CanMarkJobsAsError()
        {
            await _service.MarkSyncJobAsErroredAsync(_syncInfo.SyncJob);
            Assert.AreEqual(SyncStatus.Error.ToString(), _syncInfo.SyncJob.Status);
        }
    }
}
