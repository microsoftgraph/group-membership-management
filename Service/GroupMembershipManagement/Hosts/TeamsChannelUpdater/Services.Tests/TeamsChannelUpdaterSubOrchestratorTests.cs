// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Moq;
using Repositories.Contracts;
using Services.TeamsChannelUpdater.Contracts;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Hosts.TeamsChannelUpdater;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;
using Models.ServiceBus;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class TeamsChannelUpdaterSubOrchestratorTests
    {
        private Mock<IDurableOrchestrationContext> _mockDurableOrchestrationContext = null!;
        private Mock<ExecutionContext> _mockExecutionContext = null!;
        private TelemetryClient _mockTelemetryClient = null!;
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private Mock<ITeamsChannelUpdaterService> _mockTeamsChannelUpdaterService = null!;
        private SyncJob _syncJob = null!;
        private TeamsChannelUpdaterSubOrchestratorRequest _input = null!;
        private AzureADTeamsChannel _teamsChannelInfo = null!;

        [TestInitialize]
        public void SetUp()
        {
            var groupMembership = GetGroupMembership();
            var sourceMembers = GetGroupMembership().SourceMembers;

            _syncJob = new SyncJob
            {
                Id = groupMembership.SyncJobId,
                TargetOfficeGroupId = groupMembership.Destination.ObjectId,
                Destination = $"[{{\"value\":{{\"objectId\":\"e9c0ddc4-5379-42a8-bd35-e2f00b584733\",\"channelId\":\"19:O779DDojg816swmRBSbE23yixpmVyzsRV4QmMip_KBA1@thread.tacv2\"}},\"type\":\"TeamsChannelMembership\"}}]",
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domain.com",
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                RunId = groupMembership.RunId
            };

            _teamsChannelInfo = new AzureADTeamsChannel
            {
                Type = "TeamsChannel",
                ObjectId = Guid.Parse("e9c0ddc4-5379-42a8-bd35-e2f00b584733"),
                ChannelId = "19:O779DDojg816swmRBSbE23yixpmVyzsRV4QmMip_KBA1@thread.tacv2"
            };

            _input = new TeamsChannelUpdaterSubOrchestratorRequest
            {
                Type = RequestType.Add,
                Members = sourceMembers,
                RunId = _syncJob.RunId.GetValueOrDefault(Guid.Empty),
                TeamsChannelInfo = _teamsChannelInfo 
            };

            _mockDurableOrchestrationContext = new Mock<IDurableOrchestrationContext>();
            _mockDurableOrchestrationContext.Setup(x => x.GetInput<TeamsChannelUpdaterSubOrchestratorRequest>())
                .Returns(_input);
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()))
                .Callback<string, object>(async (name, request) =>
                {
                    await CallLoggerFunctionAsync(request as LoggerRequest);
                });
            TeamsUpdaterResponse response = new TeamsUpdaterResponse();
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync<TeamsUpdaterResponse>(nameof(TeamsUpdaterFunction), It.IsAny<TeamsUpdaterRequest>()))
                .Callback<string, object>(async (name, request) =>
                {
                    response = await CallTeamsUpdaterFunctionAsync(request as TeamsUpdaterRequest);
                })
                .ReturnsAsync(() => response);

            _mockExecutionContext = new Mock<ExecutionContext>();
            _mockTelemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockTeamsChannelUpdaterService = new Mock<ITeamsChannelUpdaterService>();
        }

        [TestMethod]
        public async Task TestNullRequest()
        {
            _mockDurableOrchestrationContext.Setup(x => x.GetInput<TeamsChannelUpdaterSubOrchestratorRequest>())
                .Returns(() => null);

            var subOrchestratorFunction = new TeamsChannelUpdaterSubOrchestratorFunction(_mockTelemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_mockDurableOrchestrationContext.Object);
            Assert.AreEqual(response.SuccessCount, 0);
        }

        [TestMethod]
        public async Task TestAddUsersSuccess()
        {
            _mockTeamsChannelUpdaterService.Setup(x => x.AddUsersToChannelAsync(_teamsChannelInfo, It.IsAny<List<AzureADTeamsUser>>()))
                .ReturnsAsync((1, new List<AzureADTeamsUser>(), new List<AzureADTeamsUser>()));

            var subOrchestratorFunction = new TeamsChannelUpdaterSubOrchestratorFunction(_mockTelemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_mockDurableOrchestrationContext.Object);

            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("TeamsChannelUpdaterSubOrchestratorFunction function started")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("TeamsChannelUpdaterSubOrchestratorFunction function completed")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            Assert.AreEqual(response.SuccessCount, 1);
        }

        [TestMethod]
        public async Task TestAddUsersTransientException()
        {
            _mockTeamsChannelUpdaterService.SetupSequence(x => x.AddUsersToChannelAsync(_teamsChannelInfo, It.IsAny<List<AzureADTeamsUser>>()))
                .ReturnsAsync((0, GetGroupMembership().SourceMembers, new List<AzureADTeamsUser>()))
                .ReturnsAsync((1, new List<AzureADTeamsUser>(), new List<AzureADTeamsUser>()));

            var subOrchestratorFunction = new TeamsChannelUpdaterSubOrchestratorFunction(_mockTelemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_mockDurableOrchestrationContext.Object);

            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("TeamsChannelUpdaterSubOrchestratorFunction function started")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("TeamsChannelUpdaterSubOrchestratorFunction function completed")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _mockDurableOrchestrationContext.Verify(x => x.CallActivityAsync<TeamsUpdaterResponse>(nameof(TeamsUpdaterFunction), It.IsAny<TeamsUpdaterRequest>()),
                Times.Exactly(2));

            Assert.AreEqual(response.SuccessCount, 1);
        }

        private string _groupMembershipJson = @"
{
    ""Destination"": {
        ""ObjectId"": ""76dfb4cc-d750-4576-8518-3d30bdad7f8d""
    },
    ""SourceMembers"": [
        {
            ""ObjectId"": ""a93204af-4044-e972-fd02-9b7129521231"",
			""MembershipAction"": 1
        }
    ],
    ""RunId"": ""5814efbd-f987-5144-a204-ab8f36b2fb70"",
    ""SyncJobRowKey"": ""17467332-c83b-4a25-a206-de81818af6f0"",
    ""SyncJobPartitionKey"": ""2021-06-28"",
    ""MembershipObtainerDryRunEnabled"": false,
    ""Exclusionary"": false,
    ""Query"": ""[{\""type\"":\""GroupMembership\"",\""source\"":\""ab1dec41-4724-41ca-aa84-113f1e067f54\""}]"",
    ""IsLastMessage"": false,
    ""TotalMessageCount"": 0
}
";

        private TeamsGroupMembership GetGroupMembership()
        {
            var groupMembership = JsonSerializer.Deserialize<TeamsGroupMembership>(_groupMembershipJson);

            return groupMembership;
        }

        private async Task CallLoggerFunctionAsync(LoggerRequest request)
        {
            var loggerFunction = new LoggerFunction(_mockLoggingRepository.Object);
            await loggerFunction.LogMessageAsync(request);
        }

        private async Task<TeamsUpdaterResponse> CallTeamsUpdaterFunctionAsync(TeamsUpdaterRequest request)
        {
            var teamsUpdaterFunction = new TeamsUpdaterFunction(_mockTeamsChannelUpdaterService.Object, _mockLoggingRepository.Object);
            return await teamsUpdaterFunction.RunAsync(request);
        }
    }
}
