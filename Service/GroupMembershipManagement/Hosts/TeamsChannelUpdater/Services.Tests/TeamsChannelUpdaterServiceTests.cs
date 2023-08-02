// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.TeamsChannelUpdater.Contracts;
using Services.TeamsChannelUpdater;
using System.Threading.Channels;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelUpdaterServiceTests
    {
        private TeamsChannelUpdaterService _teamsChannelUpdaterService = null!;
        private UpdaterChannelSyncInfo _syncInfo = null!;  
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private Mock<IMailRepository> _mockMailRepository = null!;
        private Mock<IEmailSenderRecipient> _mockEmailSenderRecipient = null!;

        private string _groupName = "Group 1 Display Name";

        private List<AzureADUser> _mockOwnerList = new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() }, new AzureADUser { ObjectId = Guid.NewGuid() } };

        private List<AzureADTeamsChannel> _mockChannels = new List<AzureADTeamsChannel>
        {
            new AzureADTeamsChannel { ObjectId = Guid.Parse("00000000-0000-0000-0000-000000000001"), ChannelId = "some channel" },
            new AzureADTeamsChannel { ObjectId = Guid.Parse("00000000-0000-0000-0000-000000000002"), ChannelId = "another channel" }
        };

        private List<List<AzureADTeamsUser>> _mockMemberLists = new List<List<AzureADTeamsUser>>
        {
            new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "first guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "second guy" } },
            new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "third guy" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "fourth guy" } }
        };

        [TestInitialize]
        public void SetUp()
        {
            _syncInfo = new UpdaterChannelSyncInfo
            {
                CurrentPart = 1,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    PartitionKey = "0000-00-00",
                    RowKey = "00000000-0000-0000-0000-000000000001",
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]"
                }
            };

            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _mockTeamsChannelRepository.Setup<Task<(int, List<AzureADTeamsUser>, List<AzureADTeamsUser>)>>(repo => repo.AddUsersToChannelAsync(_mockChannels[0], _mockMemberLists[0]))
                .ReturnsAsync(() => (2, new List<AzureADTeamsUser>(), new List<AzureADTeamsUser>()));
            _mockTeamsChannelRepository.Setup<Task<(int, List<AzureADTeamsUser>)>>(repo => repo.RemoveUsersFromChannelAsync(_mockChannels[1], _mockMemberLists[1]))
                .ReturnsAsync(() => (2, new List<AzureADTeamsUser>()));
            _mockTeamsChannelRepository.Setup<Task<string>>(repo => repo.GetGroupNameAsync(_syncInfo.SyncJob.TargetOfficeGroupId, It.IsAny<Guid>()))
                .ReturnsAsync(() => _groupName);
            _mockTeamsChannelRepository.Setup<Task<List<AzureADUser>>>(repo => repo.GetGroupOwnersAsync(_syncInfo.SyncJob.TargetOfficeGroupId, It.IsAny<Guid>(), 0))
                .ReturnsAsync(() => _mockOwnerList);

            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobRepository.Setup<Task<SyncJob>>(repo => repo.GetSyncJobAsync(_syncInfo.SyncJob.Id))
                .ReturnsAsync(_syncInfo.SyncJob);
            _mockSyncJobRepository.Setup(repo => repo.UpdateSyncJobStatusAsync(It.IsAny<SyncJob[]>(), SyncStatus.Error))
                .Callback(() =>
                {
                    _syncInfo.SyncJob.Status = SyncStatus.Error.ToString();
                });
            _mockSyncJobRepository.Setup(repo => repo.UpdateSyncJobStatusAsync(It.IsAny<SyncJob[]>(), SyncStatus.Idle))
                .Callback(() =>
                {
                    _syncInfo.SyncJob.Status = SyncStatus.Idle.ToString();
                });


            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockMailRepository = new Mock<IMailRepository>();
            _mockEmailSenderRecipient = new Mock<IEmailSenderRecipient>();

            _teamsChannelUpdaterService = new TeamsChannelUpdaterService(_mockTeamsChannelRepository.Object, _mockSyncJobRepository.Object, 
                _mockLoggingRepository.Object, _mockMailRepository.Object, _mockEmailSenderRecipient.Object);

        }

        [TestMethod]
        public async Task CanRetrieveJobs()
        {
            var job = await _teamsChannelUpdaterService.GetSyncJobAsync(_syncInfo.SyncJob.Id);
            Assert.AreEqual(job, _syncInfo.SyncJob);
        }

        [TestMethod]
        public async Task CanUpdateJobStatus()
        {
            await _teamsChannelUpdaterService.UpdateSyncJobStatusAsync(_syncInfo.SyncJob, SyncStatus.Idle, false, _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty));
            Assert.AreEqual(SyncStatus.Idle.ToString(), _syncInfo.SyncJob.Status);
        }

        [TestMethod]
        public async Task CanMarkJobsAsError()
        {
            await _teamsChannelUpdaterService.MarkSyncJobAsErroredAsync(_syncInfo.SyncJob);
            Assert.AreEqual(SyncStatus.Error.ToString(), _syncInfo.SyncJob.Status);
        }

        [TestMethod]
        public async Task CanAddUsersToChannel()
        {
            var channel = _mockChannels[0];
            var members = _mockMemberLists[0];
            var results = await _teamsChannelUpdaterService.AddUsersToChannelAsync(channel, members);
            Assert.AreEqual(results.SuccessCount, 2);
            Assert.AreEqual(results.UsersToRetry.Count, 0);
        }

        [TestMethod]
        public async Task CanRemoveUsersFromChannel()
        {
            var channel = _mockChannels[1];
            var members = _mockMemberLists[1];
            var results = await _teamsChannelUpdaterService.RemoveUsersFromChannelAsync(channel, members);
            Assert.AreEqual(results.SuccessCount, 2);
            Assert.AreEqual(results.UserRemovesFailed.Count, 0);
        }

        [TestMethod]
        public async Task CanGetGroupName()
        {
            var groupName = await _teamsChannelUpdaterService.GetGroupNameAsync(_syncInfo.SyncJob.TargetOfficeGroupId, _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty));
            Assert.AreEqual(groupName,_groupName);
        }

        [TestMethod]
        public async Task CanGetOwnersName()
        {
            var owners = await _teamsChannelUpdaterService.GetGroupOwnersAsync(_syncInfo.SyncJob.TargetOfficeGroupId, _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty));
            Assert.AreEqual(owners[0].ObjectId, _mockOwnerList[0].ObjectId);
            Assert.AreEqual(owners[1].ObjectId, _mockOwnerList[1].ObjectId);
        }
    }
}
