// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class DestinationAttributesUpdaterServiceTests
    {

        private Mock<IDatabaseDestinationAttributesRepository> _mockDatabaseDestinationAttributeRepository;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository;
        private DestinationAttributesUpdaterService _destinationAttributeUpdaterService;

        private const string GroupMembership = "GroupMembership";

        [TestInitialize]
        public void InitializeTest()
        {
            _mockDatabaseDestinationAttributeRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _destinationAttributeUpdaterService = new DestinationAttributesUpdaterService(_mockSyncJobRepository.Object, _mockDatabaseDestinationAttributeRepository.Object, _mockGraphGroupRepository.Object);
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
            Assert.AreEqual(response.First().Destination.GetType(), typeof(AzureADGroup));
        }

        [TestMethod]
        public async Task TestGetBulkDestinationAttributes()
        {
            AzureADGroup group = new AzureADGroup
            {
                ObjectId = Guid.NewGuid()
            };

            Guid tableId = Guid.NewGuid();

            Guid owner = Guid.NewGuid();

            _mockGraphGroupRepository.Setup(x => x.GetGroupNamesAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new Dictionary<Guid, string>() { { group.ObjectId,  "name"} });
            _mockGraphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>())).ReturnsAsync(new Dictionary<Guid, List<Guid>>() { { group.ObjectId, new List<Guid> { owner } } });

            var response = await _destinationAttributeUpdaterService.GetBulkDestinationAttributesAsync(new List<(AzureADGroup Destination, Guid TableId)> { (group, tableId) }, GroupMembership);

            Assert.AreEqual(response.First().Id, tableId);
        }
    }
}
