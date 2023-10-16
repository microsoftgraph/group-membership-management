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
using Repositories.Contracts.InjectConfig;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDurableOrchestrationContext> _mockDurableOrchestrationContext = null!;
        private Mock<ExecutionContext> _mockExecutionContext = null!;
        private TelemetryClient _mockTelemetryClient = null!;
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private Mock<IEmailSenderRecipient> _mockEmailSenderAndRecipients = null!;
        private Mock<IGMMResources> _mockGMMResources = null!;
        private Mock<ITeamsChannelUpdaterService> _mockTeamsChannelUpdaterService = null!;
        private SyncJob _syncJob = null!;

        private string _groupName = "Group 1 Display Name";
        private List<AzureADUser> _groupOwnerList = new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() }, new AzureADUser { ObjectId = Guid.NewGuid() } };

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

            var input = new MembershipHttpRequest
            {
                FilePath = "/file/path/name.json",
                SyncJob = _syncJob
            };

            _mockDurableOrchestrationContext = new Mock<IDurableOrchestrationContext>();
            _mockDurableOrchestrationContext.Setup(x => x.GetInput<MembershipHttpRequest>())
                .Returns(input);
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync<SyncJob>(nameof(JobReaderFunction), It.IsAny<JobReaderRequest>()))
                .ReturnsAsync(_syncJob);
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(nameof(FileDownloaderFunction), It.IsAny<FileDownloaderRequest>()))
                .ReturnsAsync(_groupMembershipJson);
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()))
                .Callback<string, object>(async (name, request) =>
                {
                    await CallLoggerFunctionAsync(request as LoggerRequest);
                });
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>()))
                .Callback<string, object>(async (name, request) =>
                {
                    await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                });
            string groupName = "";
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(nameof(GroupNameReaderFunction), It.IsAny<GroupNameReaderRequest>()))
                .Callback<string, object>(async (name, request) =>
                {
                    groupName = await CallGroupNameReaderFunctionAsync(request as GroupNameReaderRequest);
                })
                .ReturnsAsync(() => groupName);
            List<AzureADUser> owners = new List<AzureADUser>();
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync<List<AzureADUser>>(nameof(GroupOwnersReaderFunction), It.IsAny<GroupOwnersReaderRequest>()))
                .Callback<string, object>(async (name, request) =>
                {
                    owners = await CallGroupOwnersReaderFunctionAsync(request as GroupOwnersReaderRequest);
                })
                .ReturnsAsync(() => owners);
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync(nameof(EmailSenderFunction), It.IsAny<EmailSenderRequest>()))
                .Callback<string, object>(async (name, request) =>
                {
                    await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
                });


            _mockExecutionContext = new Mock<ExecutionContext>();
            _mockTelemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockEmailSenderAndRecipients = new Mock<IEmailSenderRecipient>();
            _mockGMMResources = new Mock<IGMMResources>();
            _mockTeamsChannelUpdaterService = new Mock<ITeamsChannelUpdaterService>();
            _mockTeamsChannelUpdaterService.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                .ReturnsAsync(_syncJob);
            _mockTeamsChannelUpdaterService.Setup(repo => repo.GetGroupNameAsync(_syncJob.TargetOfficeGroupId, It.IsAny<Guid>()))
                .ReturnsAsync(() => _groupName);
            _mockTeamsChannelUpdaterService.Setup(repo => repo.GetGroupOwnersAsync(_syncJob.TargetOfficeGroupId, It.IsAny<Guid>(), 0))
                .ReturnsAsync(() => _groupOwnerList);

        }

        [TestMethod]
        public async Task TestOrchestratorOngoingSync()
        {
            _mockDurableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<TeamsChannelUpdaterSubOrchestratorResponse>(nameof(TeamsChannelUpdaterSubOrchestratorFunction), It.IsAny<TeamsChannelUpdaterSubOrchestratorRequest>()))
                .ReturnsAsync(new TeamsChannelUpdaterSubOrchestratorResponse
                {
                    Type = RequestType.Add,
                    SuccessCount = 1,
                    UsersNotFound = new List<AzureADTeamsUser>(),
                    UsersFailed = new List<AzureADTeamsUser>()
                });

            var orchestratorFunction = new OrchestratorFunction(_mockLoggingRepository.Object,
                _mockTelemetryClient,
                _mockEmailSenderAndRecipients.Object,
                _mockGMMResources.Object);

            await orchestratorFunction.RunOrchestratorAsync(_mockDurableOrchestrationContext.Object, _mockExecutionContext.Object);

            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("OrchestratorFunction function started")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("OrchestratorFunction function completed")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
            _mockLoggingRepository.Verify(x => x.RemoveSyncJobProperties(It.IsAny<Guid>()), Times.Once());

            _mockTeamsChannelUpdaterService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncJob>(), SyncStatus.Idle, false, It.IsAny<Guid>())); _mockTeamsChannelUpdaterService.Verify(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task TestOrchestratorInitialSync()
        {
            _syncJob.LastRunTime = DateTime.FromFileTimeUtc(0);
            _mockDurableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<TeamsChannelUpdaterSubOrchestratorResponse>(nameof(TeamsChannelUpdaterSubOrchestratorFunction), It.IsAny<TeamsChannelUpdaterSubOrchestratorRequest>()))
                .ReturnsAsync(new TeamsChannelUpdaterSubOrchestratorResponse
                {
                    Type = RequestType.Add,
                    SuccessCount = 1,
                    UsersNotFound = new List<AzureADTeamsUser>(),
                    UsersFailed = new List<AzureADTeamsUser>()
                });

            var orchestratorFunction = new OrchestratorFunction(_mockLoggingRepository.Object,
                _mockTelemetryClient,
                _mockEmailSenderAndRecipients.Object,
                _mockGMMResources.Object);

            await orchestratorFunction.RunOrchestratorAsync(_mockDurableOrchestrationContext.Object, _mockExecutionContext.Object);

            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("OrchestratorFunction function started")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("OrchestratorFunction function completed")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
            _mockLoggingRepository.Verify(x => x.RemoveSyncJobProperties(It.IsAny<Guid>()), Times.Once());

            _mockTeamsChannelUpdaterService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncJob>(), SyncStatus.Idle, false, It.IsAny<Guid>()));
            _mockTeamsChannelUpdaterService.Verify(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<Guid>(), 
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestOrchestratorException()
        {
            _mockDurableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(nameof(FileDownloaderFunction), It.IsAny<FileDownloaderRequest>()))
                .ThrowsAsync(new FileNotFoundException());

            var orchestratorFunction = new OrchestratorFunction(_mockLoggingRepository.Object,
                _mockTelemetryClient,
                _mockEmailSenderAndRecipients.Object,
                _mockGMMResources.Object);
            
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () => await orchestratorFunction.RunOrchestratorAsync(_mockDurableOrchestrationContext.Object, _mockExecutionContext.Object));


            _mockLoggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("Caught unexpected exception")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);
            _mockLoggingRepository.Verify(x => x.RemoveSyncJobProperties(It.IsAny<Guid>()), Times.Once());

            _mockTeamsChannelUpdaterService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncJob>(), SyncStatus.Error, false, It.IsAny<Guid>()));
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
        },
        {
            ""Properties"": {
                ""ConversationMemberId"": ""MCMjMiMjZTM3ZjJjYTktZGVjYi00NzIzLWExOTctMTVkNjNiOTZiZjczIyMxOTo1MzdkZDk0NWJkOWQ0N2JjYTY2MDk1YmFlN2Q0MjQ2N0B0aHJlYWQudGFjdjIjIzMyMjdhNmFlLWZkMDItNDA0NC05YjcxLWU5NzI3NTQ3ZWM0NA==""
            },
            ""ObjectId"": ""3227a6ae-fd02-4044-9b71-e9727547ec44"",
			""MembershipAction"": 2
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
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.Converters.Add(new AzureADTeamsUserConverter());
            var groupMembership = JsonSerializer.Deserialize<TeamsGroupMembership>(_groupMembershipJson, options);

            return groupMembership;
        }

        private async Task CallLoggerFunctionAsync(LoggerRequest request)
        {
            var loggerFunction = new LoggerFunction(_mockLoggingRepository.Object);
            await loggerFunction.LogMessageAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(_mockLoggingRepository.Object, _mockTeamsChannelUpdaterService.Object);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }

        private async Task<string> CallGroupNameReaderFunctionAsync(GroupNameReaderRequest request)
        {
            var groupNameReaderFunction = new GroupNameReaderFunction(_mockLoggingRepository.Object, _mockTeamsChannelUpdaterService.Object);
            return await groupNameReaderFunction.GetGroupNameAsync(request);
        }

        private async Task<List<AzureADUser>> CallGroupOwnersReaderFunctionAsync(GroupOwnersReaderRequest request)
        {
            var groupOwnersReaderFunction = new GroupOwnersReaderFunction(_mockLoggingRepository.Object, _mockTeamsChannelUpdaterService.Object);
            return await groupOwnersReaderFunction.GetGroupOwnersAsync(request);
        }

        private async Task CallEmailSenderFunctionAsync(EmailSenderRequest request)
        {
            var emailSenderFunction = new EmailSenderFunction(_mockLoggingRepository.Object, _mockTeamsChannelUpdaterService.Object);
            await emailSenderFunction.SendEmailAsync(request);
        }

        private async Task CallTeamsSubOrchestratorFunctionAsync(TeamsChannelUpdaterSubOrchestratorRequest request)
        {
            var teamsSubOrchestratorFunction = new TeamsChannelUpdaterSubOrchestratorFunction(_mockTelemetryClient);
            await teamsSubOrchestratorFunction.RunSubOrchestratorAsync(_mockDurableOrchestrationContext.Object);
        }
    }
}
