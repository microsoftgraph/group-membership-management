// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Moq;
using Repositories.Contracts;
using Services;
using Services.Messages.Requests;
using SqlMembershipObtainer.Entities;
using System.Net;

namespace WebApi.Tests
{
    [TestClass]
    public class SpotCheckUserMembershipHandlerTests
    {
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository = null!;
        private Mock<ISqlMembershipRepository> _mockSqlMembershipRepository = null!;
        private Mock<IDataFactoryRepository> _mockDataFactoryRepository = null!;
        private SpotCheckUserMembershipHandler _handler = null!;

        private Guid _syncJobId;
        private string _userId = null!;
        private readonly Guid _groupId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        [TestInitialize]
        public void Initialize()
        {
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            _mockSqlMembershipRepository = new Mock<ISqlMembershipRepository>();
            _mockDataFactoryRepository = new Mock<IDataFactoryRepository>();

            _syncJobId = Guid.NewGuid();
            _userId = Guid.NewGuid().ToString();

            _handler = new SpotCheckUserMembershipHandler(
                NullLogger<SpotCheckUserMembershipHandler>.Instance,
                _mockSyncJobRepository.Object,
                _mockGraphGroupRepository.Object,
                _mockSqlMembershipRepository.Object,
                _mockDataFactoryRepository.Object);
        }

        private void SetupJob(string query)
        {
            _mockSyncJobRepository
                .Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(new SyncJob { Id = _syncJobId, Query = query });
        }

        [TestMethod]
        public async Task ReturnsNotFound_WhenJobDoesNotExist()
        {
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId)).ReturnsAsync((SyncJob)null!);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ReturnsNotFound_WhenUserDoesNotExist()
        {
            SetupJob("[]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync((bool?)null);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ReportsDisabledAccount_AndSkipsPartEvaluation()
        {
            SetupJob($"[{{\"type\":\"GroupMembership\",\"source\":\"{_groupId}\",\"exclusionary\":false}}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(false);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsFalse(response.Model.AccountEnabled);
            Assert.AreEqual(0, response.Model.Parts.Count);
            _mockGraphGroupRepository.Verify(x => x.IsEmailRecipientMemberOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
        }

        [TestMethod]
        public async Task EvaluatesGroupMembership_Included()
        {
            SetupJob($"[{{\"type\":\"GroupMembership\",\"source\":\"{_groupId}\",\"exclusionary\":false}}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockGraphGroupRepository.Setup(x => x.IsEmailRecipientMemberOfGroupAsync(_userId, _groupId)).ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(response.Model.AccountEnabled);
            Assert.AreEqual(1, response.Model.Parts.Count);
            var part = response.Model.Parts[0];
            Assert.IsTrue(part.Supported);
            Assert.IsFalse(part.Exclusionary);
            Assert.AreEqual(true, part.Included);
            Assert.IsFalse(response.Model.HasUnsupportedParts);
        }

        [TestMethod]
        public async Task EvaluatesExclusionaryGroupMembership_NotIncluded()
        {
            SetupJob($"[{{\"type\":\"GroupMembership\",\"source\":\"{_groupId}\",\"exclusionary\":true}}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockGraphGroupRepository.Setup(x => x.IsEmailRecipientMemberOfGroupAsync(_userId, _groupId)).ReturnsAsync(false);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            var part = response.Model.Parts[0];
            Assert.IsTrue(part.Supported);
            Assert.IsTrue(part.Exclusionary);
            Assert.AreEqual(false, part.Included);
        }

        [TestMethod]
        public async Task EvaluatesSqlMembershipFilter_Included()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"exclusionary\":false}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("abc-def");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("abcdef")).ReturnsAsync(true);
            _mockSqlMembershipRepository.Setup(x => x.IsUserInFilterAsync("Department = 'Sales'", "abcdef", _userId)).ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            var part = response.Model.Parts[0];
            Assert.IsTrue(part.Supported);
            Assert.AreEqual(true, part.Included);
        }

        [TestMethod]
        public async Task EvaluatesSqlMembershipOrgStructure_ChecksReportingTree()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"\",\"manager\":{\"id\":1234,\"depth\":2}},\"exclusionary\":false}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("table1");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("table1")).ReturnsAsync(true);
            _mockSqlMembershipRepository
                .Setup(x => x.GetChildEntitiesAsync("", 1234, "table1", 2))
                .ReturnsAsync(new List<PersonEntity> { new PersonEntity { AzureObjectId = _userId } });

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            var part = response.Model.Parts[0];
            Assert.IsTrue(part.Supported);
            Assert.AreEqual(true, part.Included);
        }

        [TestMethod]
        public async Task ReportsUnsupportedPartTypes()
        {
            SetupJob("[{\"type\":\"PlaceMembership\",\"source\":{},\"exclusionary\":false}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.IsTrue(response.Model.HasUnsupportedParts);
            var part = response.Model.Parts[0];
            Assert.IsFalse(part.Supported);
            Assert.IsNull(part.Included);
        }

        [TestMethod]
        public async Task SqlMembership_ReturnsNullIncluded_WhenTableUnavailable()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"exclusionary\":false}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync((string)null!);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            var part = response.Model.Parts[0];
            Assert.IsTrue(part.Supported);
            Assert.IsNull(part.Included);
        }
    }
}
