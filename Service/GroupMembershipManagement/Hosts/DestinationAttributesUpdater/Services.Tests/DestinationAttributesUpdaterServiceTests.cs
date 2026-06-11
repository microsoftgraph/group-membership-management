// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.Helpers;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services.Tests
{
    [TestClass]
    public class DestinationAttributesUpdaterServiceTests
    {

        private Mock<IDatabaseDestinationAttributesRepository> _mockDatabaseDestinationAttributeRepository;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository;
        private Mock<IDatabaseGroupsRepository> _mockGroupsRepository;
        private Mock<IDatabaseChannelsRepository> _mockChannelsRepository;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository;
        private DestinationAttributesUpdaterService _destinationAttributeUpdaterService;
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository = null;
        private const string GroupMembership = "GroupMembership";
        private const string TeamsChannelMembership = "TeamsChannelMembership";

        [TestInitialize]
        public void InitializeTest()
        {
            _mockDatabaseDestinationAttributeRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _mockGroupsRepository = new Mock<IDatabaseGroupsRepository>();
            _mockChannelsRepository = new Mock<IDatabaseChannelsRepository>();
            _destinationAttributeUpdaterService = new DestinationAttributesUpdaterService(
                _mockSyncJobRepository.Object,
                _mockGroupsRepository.Object,
                _mockChannelsRepository.Object,
                _mockDatabaseDestinationAttributeRepository.Object,
                _mockGraphGroupRepository.Object,
                _mockTeamsChannelRepository.Object
                );
        }

        public Guid getDestinationObjectId(SyncJob job)
        {
            return job.Group.GroupId;
        }

        [TestMethod]
        public async Task TestGetGroupDestinations()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            _mockSyncJobRepository.Setup(x => x.GetSyncJobsByDestinationAsync(It.IsAny<string>())).ReturnsAsync(new List<SyncJob> { job });

            var response = await _destinationAttributeUpdaterService.GetDestinationsAsync(GroupMembership);

            Assert.AreEqual(response.First().JobId, job.Id);
            Assert.AreEqual(response.First().Destination.GetType(), typeof(string));
        }

        [TestMethod]
        public async Task TestGetBulkDestinationAttributes()
        {
            var destination = new DestinationObject()
            {
                Type = "GroupMembership",
                Value = new GroupDestinationValue() { ObjectId = Guid.NewGuid() }
            };

            var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
            var serializedDestination = JsonSerializer.Serialize(destination, options);

            Guid tableId = Guid.NewGuid();

            Guid owner = Guid.NewGuid();

            _mockGraphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = destination.Value.ObjectId, Name = "name", Email = "email" } });
            _mockGraphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new Dictionary<Guid, List<Guid>>() { { destination.Value.ObjectId, new List<Guid> { owner } } });

            var response = await _destinationAttributeUpdaterService.GetBulkDestinationAttributesAsync(new List<DestinationInfo> { new DestinationInfo { Destination = serializedDestination, JobId = tableId } }, GroupMembership);

            var attributes = response.First();
            Assert.AreEqual(attributes.Id, tableId);
            Assert.IsNotNull(attributes.Name);
            Assert.IsNotNull(attributes.Email);
            Assert.IsNotNull(attributes.Owners);
        }

        [TestMethod]
        public async Task TestGetBulkDestinationAttributesWithMissingData_Groups()
        {
            var destination = new DestinationObject()
            {
                Type = "GroupMembership",
                Value = new GroupDestinationValue() { ObjectId = Guid.NewGuid() }
            };

            var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
            var serializedDestination = JsonSerializer.Serialize(destination, options);

            Guid tableId = Guid.NewGuid();

            Guid owner = Guid.NewGuid();

            _mockGraphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = Guid.NewGuid(), Name = "name", Email = "email" } });
            _mockGraphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new Dictionary<Guid, List<Guid>>() { { Guid.NewGuid(), new List<Guid> { owner } } });

            var response = await _destinationAttributeUpdaterService.GetBulkDestinationAttributesAsync(new List<DestinationInfo> { new DestinationInfo { Destination = serializedDestination, JobId = tableId } }, GroupMembership);

            var attributes = response.First();
            Assert.AreEqual(attributes.Id, tableId);
            Assert.IsNull(attributes.Name);
            Assert.IsNull(attributes.Email);
            Assert.IsNull(attributes.Owners);
        }

        [TestMethod]
        public async Task TestGetBulkDestinationAttributesWithMissingData_Channels()
        {
            var destination = new DestinationObject()
            {
                Type = TeamsChannelMembership,
                Value = new TeamsChannelDestinationValue() { ObjectId = Guid.NewGuid(), ChannelId = "some-channel-id" }
            };

            var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
            var serializedDestination = JsonSerializer.Serialize(destination, options);

            Guid tableId = Guid.NewGuid();

            Guid owner = Guid.NewGuid();

            _mockTeamsChannelRepository.Setup(x => x.GetTeamsChannelNamesAsync(It.IsAny<List<AzureADTeamsChannel>>())).ReturnsAsync(new Dictionary<string, string>() { { Guid.NewGuid().ToString(), "name" } });
            _mockGraphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new Dictionary<Guid, List<Guid>>() { { Guid.NewGuid(), new List<Guid> { owner } } });
            _mockTeamsChannelRepository.Setup(x => x.GetTeamsChannelEmailsAsync(It.IsAny<List<AzureADTeamsChannel>>())).ReturnsAsync(new Dictionary<string, string>() { { Guid.NewGuid().ToString(), "email" } });

            var response = await _destinationAttributeUpdaterService.GetBulkDestinationAttributesAsync(new List<DestinationInfo> { new DestinationInfo { Destination = serializedDestination, JobId = tableId } }, TeamsChannelMembership);

            var attributes = response.First();
            Assert.AreEqual(attributes.Id, tableId);
            Assert.IsNull(attributes.Name);
            Assert.IsNull(attributes.Email);
            Assert.IsNull(attributes.Owners);
        }
    }
}
