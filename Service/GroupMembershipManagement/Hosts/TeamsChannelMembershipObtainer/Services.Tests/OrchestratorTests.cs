// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.TeamsChannelMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Moq;
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

        [TestInitialize]
        public void SetUp()
        {
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _durableOrchestrationContext = new Mock<TaskOrchestrationContext>();
            _dryRunValue = new Mock<IDryRunValue>();
            _teamsChannelMembershipObtainerService = new Mock<ITeamsChannelService>();

            List<AzureADTeamsUser> testUsers = new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "user1" }, new AzureADTeamsUser { ObjectId = Guid.NewGuid(), ConversationMemberId = "user2" } };

            _teamsChannelMembershipObtainerService.Setup(x => x.GetUsersFromTeamAsync(It.IsAny<AzureADTeamsChannel>(), It.IsAny<Guid>()))
                                  .ReturnsAsync(() => testUsers);
            _teamsChannelMembershipObtainerService.Setup(x => x.VerifyChannelAsync(It.IsAny<ChannelSyncInfo>()))
                                  .ReturnsAsync(() => new ValidateChannelResponse
                                  {
                                      ParsedChannel = new AzureADTeamsChannel(),
                                      IsValid = true
                                  });

            _durableOrchestrationContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>()))
                .Returns(NullLogger.Instance);

            _durableOrchestrationContext.Setup(x => x.GetInput<ChannelSyncInfo>())
                                       .Returns(() => _syncInfo);

            var validated = new ValidateChannelResponse { ParsedChannel = null, IsValid = false };
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

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallTelemetryTrackerFunctionAsync(request as TelemetryTrackerRequest);
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

            var orchestratorFunction = new OrchestratorFunction(_dryRunValue.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<ValidateChannelResponse>(It.IsAny<TaskName>(), It.IsAny<ChannelSyncInfo>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<List<AzureADTeamsUser>>(It.IsAny<TaskName>(), It.IsAny<UserReaderRequest>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileUploaderRequest>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageSenderRequest>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()), Times.Never);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Never);
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

            var orchestratorFunction = new OrchestratorFunction(_dryRunValue.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.Is<JobStatusUpdaterRequest>(request => request.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<ValidateChannelResponse>(It.IsAny<TaskName>(), It.IsAny<ChannelSyncInfo>(), It.IsAny<TaskOptions>()), Times.Never);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<List<AzureADTeamsUser>>(It.IsAny<TaskName>(), It.IsAny<UserReaderRequest>(), It.IsAny<TaskOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task TestFailedValidation()
        {
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<ValidateChannelResponse>(It.IsAny<TaskName>(), It.IsAny<ChannelSyncInfo>(), It.IsAny<TaskOptions>()))
                                       .ReturnsAsync(() => new ValidateChannelResponse
                                       {
                                           ParsedChannel = new AzureADTeamsChannel(),
                                           IsValid = false
                                       });

            var orchestratorFunction = new OrchestratorFunction(_dryRunValue.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<ValidateChannelResponse>(It.IsAny<TaskName>(), It.IsAny<ChannelSyncInfo>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<List<AzureADTeamsUser>>(It.IsAny<TaskName>(), It.IsAny<UserReaderRequest>(), It.IsAny<TaskOptions>()), Times.Never);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileUploaderRequest>(), It.IsAny<TaskOptions>()), Times.Never);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<QueueMessageSenderRequest>(), It.IsAny<TaskOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task TestUnhandledException()
        {
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<ValidateChannelResponse>(It.IsAny<TaskName>(), It.IsAny<ChannelSyncInfo>(), It.IsAny<TaskOptions>()))
                                       .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(_dryRunValue.Object);
            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object));

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.Is<JobStatusUpdaterRequest>(request => request.Status == SyncStatus.Error), It.IsAny<TaskOptions>()), Times.Once);
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()), Times.Once);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var function = new TelemetryTrackerFunction(NullLogger<TelemetryTrackerFunction>.Instance, _telemetryClient);
            await function.TrackEventAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, _teamsChannelMembershipObtainerService.Object);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task<string> CallFileUploaderFunctionAsync(FileUploaderRequest request)
        {
            var function = new FileUploaderFunction(NullLogger<FileUploaderFunction>.Instance, _teamsChannelMembershipObtainerService.Object);
            return await function.UploadFileAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(QueueMessageSenderRequest request)
        {
            var function = new QueueMessageSenderFunction(NullLogger<QueueMessageSenderFunction>.Instance, _teamsChannelMembershipObtainerService.Object);
            await function.SendMessageAsync(request);
        }

        private async Task<List<AzureADTeamsUser>> CallUserReaderFunctionAsync(UserReaderRequest request)
        {
            var function = new UserReaderFunction(NullLogger<UserReaderFunction>.Instance, _teamsChannelMembershipObtainerService.Object);
            return await function.ReadUsersAsync(request);
        }

        private async Task<ValidateChannelResponse> CallChannelValidatorFunctionAsync(ChannelSyncInfo request)
        {
            var function = new ChannelValidatorFunction(NullLogger<ChannelValidatorFunction>.Instance, _teamsChannelMembershipObtainerService.Object);
            return await function.ValidateChannelAsync(request);
        }
    }
}