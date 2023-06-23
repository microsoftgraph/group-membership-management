// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.TeamsChannelMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.DurableTask.Protobuf;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.ServiceBus;
using Moq;
using Moq.Protected;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.FeatureFlag;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading;
using TeamsChannel.Service;
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


            _durableOrchestrationContext.Setup(x => x.GetInput<ChannelSyncInfo>())
                                       .Returns(() => _syncInfo);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<LoggerRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallLoggerFunctionAsync(request as LoggerRequest);
                                        });

            _teamsChannelMembershipObtainerService.Setup(x => x.VerifyChannelAsync(It.IsAny<ChannelSyncInfo>()))
                                   .ReturnsAsync(() => (new AzureADTeamsChannel(), isGood: true));

            (AzureADTeamsChannel parsedChannel, bool isGood) validated = (null, false);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<(AzureADTeamsChannel parsedChannel, bool isGood)>(It.IsAny<string>(), It.IsAny<ChannelSyncInfo>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            validated = await CallChannelValidatorFunctionAsync(request as ChannelSyncInfo);
                                        })
                                        .ReturnsAsync(() => validated);

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
                    Query = @"[{""type"":""SecurityGroup"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""groupId"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
                }
            };
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
                    Query = @"[{""type"":""SecurityGroup"",""source"":""00000000-0000-0000-0000-000000000000""}]",
                    Destination = @"[{""type"":""TeamsChannel"",""value"":{""groupId"":""00000000-0000-0000-0000-000000000000"", ""channelId"":""some channel""}}]"
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
                                               It.Is<LogMessage>(m => m.Message.Contains("Target office group did not validate.")),
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

        private async Task<(AzureADTeamsChannel parsedChannel, bool isGood)> CallChannelValidatorFunctionAsync(ChannelSyncInfo request)
        {
            var function = new ChannelValidatorFunction(_loggingRepository.Object, _teamsChannelMembershipObtainerService.Object);
            return await function.ValidateChannelAsync(request);
        }

    }
}
