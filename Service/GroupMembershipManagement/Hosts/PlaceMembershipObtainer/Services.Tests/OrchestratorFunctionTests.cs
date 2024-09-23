// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Tests.Services.Helpers;
using Moq;
using Hosts.PlaceMembershipObtainer;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Services;
using Repositories.Contracts.InjectConfig;
using Microsoft.DurableTask;
using Microsoft.Azure.Functions.Worker.Extensions.DurableTask.Http;

namespace Tests.Services
{
    [TestClass]
    public class OrchestratorFunctionTests
    {
        private int _profilesCount = 10;
        private List<GraphProfileInformation> _profiles;
        private Mock<IConfiguration> _configuration;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<TaskOrchestrationContext> _context;
        private Mock<Microsoft.Azure.WebJobs.ExecutionContext> _executionContext;
        private SyncJob _syncJob;
        private QuerySample _querySample;
        private OrchestratorRequest _orchestratorRequest;
        private SyncStatus _subOrchestratorResponseStatus;
        private SyncStatus _senderResponseStatus = SyncStatus.InProgress;
        private string _senderResponseFilePath = "file-path";
        private DurableHttpResponse _membershipAggregatorResponse;
        private TelemetryClient _telemetryClient;
        private PlaceMembershipObtainerService _placeMembershipObtainerService;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        SchemaProvider _schemaProvider;
        private bool _isValid = true;
        private int _usersToReturn;


        [TestInitialize]
        public void Setup()
        {
            _configuration = new Mock<IConfiguration>();
            _loggingRepository = new Mock<ILoggingRepository>();
            var mockGraphGroupRepository = new Mock<IGraphGroupRepository>();
            var mockBlobStorageRepository = new Mock<IBlobStorageRepository>();
            var mockSyncJob = new Mock<IDatabaseSyncJobsRepository>();
            var mockDryRunValue = new Mock<IDryRunValue>();
            _querySample = QuerySample.GenerateQuerySample("PlaceMembership");
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = _querySample.GetQuery(),
                Status = "InProgress",
                Period = 6
            };

            _orchestratorRequest = new OrchestratorRequest
            {
                CurrentPart = 1,
                TotalParts = _querySample.QueryParts.Count + 1,
                SyncJob = syncJob,
                IsDestinationPart = false
            };
            _usersToReturn = 10;
            mockDryRunValue.Setup(d => d.DryRunEnabled).Returns(true);
            _placeMembershipObtainerService = new PlaceMembershipObtainerService(
            mockGraphGroupRepository.Object,
            mockBlobStorageRepository.Object,
            mockSyncJob.Object,
            mockDryRunValue.Object);
            _context = new Mock<TaskOrchestrationContext>();
            _executionContext = new Mock<Microsoft.Azure.WebJobs.ExecutionContext>();
            _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();

            _context.Setup(x => x.GetInput<OrchestratorRequest>())
                                       .Returns(() => _orchestratorRequest);

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

            _membershipAggregatorResponse = new DurableHttpResponse(HttpStatusCode.NoContent);

            _subOrchestratorResponseStatus = SyncStatus.InProgress;

            _context.Setup(x => x.CallSubOrchestratorAsync<SubOrchestratorResponse>(It.IsAny<TaskName>(), It.IsAny<object>(), null))
                .ReturnsAsync(() =>
                {
                    var users = new List<AzureADUser>();
                    for (var i = 0; i < _usersToReturn; i++)
                    {
                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                    }

                    var response = new SubOrchestratorResponse { Users = users, Status = _subOrchestratorResponseStatus };
                    return response;
                });

            string _filePath = null;
            _context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<UsersSenderRequest>(), null))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        _filePath = await CallUsersSenderFunctionAsync(request as UsersSenderRequest);
                    })
                    .ReturnsAsync(() => _filePath);

            _context.Setup(r => r.CallActivityAsync<DurableHttpResponse>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>())).ReturnsAsync(_membershipAggregatorResponse);

            _context.Setup(x => x.CallActivityAsync(nameof(QueueMessageSenderFunction), It.IsAny<MembershipAggregatorHttpRequest>(), null))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallQueueMessageSenderFunctionAsync(request as MembershipAggregatorHttpRequest);
                    });

            _schemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();

            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), null))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
                    })
                    .ReturnsAsync(() => _isValid);
        }

        [TestMethod]
        public async Task TestValidPlaceMembershipQueryAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _placeMembershipObtainerService, _configuration.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);
            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                 It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"),
                                                 It.IsAny<VerbosityLevel>(),
                                                 It.IsAny<string>(),
                                                 It.IsAny<string>()
                                             ), Times.Once);
        }


        [TestMethod]
        public async Task TestInvalidSchemaAsync()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = "[{\"type\":\"PlaceMembership\",\"sources\":\"https://graph.microsoft.com/v1.0/users?$count=true&$filter=mail+eq+'USER2@M365x720024.onmicrosoft.com'\"}]",
                Status = "InProgress",
                Period = 6
            };

            _context.Setup(x => x.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), It.IsAny<SchemaValidatorRequest>(), null))
                .Callback<TaskName, object, TaskOptions>(async (taskName, request, options) =>
                {
                    await CallSchemaValidatorFunctionAsync(request as SchemaValidatorRequest);
                })
                .ReturnsAsync(() => false);

            var orchestratorFunction = new OrchestratorFunction(_loggingRepository.Object, _placeMembershipObtainerService, _configuration.Object);
            await orchestratorFunction.RunOrchestratorAsync(_context.Object);
            _context.Verify(x => x.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                    It.Is<JobStatusUpdaterRequest>(x => x.Status == SyncStatus.SchemaError), null), Times.Once());
        }

        private async Task<string> CallUsersSenderFunctionAsync(UsersSenderRequest request)
        {
            var function = new UsersSenderFunction(_loggingRepository.Object, _placeMembershipObtainerService);
            return await function.SendUsersAsync(request);
        }

        private async Task<bool> CallSchemaValidatorFunctionAsync(SchemaValidatorRequest request)
        {
            var function = new SchemaValidatorFunction(_loggingRepository.Object, _schemaProvider);
            return await function.ValidateSchemasAsync(request);
        }

        private async Task CallQueueMessageSenderFunctionAsync(MembershipAggregatorHttpRequest request)
        {
            var function = new QueueMessageSenderFunction(_loggingRepository.Object, _serviceBusQueueRepository.Object);
            await function.SendMessageAsync(request);
        }
    }
}
