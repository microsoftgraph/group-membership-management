// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.TeamsChannelMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using TeamsChannel.Service.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;
        private Mock<Microsoft.Azure.WebJobs.ExecutionContext> _executionContext;
        private TelemetryClient _telemetryClient;
        private Mock<ITeamsChannelService> _teamsChannelMembershipObtainerService = null!;
        private ChannelSyncInfo _syncInfo = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        
        [TestInitialize]
        public void SetUp()
        {

            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _loggingRepository = new Mock<ILoggingRepository>();
            _executionContext = new Mock<Microsoft.Azure.WebJobs.ExecutionContext>();
            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();
            _dryRunValue = new Mock<IDryRunValue>();
            _teamsChannelMembershipObtainerService = new Mock<ITeamsChannelService>();

            List<AzureADTeamsUser> testUsers = new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "user1" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "user2" } };


            _teamsChannelMembershipObtainerService.Setup(x => x.GetUsersFromTeamAsync(It.IsAny<AzureADTeamsChannel>(), It.IsAny<Guid>()))
                                  .ReturnsAsync(() => testUsers);
            _teamsChannelMembershipObtainerService.Setup(x => x.VerifyChannelAsync(It.IsAny<ChannelSyncInfo>()))
                                  .ReturnsAsync(() => (new AzureADTeamsChannel(), isGood: true));


            _durableOrchestrationContext.Setup(x => x.GetInput<ChannelSyncInfo>())
                                       .Returns(() => _syncInfo);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallLoggerFunctionAsync(request as LoggerRequest);
                                        });

            (AzureADTeamsChannel parsedChannel, bool isGood) validated = (null, false);
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<(AzureADTeamsChannel parsedChannel, bool isGood)>(It.IsAny<string>(), It.IsAny<ChannelSyncInfo>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            validated = await CallChannelValidatorFunctionAsync(request as ChannelSyncInfo);
                                        })
                                        .ReturnsAsync(() => validated);

            List<AzureADTeamsUser> users = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<AzureADTeamsUser>>(It.IsAny<string>(), It.IsAny<UserReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                           users = await CallUserReaderFunctionAsync(request as UserReaderRequest);
                                        })
                                        .ReturnsAsync(() => users);

            string filename = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<UserUploaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           filename = await CallUserUploaderFunctionAsync(request as UserUploaderRequest);
                                       })
                                       .ReturnsAsync(() => filename);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<QueueMessageSenderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallQueueMessageSenderFunctionAsync(request as QueueMessageSenderRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });


            _syncInfo = new ChannelSyncInfo
            {
                TotalParts = 1,
                CurrentPart = 1,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""objectId"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
                }
            };

        }

        [TestMethod]
        public async Task TestValidRequest()
        {

            _syncInfo = new ChannelSyncInfo
            {
                TotalParts = 2,
                CurrentPart = 1,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""objectId"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
                }
            };


            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _teamsChannelMembershipObtainerService.Object,
                                            _dryRunValue.Object
            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("function finished")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<SyncStatus>()), Times.Never);

        }

        [TestMethod]
        public async Task TestInvalidCurrentPartAsync()
        {
            
            _syncInfo = new ChannelSyncInfo
            {
                TotalParts = 1,
                CurrentPart = 0,
                IsDestinationPart = true,
                SyncJob = new SyncJob
                {
                    RunId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
                    Status = SyncStatus.InProgress.ToString(),
                    TargetOfficeGroupId = Guid.Parse("00000000-0000-0000-0000-000000000042"),
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""objectId"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
                }
            };


            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _teamsChannelMembershipObtainerService.Object,
                                            _dryRunValue.Object
            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("Found invalid value for CurrentPart or TotalParts")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<string>(), It.Is<JobStatusUpdaterRequest>(request => request.Status == SyncStatus.Error)), Times.Once);

        }


        [TestMethod]
        public async Task TestFailedValidation()
        {

            _teamsChannelMembershipObtainerService.Setup(x => x.VerifyChannelAsync(It.IsAny<ChannelSyncInfo>()))
                                   .ReturnsAsync(() => (new AzureADTeamsChannel(), isGood: false));

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object, _dryRunValue.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                               It.Is<LogMessage>(m => m.Message.Contains("Teams Channel Destination did not validate.")),
                                               It.IsAny<VerbosityLevel>(),
                                               It.IsAny<string>(),
                                               It.IsAny<string>()
                                           ), Times.Once);

        }

        [TestMethod]
        public async Task TestUnhandledException()
        {

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<(AzureADTeamsChannel parsedChannel, bool isGood)>(It.IsAny<string>(), It.IsAny<ChannelSyncInfo>()))
                                       .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object, _dryRunValue.Object);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object, _executionContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                               It.Is<LogMessage>(m => m.Message.Contains("Caught unexpected exception:")),
                                               It.IsAny<VerbosityLevel>(),
                                               It.IsAny<string>(),
                                               It.IsAny<string>()
                                           ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<string>(), It.Is<JobStatusUpdaterRequest>(request => request.Status == SyncStatus.Error)), Times.Once);

        }

        private async Task CallLoggerFunctionAsync(LoggerRequest request)
        {
            var loggerFunction = new LoggerFunction(_loggingRepository.Object);
            await loggerFunction.LogMessageAsync(request);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(_loggingRepository.Object, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task<string> CallUserUploaderFunctionAsync(UserUploaderRequest request)
        {
            var function = new UserUploaderFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            return await function.UploadUsersAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(QueueMessageSenderRequest request)
        {
            var function = new QueueMessageSenderFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            await function.SendMessageAsync(request);
        }

        private async Task<List<AzureADTeamsUser>> CallUserReaderFunctionAsync(UserReaderRequest request)
        {
            var function = new UserReaderFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            return await function.ReadUsersAsync(request);
        }

        private async Task<(AzureADTeamsChannel parsedChannel, bool isGood)> CallChannelValidatorFunctionAsync(ChannelSyncInfo request)
        {
            var function = new ChannelValidatorFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            return await function.ValidateChannelAsync(request);
        }

    }
}
