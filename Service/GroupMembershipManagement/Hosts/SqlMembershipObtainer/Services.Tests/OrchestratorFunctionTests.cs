// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using Services.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Polly;
using SqlMembershipObtainer;
using SqlMembershipObtainer.SubOrchestrator;
using System.Net;
using SqlMembershipObtainer.Entities;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private int _profilesCount = 10;
        private List<GraphProfileInformation> _profiles;
        private Mock<TaskOrchestrationContext> _context;
        private Mock<FunctionContext> _functionContext;
        private SyncJob _syncJob;
        private OrchestratorRequest _mainRequest;
        private MembershipFileResult _groupMembershipSenderResponse;
        private SyncStatus _senderResponseStatus = SyncStatus.InProgress;
        private string _senderResponseFilePath = "file-path";
        private TelemetryClient _telemetryClient;
        private Mock<ISqlMembershipObtainerService> _sqlMembershipObtainerService;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        SchemaProvider _schemaProvider;
        private bool _isValid = true;

        [TestInitialize]
        public void Setup()
        {
            _sqlMembershipObtainerService = new Mock<ISqlMembershipObtainerService>();
            _context = new Mock<TaskOrchestrationContext>();
            _functionContext = new Mock<FunctionContext>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();

            _syncJob = SqlMembershipJobCreator.CreateSampleSyncJobs(1, "SqlMembership", 24).First();

            _mainRequest = new OrchestratorRequest
            {
                CurrentPart = 1,
                TotalParts = 2,
                SyncJob = _syncJob,
                Exclusionary = false
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

            _groupMembershipSenderResponse = new MembershipFileResult
            {
                Status = _senderResponseStatus,
                FilePath = _senderResponseFilePath
            };

            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            _context.Setup(x => x.CallActivityAsync<Guid>(nameof(GetGroupFunction), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(_syncJob.Group.GroupId);

            _context.Setup(x => x.GetInput<OrchestratorRequest>()).Returns(() => _mainRequest);

            _context.Setup(x => x.CallSubOrchestratorAsync<MembershipFileResult>(
                                                        nameof(OrganizationProcessorFunction),
                                                        It.IsAny<OrganizationProcessorRequest>(),
                                                        It.IsAny<TaskOptions>()))
                    .ReturnsAsync(() => _groupMembershipSenderResponse);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x == nameof(TelemetryTrackerFunction)), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallTelemetryTrackerFunctionAsync(request as TelemetryTrackerRequest);
                    });

            _context.Setup(x => x.CallActivityAsync(nameof(QueueMessageSenderFunction), It.IsAny<MembershipAggregatorHttpRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallQueueMessageSenderFunctionAsync(request as MembershipAggregatorHttpRequest);
                    });

            _schemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();

            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
                    })
                    .ReturnsAsync(() => _isValid);
        }

        [TestMethod]
        public async Task TestValidSqlMembershipQueryAsync()
        {
            var expectedResponse = new MembershipFileResult
            {
                Status = _senderResponseStatus,
                FilePath = _senderResponseFilePath
            };

            _context.Setup(x => x.CallActivityAsync<MembershipFileResult>(
                nameof(ChildEntitiesFilterFunction),
                It.IsAny<ChildEntitiesFilterRequest>(),
                It.IsAny<TaskOptions>()))
                .ReturnsAsync(expectedResponse);

            var orchestratorFunction = new OrchestratorFunction();
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallSubOrchestratorAsync<MembershipFileResult>(
                nameof(OrganizationProcessorFunction),
                It.Is<OrganizationProcessorRequest>(r => r.CurrentPart == 1 && r.TotalParts == 2),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        [DataRow("[{\"type\":\"SqlMembership\"}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\": null }]")]
        public async Task TestEmptySqlMembershipQueryAsync(string query)
        {
            _syncJob.Query = query;

            var orchestratorFunction = new OrchestratorFunction();
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.QueryNotValid && x.CurrentPart == 1 && x.TotalParts == 2),
                                                    It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task TestInvalidSqlMembershipQueryAsync()
        {
            _syncJob.Query = "some-invalid-query";

            var orchestratorFunction = new OrchestratorFunction();
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.QueryNotValid && x.CurrentPart == 1 && x.TotalParts == 2),
                                                    It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task TestInvalidSchemaAsync()
        {
            _syncJob.Query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":[1, 2]},\"filter\":\"Attribute = 'Value'\"}}]";

            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
                    })
                    .ReturnsAsync(() => false);

            var orchestratorFunction = new OrchestratorFunction();
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.SchemaError && x.CurrentPart == 1 && x.TotalParts == 2),
                                                    It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task TestInvalidFilePathFailureAsync()
        {
            _senderResponseFilePath = null;

            _groupMembershipSenderResponse = new MembershipFileResult
            {
                Status = SyncStatus.InProgress,
                FilePath = _senderResponseFilePath
            };

            var orchestratorFunction = new OrchestratorFunction();
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.FilePathNotValid && x.CurrentPart == 1 && x.TotalParts == 2),
                                                    It.IsAny<TaskOptions>()), Times.Once());
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
            _context.Setup(x => x.CallSubOrchestratorAsync<MembershipFileResult>(
                                                      nameof(OrganizationProcessorFunction),
                                                      It.IsAny<OrganizationProcessorRequest>(),
                                                      It.IsAny<TaskOptions>()))
                    .ThrowsAsync(new Exception("Internal .NET Framework Data Provider error 6"));

            var orchestratorFunction = new OrchestratorFunction();
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.Error && x.CurrentPart == 1 && x.TotalParts == 2),
                                                    It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task TestFailJobOnSqlExceptionAsync()
        {
            _context.Reset();

            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            _context.Setup(x => x.GetInput<OrchestratorRequest>()).Returns(() => _mainRequest);
            _context.Setup(x => x.CallActivityAsync<Guid>(nameof(GetGroupFunction), It.IsAny<GetGroupRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(_syncJob.Group.GroupId);
            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
                    })
                    .ReturnsAsync(() => _isValid);
            _context.Setup(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()));
            _context.Setup(x => x.CallActivityAsync(nameof(TelemetryTrackerFunction), It.IsAny<TelemetryTrackerRequest>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallTelemetryTrackerFunctionAsync(request as TelemetryTrackerRequest);
                    });

            _context.Setup(x => x.CallSubOrchestratorAsync<MembershipFileResult>(
                                                      nameof(OrganizationProcessorFunction),
                                                      It.IsAny<OrganizationProcessorRequest>(),
                                                      It.IsAny<TaskOptions>()))
                    .ThrowsAsync(MakeSqlException());

            var orchestratorFunction = new OrchestratorFunction();
            var sqlException = await Assert.ThrowsExceptionAsync<SqlException>(async () =>
            {
                await orchestratorFunction.RunOrchestratorAsync(_context.Object);
            });

            _context.Verify(x => x.CallSubOrchestratorAsync<MembershipFileResult>(
                                                      nameof(OrganizationProcessorFunction),
                                                      It.Is<OrganizationProcessorRequest>(r => r.CurrentPart == 1 && r.TotalParts == 2),
                                                      It.IsAny<TaskOptions>()), Times.Once());

            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.Error && x.CurrentPart == 1 && x.TotalParts == 2),
                                                    It.IsAny<TaskOptions>()), Times.Once());
        }

        public static SqlException MakeSqlException()
        {
            SqlException exception = null;
            try
            {
                SqlConnection conn = new SqlConnection(@"Data Source=.;Database=GUARANTEED_TO_FAIL;Connection Timeout=1;Encrypt=true;");
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
            var function = new TelemetryTrackerFunction(NullLogger<TelemetryTrackerFunction>.Instance, _telemetryClient);
            await function.TrackEventAsync(request);
        }

        private async Task CallSchemaValidatorFunctionAsync(SchemaValidatorRequest request)
        {
            var function = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _schemaProvider);
            await function.ValidateSchemasAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(MembershipAggregatorHttpRequest request)
        {
            var function = new QueueMessageSenderFunction(NullLogger<QueueMessageSenderFunction>.Instance, _serviceBusQueueRepository.Object);
            await function.SendMessageAsync(request);
        }
    }
}