// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Repositories.Contracts;
using Services.Contracts;
using Services.TeamsChannelUpdater.Contracts;
using Services.TeamsChannelUpdater;
using System.Threading.Channels;
using Models.ServiceBus;
using Models.SyncJobHistory;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelUpdaterServiceTests
    {
        private TeamsChannelUpdaterService _teamsChannelUpdaterService = null!;
        private UpdaterChannelSyncInfo _syncInfo = null!;  
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<IDatabaseGroupsRepository> _mockGroupsRepository = null!;
        private Mock<IDatabaseChannelsRepository> _mockChannelsRepository = null!;
        private Mock<IServiceBusQueueRepository> _mockServiceBusQueueRepository = null!;
        private Mock<ISyncJobStatusService> _mockSyncJobStatusService = null!;
        private Mock<ISyncJobHistoryRepository> _mockSyncJobHistoryRepository = null!;

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
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000000"", ""channel"":""some channel""}},{""type"":""TeamsChannel"",""source"":{""group"":""00000000-0000-0000-0000-000000000001"", ""channel"":""another channel""}}]",
                    Channel = new Models.Channel
                    {
                        ChannelId = "channelId",
                        GroupId = Guid.Parse("00000000-0000-0000-0000-000000000042")
                    },
                    MembershipType = "TeamsChannelMembership"
                }
            };

            _mockGroupsRepository = new Mock<IDatabaseGroupsRepository>();
            _mockChannelsRepository = new Mock<IDatabaseChannelsRepository>();
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _mockTeamsChannelRepository.Setup<Task<(int, List<AzureADTeamsUser>, List<AzureADTeamsUser>)>>(repo => repo.AddUsersToChannelAsync(_mockChannels[0], _mockMemberLists[0]))
                .ReturnsAsync(() => (2, new List<AzureADTeamsUser>(), new List<AzureADTeamsUser>()));
            _mockTeamsChannelRepository.Setup<Task<(int, List<AzureADTeamsUser>)>>(repo => repo.RemoveUsersFromChannelAsync(_mockChannels[1], _mockMemberLists[1]))
                .ReturnsAsync(() => (2, new List<AzureADTeamsUser>()));
            _mockTeamsChannelRepository.Setup<Task<string>>(repo => repo.GetGroupNameAsync(_syncInfo.SyncJob.Channel.GroupId, It.IsAny<Guid>()))
                .ReturnsAsync(() => _groupName);
            _mockTeamsChannelRepository.Setup<Task<List<AzureADUser>>>(repo => repo.GetGroupOwnersAsync(_syncInfo.SyncJob.Channel.GroupId, It.IsAny<Guid>(), 0))
                .ReturnsAsync(() => _mockOwnerList);

            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockSyncJobRepository.Setup<Task<SyncJob>>(repo => repo.GetSyncJobAsync(_syncInfo.SyncJob.Id))
                .ReturnsAsync(_syncInfo.SyncJob);


            _mockServiceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _mockSyncJobStatusService = new Mock<ISyncJobStatusService>();
            _mockSyncJobStatusService
                .Setup(service => service.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory?>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory?, string>((job, status, history, functionName) =>
                {
                    job.Status = status?.ToString();
                });

            _mockSyncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();

            _teamsChannelUpdaterService = new TeamsChannelUpdaterService(NullLogger<TeamsChannelUpdaterService>.Instance,
                _mockTeamsChannelRepository.Object, _mockSyncJobRepository.Object,
                _mockGroupsRepository.Object, _mockChannelsRepository.Object,
                _mockServiceBusQueueRepository.Object,
                _mockSyncJobStatusService.Object,
                _mockSyncJobHistoryRepository.Object);

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
        public async Task UpdateJobStatus_PopulatesCountsAndAfterSyncUserCount_OnIdle()
        {
            var runId = _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            _mockSyncJobHistoryRepository.Setup(r => r.GetByRunIdAsync(runId))
                .ReturnsAsync(new SyncJobHistory { RunId = runId, BeforeSyncUserCount = 100 });

            SyncJobHistory captured = null;
            _mockSyncJobStatusService
                .Setup(s => s.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory?>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory?, string>((job, status, history, fn) => captured = history);

            await _teamsChannelUpdaterService.UpdateSyncJobStatusAsync(_syncInfo.SyncJob, SyncStatus.Idle, false, runId, usersAdded: 10, usersRemoved: 5);

            Assert.IsNotNull(captured);
            Assert.AreEqual(10, captured.UsersAdded);
            Assert.AreEqual(5, captured.UsersRemoved);
            Assert.AreEqual(105, captured.AfterSyncUserCount); // 100 + 10 - 5
        }

        [TestMethod]
        public async Task UpdateJobStatus_AfterSyncUserCountNull_WhenNoBeforeCount()
        {
            var runId = _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            _mockSyncJobHistoryRepository.Setup(r => r.GetByRunIdAsync(runId))
                .ReturnsAsync(new SyncJobHistory { RunId = runId, BeforeSyncUserCount = null });

            SyncJobHistory captured = null;
            _mockSyncJobStatusService
                .Setup(s => s.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory?>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory?, string>((job, status, history, fn) => captured = history);

            await _teamsChannelUpdaterService.UpdateSyncJobStatusAsync(_syncInfo.SyncJob, SyncStatus.Idle, false, runId, usersAdded: 10, usersRemoved: 5);

            Assert.IsNotNull(captured);
            Assert.AreEqual(10, captured.UsersAdded);
            Assert.AreEqual(5, captured.UsersRemoved);
            Assert.IsNull(captured.AfterSyncUserCount);
        }

        [TestMethod]
        public async Task UpdateJobStatus_DoesNotComputeAfterSyncUserCount_WhenNotIdle()
        {
            var runId = _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            SyncJobHistory captured = null;
            _mockSyncJobStatusService
                .Setup(s => s.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory?>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory?, string>((job, status, history, fn) => captured = history);

            await _teamsChannelUpdaterService.UpdateSyncJobStatusAsync(_syncInfo.SyncJob, SyncStatus.Error, false, runId, usersAdded: 10, usersRemoved: 5);

            Assert.IsNotNull(captured);
            Assert.IsNull(captured.AfterSyncUserCount);
            _mockSyncJobHistoryRepository.Verify(r => r.GetByRunIdAsync(It.IsAny<Guid>()), Times.Never);
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
            var groupName = await _teamsChannelUpdaterService.GetGroupNameAsync(_syncInfo.SyncJob.Channel.GroupId, _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty));
            Assert.AreEqual(groupName,_groupName);
        }

        [TestMethod]
        public async Task DestinationLabelRendersTeamNameColonChannelName()
        {
            // Render "TeamName: ChannelName".
            _mockTeamsChannelRepository
                .Setup(repo => repo.GetTeamsChannelNameAsync(It.IsAny<AzureADTeamsChannel>()))
                .ReturnsAsync("Engineering Channel");

            var label = await _teamsChannelUpdaterService.GetDestinationLabelAsync(_syncInfo.SyncJob, _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty));

            Assert.AreEqual($"{_groupName}: Engineering Channel", label);
        }

        [TestMethod]
        public async Task DestinationLabelSubstitutesChannelIdWhenNameUnresolvable()
        {
            // Substitute the id when a name is unresolvable.
            _mockTeamsChannelRepository
                .Setup(repo => repo.GetTeamsChannelNameAsync(It.IsAny<AzureADTeamsChannel>()))
                .ReturnsAsync((string)null);

            var label = await _teamsChannelUpdaterService.GetDestinationLabelAsync(_syncInfo.SyncJob, _syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty));

            Assert.AreEqual($"{_groupName}: {_syncInfo.SyncJob.Channel.ChannelId}", label);
        }
    }
}
