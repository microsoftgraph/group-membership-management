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

        private const string AdfRunId = "0f8fad5b-d9cb-469f-a165-70867728950e";
        private const string AdfTableName = "0f8fad5bd9cb469fa16570867728950e";

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

        private static bool MatchesRequest(
            SqlMembershipEvaluationRequest request,
            int? managerEmployeeId,
            int depth,
            string filter,
            IdentityTypeSet identityTypes,
            string tableName)
        {
            return request.ManagerEmployeeId == managerEmployeeId &&
                   request.Depth == depth &&
                   request.Filter == filter &&
                   request.IdentityTypes == identityTypes &&
                   request.TableName == tableName;
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
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(AdfRunId);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(AdfTableName)).ReturnsAsync(true);
            _mockSqlMembershipRepository
                .Setup(x => x.IsIdentitySelectedAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        MatchesRequest(
                            request,
                            null,
                            0,
                            "Department = 'Sales'",
                            IdentityTypeSet.UsersOnly,
                            AdfTableName)),
                    Guid.Parse(_userId),
                    IdentityType.Users))
                .ReturnsAsync(true);

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
                .Setup(x => x.IsIdentitySelectedAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        MatchesRequest(
                            request,
                            1234,
                            2,
                            string.Empty,
                            IdentityTypeSet.UsersOnly,
                            "table1")),
                    Guid.Parse(_userId),
                    IdentityType.Users))
                .ReturnsAsync(true);

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

        [TestMethod]
        public async Task SqlMembership_AgentsOnly_UsesSharedAgentProbe()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"users.Department = 'Sales' AND agents.BlueprintId = 'approved'\"},\"identityTypes\":[\"agents\"]}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync(AdfRunId);
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(AdfTableName)).ReturnsAsync(true);
            _mockSqlMembershipRepository
                .Setup(x => x.IsIdentitySelectedAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        MatchesRequest(
                            request,
                            null,
                            0,
                            "users.Department = 'Sales' AND agents.BlueprintId = 'approved'",
                            IdentityTypeSet.Of(IdentityType.Agents),
                            AdfTableName)),
                    Guid.Parse(_userId),
                    IdentityType.Agents))
                .ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(true, response.Model.Parts[0].Included);
            _mockSqlMembershipRepository.Verify(
                x => x.IsIdentitySelectedAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>(),
                    It.IsAny<Guid>(),
                    IdentityType.Users),
                Times.Never);
        }

        [TestMethod]
        public async Task SqlMembership_BothSelected_ReturnsAgentMatchAfterUserMiss()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"users\",\"agents\"]}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("table1");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("table1")).ReturnsAsync(true);
            _mockSqlMembershipRepository
                .Setup(x => x.IsIdentitySelectedAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        request.IdentityTypes == IdentityTypeSet.All),
                    Guid.Parse(_userId),
                    IdentityType.Users))
                .ReturnsAsync(false);
            _mockSqlMembershipRepository
                .Setup(x => x.IsIdentitySelectedAsync(
                    It.Is<SqlMembershipEvaluationRequest>(request =>
                        request.IdentityTypes == IdentityTypeSet.All),
                    Guid.Parse(_userId),
                    IdentityType.Agents))
                .ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(true, response.Model.Parts[0].Included);
        }

        [TestMethod]
        public async Task SqlMembership_UnavailableRequiredInput_ReturnsIndeterminate()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"agents.BlueprintId = 'approved'\"},\"identityTypes\":[\"users\"]}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("table1");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("table1")).ReturnsAsync(true);
            _mockSqlMembershipRepository
                .Setup(x => x.IsIdentitySelectedAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>(),
                    Guid.Parse(_userId),
                    IdentityType.Users))
                .ThrowsAsync(new InvalidOperationException("Required agents input is unavailable."));

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNull(response.Model.Parts[0].Included);
        }

        [TestMethod]
        [DataRow("\"identityTypes\":[]")]
        [DataRow("\"identityTypes\":null")]
        [DataRow("\"identityTypes\":\"agents\"")]
        [DataRow("\"identityTypes\":[\"users\",\"users\"]")]
        [DataRow("\"identityTypes\":[\"Users\"]")]
        public async Task SqlMembership_InvalidSelection_ReturnsIndeterminate(string selectionJson)
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"}," + selectionJson + "}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("table1");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("table1")).ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.IsNull(response.Model.Parts[0].Included);
            _mockSqlMembershipRepository.Verify(
                x => x.IsIdentitySelectedAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>(),
                    It.IsAny<Guid>(),
                    It.IsAny<IdentityType>()),
                Times.Never);
        }

        [TestMethod]
        [DataRow("{}")]
        [DataRow("{\"filter\":\"\"}")]
        [DataRow("{\"filter\":\"   \"}")]
        [DataRow("{\"manager\":{\"id\":0},\"filter\":\"\"}")]
        public async Task SqlMembership_NoManagerAndNoFilter_SelectsNobody(string sourceJson)
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":" + sourceJson + ",\"identityTypes\":[\"users\",\"agents\"]}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("table1");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("table1")).ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(false, response.Model.Parts[0].Included);
            _mockSqlMembershipRepository.Verify(
                x => x.IsIdentitySelectedAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>(),
                    It.IsAny<Guid>(),
                    It.IsAny<IdentityType>()),
                Times.Never);
        }

        [TestMethod]
        public async Task SqlMembership_MisplacedSelection_ReturnsIndeterminate()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\",\"identityTypes\":[\"agents\"]}}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("table1");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("table1")).ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.IsNull(response.Model.Parts[0].Included);
        }

        [TestMethod]
        public async Task SqlMembership_SelectionKeyWithDifferentLetterCase_ReturnsIndeterminate()
        {
            SetupJob("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"IdentityTypes\":[\"agents\"]}]");
            _mockGraphGroupRepository.Setup(x => x.GetUserAccountEnabledAsync(_userId)).ReturnsAsync(true);
            _mockDataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("table1");
            _mockSqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync("table1")).ReturnsAsync(true);

            var response = await _handler.ExecuteAsync(new SpotCheckUserMembershipRequest(_syncJobId, _userId));

            Assert.IsNull(response.Model.Parts[0].Included);
            _mockSqlMembershipRepository.Verify(
                x => x.IsIdentitySelectedAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>(),
                    It.IsAny<Guid>(),
                    It.IsAny<IdentityType>()),
                Times.Never);
        }
    }
}
