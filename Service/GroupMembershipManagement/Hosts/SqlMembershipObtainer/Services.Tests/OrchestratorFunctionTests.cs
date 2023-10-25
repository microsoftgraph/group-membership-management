// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Newtonsoft.Json;
using SqlMembershipObtainer;
using SqlMembershipObtainer.SubOrchestrator;
using Repositories.Contracts;
using Services.Contracts;
using Services.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private int _profilesCount = 10;
        private List<GraphProfileInformation> _profiles;
        private Mock<IConfiguration> _configuration;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IDurableOrchestrationContext> _context;
        private Mock<Microsoft.Azure.WebJobs.ExecutionContext> _executionContext;
        private SyncJob _syncJob;
        private OrchestratorRequest _mainRequest;
        private GraphProfileInformationResponse _graphProfileInformationResponse;
        private SyncStatus _senderResponseStatus = SyncStatus.InProgress;
        private string _senderResponseFilePath = "file-path";
        private DurableHttpResponse _membershipAggregatorResponse;
        private TelemetryClient _telemetryClient;
        private Mock<ISqlMembershipObtainerService> _sqlMembershipObtainerService;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;

        [TestInitialize]
        public void Setup()
        {
            _configuration = new Mock<IConfiguration>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _sqlMembershipObtainerService = new Mock<ISqlMembershipObtainerService>();
            _context = new Mock<IDurableOrchestrationContext>();
            _executionContext = new Mock<Microsoft.Azure.WebJobs.ExecutionContext>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();

            _syncJob = SqlMembershipJobCreator.CreateSampleSyncJobs(1, "SqlMembership", 24).First();

            _mainRequest = new OrchestratorRequest
            {
                CurrentPart = 1,
                TotalParts = 2,
                SyncJob = _syncJob
            };

            _profiles = new List<GraphProfileInformation>();
            for (int i = 0; i < _profilesCount; i++)
            {
                _profiles.Add(new GraphProfileInformation
                {
                    Id = Guid.NewGuid().ToString(),
                    PersonnelNumber = (i + 1).ToString(),
                    UserPrincipalName = $"user{i}@domain.com"
                });
            }

            _graphProfileInformationResponse = new GraphProfileInformationResponse
            {
                GraphProfiles = TextCompressor.Compress(JsonConvert.SerializeObject(_profiles)),
                GraphProfileCount = _profiles.Count
            };

            _membershipAggregatorResponse = new DurableHttpResponse(HttpStatusCode.NoContent);

            _context.Setup(x => x.GetInput<OrchestratorRequest>()).Returns(() => _mainRequest);
            _context.Setup(x => x.CallActivityAsync(nameof(LoggerFunction), It.IsAny<LoggerRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        await CallLoggerFunctionAsync(request as LoggerRequest);
                    });

            _context.Setup(x => x.CallSubOrchestratorAsync<GraphProfileInformationResponse>(
                                                        nameof(OrganizationProcessorFunction),
                                                        It.IsAny<OrganizationProcessorRequest>()
                                                        ))
                    .ReturnsAsync(() => _graphProfileInformationResponse);

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var telemetryRequest = request as TelemetryTrackerRequest;
                        await CallTelemetryTrackerFunctionAsync(telemetryRequest);
                    });

            _context.Setup(x => x.CallActivityAsync<(SyncStatus Status, string FilePath)>(
                                                        nameof(GroupMembershipSenderFunction),
                                                        It.IsAny<GroupMembershipSenderRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        await CallGroupMembershipSenderFunctionAsync(request as GroupMembershipSenderRequest);
                    })
                    .ReturnsAsync(() => (_senderResponseStatus, _senderResponseFilePath));

            _context.Setup(x => x.CallHttpAsync(It.IsAny<DurableHttpRequest>())).ReturnsAsync(() => _membershipAggregatorResponse);

            _context.Setup(x => x.CallActivityAsync(nameof(QueueMessageSenderFunction), It.IsAny<MembershipAggregatorHttpRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallQueueMessageSenderFunctionAsync(request as MembershipAggregatorHttpRequest);
                                        });
        }

        [TestMethod]
        public async Task TestValidSqlMembershipQueryAsync()
        {
            List<GraphProfileInformation> profilesSent = null;
            _sqlMembershipObtainerService.Setup(x => x.SendGroupMembershipAsync(
                                                        It.IsAny<List<GraphProfileInformation>>(),
                                                        It.IsAny<SyncJob>(),
                                                        It.IsAny<int>(),
                                                        It.IsAny<bool>(),
                                                        It.IsAny<string>()))
                                .Callback<List<GraphProfileInformation>, SyncJob, int, bool, string>((profiles, syncJob, currentPart, exclusionary, directory) =>
                                {
                                    profilesSent = profiles;
                                })
                                .ReturnsAsync(() => (_senderResponseStatus, _senderResponseFilePath));

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.StartsWith($"Retrieved {_profilesCount} total profiles from SqlMembershipObtainer")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()), Times.Once());

            _sqlMembershipObtainerService.Verify(x => x.SendGroupMembershipAsync(It.IsAny<List<GraphProfileInformation>>(),
                                                        It.IsAny<SyncJob>(),
                                                        It.IsAny<int>(),
                                                        It.IsAny<bool>(),
                                                        It.IsAny<string>()), Times.Once());

            Assert.AreEqual(_profilesCount, profilesSent.Count);
            Assert.IsTrue(profilesSent.All(x => _profiles.Contains(x)));
        }

        [TestMethod]
        [DataRow("[{\"type\":\"SqlMembership\"}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\": null }]")]
        public async Task TestEmptySqlMembershipQueryAsync(string query)
        {
            _syncJob.Query = query;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"The job Id:{_syncJob.Id} Part#{_mainRequest.CurrentPart} does not have a valid query")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task TestInvalidSqlMembershipQueryAsync()
        {
            _syncJob.Query = "some-invalid-query";

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"The job Id:{_syncJob.Id} Part#{_mainRequest.CurrentPart} does not have a valid query")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.QueryNotValid)), Times.Once());
        }

        [TestMethod]
        public async Task TestInvalidFilePathFailureAsync()
        {
            _senderResponseFilePath = null;

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.StartsWith($"Retrieved {_profilesCount} total profiles from SqlMembershipObtainer")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()), Times.Once());

            _loggingRepository.Verify(x => x.LogMessageAsync(
                    It.Is<LogMessage>(m => m.Message.StartsWith($"Membership file path is not valid, marking sync job as {SyncStatus.FilePathNotValid}")),
                    It.IsAny<VerbosityLevel>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.FilePathNotValid)), Times.Once());
        }

        [TestMethod]
        public async Task TestFailJobOnFrameworkDataProviderErrorAsync()
        {
            _senderResponseFilePath = null;
            var originalStartDate = _syncJob.StartDate = DateTime.UtcNow;

            //Won't reschedule job if it's been more than Period + 2hrs since it ran successfully
            var hoursSinceLastSuccessfulRun = _syncJob.Period + 3;
            _syncJob.LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-hoursSinceLastSuccessfulRun);

            _context.Setup(x => x.CurrentUtcDateTime).Returns(originalStartDate);
            _context.Setup(x => x.CallSubOrchestratorAsync<GraphProfileInformationResponse>(
                                                      nameof(OrganizationProcessorFunction),
                                                      It.IsAny<OrganizationProcessorRequest>()
                                                      ))
                    .ThrowsAsync(new Exception("Internal .NET Framework Data Provider error 6"));

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.StartsWith($"Rescheduling job at")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()), Times.Never());

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.Error)), Times.Once());
        }

        [TestMethod]
        [ExpectedException(typeof(Microsoft.Data.SqlClient.SqlException))]
        public async Task TestFailJobOnSqlExceptionAsync()
        {
            _context.Setup(x => x.CallSubOrchestratorAsync<GraphProfileInformationResponse>(
                                                      nameof(OrganizationProcessorFunction),
                                                      It.IsAny<OrganizationProcessorRequest>()
                                                      ))
                    .ThrowsAsync(MakeSqlException());

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object, _loggingRepository.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object, _executionContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.StartsWith($"Caught SqlException")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.Error)), Times.Once());
        }

        public static SqlException MakeSqlException()
        {
            SqlException exception = null;
            try
            {
                SqlConnection conn = new SqlConnection(@"Data Source=.;Database=GUARANTEED_TO_FAIL;Connection Timeout=1");
                conn.Open();
            }
            catch (SqlException ex)
            {
                exception = ex;
            }
            return (exception);
        }

        private async Task CallTelemetryTrackerFunctionAsync(TelemetryTrackerRequest request)
        {
            var telemetryTrackerFunction = new TelemetryTrackerFunction(_loggingRepository.Object, _telemetryClient);
            await telemetryTrackerFunction.TrackEventAsync(request);
        }

        private async Task CallLoggerFunctionAsync(LoggerRequest request)
        {
            var function = new LoggerFunction(_loggingRepository.Object);
            await function.LogMessageAsync(request);
        }

        private async Task<(SyncStatus Status, string FilePath)> CallGroupMembershipSenderFunctionAsync(GroupMembershipSenderRequest request)
        {
            var function = new GroupMembershipSenderFunction(_sqlMembershipObtainerService.Object, _loggingRepository.Object);
            return await function.SendGroupMembershipAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(MembershipAggregatorHttpRequest request)
        {
            var function = new QueueMessageSenderFunction(_loggingRepository.Object, _serviceBusQueueRepository.Object);
            await function.SendMessageAsync(request);
        }
    }
}
