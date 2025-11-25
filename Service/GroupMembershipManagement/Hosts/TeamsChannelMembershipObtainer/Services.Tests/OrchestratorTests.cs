// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.TeamsChannelMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<TaskOrchestrationContext> _durableOrchestrationContext;
        private TelemetryClient _telemetryClient;
        private Mock<ITeamsChannelService> _teamsChannelMembershipObtainerService = null!;
        private ChannelSyncInfo _syncInfo = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;

        [TestInitialize]
        public void SetUp()
        {

            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _loggingRepository = new Mock<ILoggingRepository>();
            _durableOrchestrationContext = new Mock<TaskOrchestrationContext>();
            _dryRunValue = new Mock<IDryRunValue>();
            _teamsChannelMembershipObtainerService = new Mock<ITeamsChannelService>();

            List<AzureADTeamsUser> testUsers = new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "user1" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "user2" } };

            Channel destination = new Channel { GroupId = Guid.NewGuid(), ChannelId = "some-channel" };

            _teamsChannelMembershipObtainerService.Setup(x => x.GetUsersFromTeamAsync(It.IsAny<AzureADTeamsChannel>(), It.IsAny<Guid>()))
                                  .ReturnsAsync(() => testUsers);
            _teamsChannelMembershipObtainerService.Setup(x => x.VerifyChannelAsync(It.IsAny<ChannelSyncInfo>()))
                                  .ReturnsAsync(() => (new ValidateChannelResponse{
                                      ParsedChannel = new AzureADTeamsChannel(),
                                      IsValid = true
                                  }));


            _durableOrchestrationContext.Setup(x => x.GetInput<ChannelSyncInfo>())
                                       .Returns(() => _syncInfo);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<LoggerRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            await CallLoggerFunctionAsync(request as LoggerRequest);
                                        });

            var validated = new ValidateChannelResponse{ ParsedChannel = null, IsValid = false };
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<ValidateChannelResponse>(It.IsAny<TaskName>(), It.IsAny<ChannelSyncInfo>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            validated = await CallChannelValidatorFunctionAsync(request as ChannelSyncInfo);
                                        })
                                        .ReturnsAsync(() => validated);

            List<AzureADTeamsUser> users = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<AzureADTeamsUser>>(It.IsAny<TaskName>(), It.IsAny<UserReaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                           users = await CallUserReaderFunctionAsync(request as UserReaderRequest);
                                        })
                                        .ReturnsAsync(() => users);

            string filename = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileUploaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           filename = await CallFileUploaderFunctionAsync(request as FileUploaderRequest);
                                       })
                                       .ReturnsAsync(() => filename);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageSenderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            await CallQueueMessageSenderFunctionAsync(request as QueueMessageSenderRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                        {
                                            await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.ToString() == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
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
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    MembershipType = "TeamsChannelMembership",
                    Channel = new Channel
                    {
                        GroupId = Guid.NewGuid(),
                        ChannelId = "some-channel"
                    }
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
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    MembershipType = "TeamsChannelMembership",
                    Channel = new Channel
                    {
                        GroupId = Guid.NewGuid(),
                        ChannelId = "some-channel"
                    }
                }
            };


            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _teamsChannelMembershipObtainerService.Object,
                                            _dryRunValue.Object
            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("function finished")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<SyncStatus>(), It.IsAny<TaskOptions>()), Times.Never);

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
                    Timestamp = new DateTimeOffset(1995, 03, 28, 1, 2, 3, TimeSpan.Zero),
                    Query = @"[{""type"":""GroupMembership"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    MembershipType = "TeamsChannelMembership",
                    Channel = new Channel
                    {
                        GroupId = Guid.NewGuid(),
                        ChannelId = "some-channel"
                    }
                }
            };


            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _teamsChannelMembershipObtainerService.Object,
                                            _dryRunValue.Object
            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("Found invalid value for CurrentPart or TotalParts")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.Is<JobStatusUpdaterRequest>(request => request.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Once);

        }


        [TestMethod]
        public async Task TestFailedValidation()
        {

            _teamsChannelMembershipObtainerService.Setup(x => x.VerifyChannelAsync(It.IsAny<ChannelSyncInfo>()))
                                   .ReturnsAsync(() => (new ValidateChannelResponse
                                    {
                                        ParsedChannel = new AzureADTeamsChannel(),
                                        IsValid = false
                                    }));

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object, _dryRunValue.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

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

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<ValidateChannelResponse>(It.IsAny<TaskName>(), It.IsAny<ChannelSyncInfo>(), It.IsAny<TaskOptions>()))
                                       .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object, _dryRunValue.Object);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                               It.Is<LogMessage>(m => m.Message.Contains("Caught unexpected exception:")),
                                               It.IsAny<VerbosityLevel>(),
                                               It.IsAny<string>(),
                                               It.IsAny<string>()
                                           ), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.Is<JobStatusUpdaterRequest>(request => request.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Once);

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

        private async Task<string> CallFileUploaderFunctionAsync(FileUploaderRequest request)
        {
            var function = new FileUploaderFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            return await function.UploadFileAsync(request);
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

        private async Task<ValidateChannelResponse> CallChannelValidatorFunctionAsync(ChannelSyncInfo request)
        {
            var function = new ChannelValidatorFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            return await function.ValidateChannelAsync(request);
        }

    }
}


