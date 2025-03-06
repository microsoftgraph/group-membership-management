// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository;
        private DestinationAttributesUpdaterService _destinationAttributeUpdaterService;
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository = null;
        private const string GroupMembership = "GroupMembership";

        [TestInitialize]
        public void InitializeTest()
        {
            _mockDatabaseDestinationAttributeRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _destinationAttributeUpdaterService = new DestinationAttributesUpdaterService(
                _mockSyncJobRepository.Object, 
                _mockDatabaseDestinationAttributeRepository.Object, 
                _mockGraphGroupRepository.Object,
                _mockTeamsChannelRepository.Object
                );
        }

        public Guid getDestinationObjectId(SyncJob job)
        {
            return new Guid((JArray.Parse(job.Destination)[0] as JObject)["value"]["objectId"].Value<string>());
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

            _mockGraphGroupRepository.Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new Dictionary<Guid, string>() { { destination.Value.ObjectId,  "name"} });
            _mockGraphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new Dictionary<Guid, List<Guid>>() { { destination.Value.ObjectId, new List<Guid> { owner } } });

            var response = await _destinationAttributeUpdaterService.GetBulkDestinationAttributesAsync(new List<(string Destination, Guid TableId)> { (serializedDestination, tableId) }, GroupMembership);

            Assert.AreEqual(response.First().Id, tableId);
        }
    }
}
