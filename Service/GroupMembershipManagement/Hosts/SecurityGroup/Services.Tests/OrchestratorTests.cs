// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Hosts.SecurityGroup;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public class OrchestratorTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<IConfiguration> _configuration;
        private Mock<IMailRepository> _mailRepository;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ISyncJobRepository> _syncJobRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;
        private int _usersToReturn;
        private QuerySample _querySample;
        private OrchestratorRequest _orchestratorRequest;
        private SyncStatus _subOrchestratorResponseStatus;
        private SGMembershipCalculator _membershipCalculator;
        private DurableHttpResponse _membershipAgregatorResponse;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _configuration = new Mock<IConfiguration>();
            _mailRepository = new Mock<IMailRepository>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();

            _usersToReturn = 10;
            _querySample = QuerySample.GenerateQuerySample("SecurityGroup");

            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
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

            _membershipCalculator = new SGMembershipCalculator(
                                            _graphGroupRepository.Object,
                                            _blobStorageRepository.Object,
                                            _mailRepository.Object,
                                            _emailSenderRecipient.Object,
                                            _syncJobRepository.Object,
                                            _loggingRepository.Object,
                                            _dryRunValue.Object
                                            );

            _configuration.SetupGet(x => x[It.Is<string>(s => s == "membershipAggregatorUrl")]).Returns("http://app-config-url");
            _configuration.SetupGet(x => x[It.Is<string>(s => s == "membershipAggregatorFunctionKey")]).Returns("112233445566");

            _durableOrchestrationContext.Setup(x => x.GetInput<OrchestratorRequest>())
                                        .Returns(() => _orchestratorRequest);

            _durableOrchestrationContext.Setup(x => x.CurrentUtcDateTime).Returns(DateTime.UtcNow);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<JobStatusUpdaterRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                                        });

            AzureADGroup sourceGroup = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<AzureADGroup>(It.IsAny<string>(), It.IsAny<SourceGroupsReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            sourceGroup = await CallSourceGroupsReaderFunctionAsync(request as SourceGroupsReaderRequest);
                                        })
                                        .ReturnsAsync(() => sourceGroup);

            _subOrchestratorResponseStatus = SyncStatus.InProgress;
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<(List<AzureADUser> Users, SyncStatus Status)>(It.IsAny<string>(), It.IsAny<SecurityGroupRequest>()))
                                        .ReturnsAsync(() =>
                                        {
                                            var users = new List<AzureADUser>();
                                            for (var i = 0; i < _usersToReturn; i++)
                                            {
                                                users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                            }

                                            return (users, _subOrchestratorResponseStatus);
                                        });

            string _filePath = null;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<UsersSenderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _filePath = await CallUsersSenderFunctionAsync(request as UsersSenderRequest);
                                        })
                                        .ReturnsAsync(() => _filePath);

            _membershipAgregatorResponse = new DurableHttpResponse(System.Net.HttpStatusCode.NoContent);
            _durableOrchestrationContext.Setup(x => x.CallHttpAsync(It.IsAny<DurableHttpRequest>())).ReturnsAsync(() => _membershipAgregatorResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<EmailSenderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            await CallEmailSenderFunctionAsync(request as EmailSenderRequest);
                                        });
        }

        [TestMethod]
        public async Task TestInvalidCurrentPartAsync()
        {
            _orchestratorRequest.CurrentPart = 0;

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains("Found invalid value for CurrentPart or TotalParts")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.Error)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestOldSourceGroupsQueryFormatAsync()
        {
            var oldQueryFormat = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"0fab28de-4d33-4bb7-be1f-17e46cf75200\",\"ab7b7af7-fa0f-4874-bb03-15d435506595\"]}]";
            _orchestratorRequest.SyncJob.Query = oldQueryFormat;

            var orchestratorFunction = new OrchestratorFunction(
                                           _loggingRepository.Object,
                                           _membershipCalculator,
                                           _configuration.Object
                                           );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains($"Marking job as {SyncStatus.QueryNotValid}")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid)
                                            ), Times.Once);

            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Once);
        }

        [TestMethod]
        public async Task TestEmptySourceGroupsAsync()
        {
            _querySample.QueryParts.ForEach(x => x.SourceId = Guid.Empty);
            _orchestratorRequest.SyncJob.Query = _querySample.GetQuery();

            var orchestratorFunction = new OrchestratorFunction(
                                           _loggingRepository.Object,
                                           _membershipCalculator,
                                           _configuration.Object
                                           );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains($"Marking job as {SyncStatus.QueryNotValid}")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid)
                                            ), Times.Once);

            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Once);
        }

        [TestMethod]
        public async Task TestSecurityGroupNotFoundAsync()
        {
            _subOrchestratorResponseStatus = SyncStatus.SecurityGroupNotFound;

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.SecurityGroupNotFound)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestUnhandledExceptionAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<(List<AzureADUser> Users, SyncStatus Status)>(It.IsAny<string>(), It.IsAny<SecurityGroupRequest>()))
                                        .Throws<Exception>();

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object
                                            );

            await Assert.ThrowsExceptionAsync<Exception>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Caught unexpected exception")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.Error)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestGraphAPITimeoutExceptionAsync()
        {
            var error = new Error
            {
                Code = "timeout",
                Message = "The request timed out"
            };

            var exception = new Exception(error.Message);

            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<(List<AzureADUser> Users, SyncStatus Status)>(It.IsAny<string>(), It.IsAny<SecurityGroupRequest>()))
                                        .Throws(exception);

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Rescheduling job at")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            var currentUtcDate = _durableOrchestrationContext.Object.CurrentUtcDateTime;
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.Is<IEnumerable<SyncJob>>(x => x.All(y => y.StartDate == currentUtcDate.AddMinutes(30))),
                                                It.Is<SyncStatus>(s => s == SyncStatus.Idle)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestServiceExceptionAsync()
        {

            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = _querySample.GetQuery(),
                Status = "InProgress",
                Period = 6,
                LastSuccessfulRunTime = DateTime.UtcNow
            };

            _orchestratorRequest = new OrchestratorRequest
            {
                CurrentPart = 1,
                TotalParts = _querySample.QueryParts.Count + 1,
                SyncJob = syncJob,
                IsDestinationPart = false
            };

            _durableOrchestrationContext.Setup(x => x.GetInput<OrchestratorRequest>())
                                        .Returns(() => _orchestratorRequest);
            var error = new Error
            {
                Code = "502",
                Message = "Bad Gateway"
            };

            _durableOrchestrationContext.Setup(x => x.CallSubOrchestratorAsync<(List<AzureADUser> Users, SyncStatus Status)>(It.IsAny<string>(), It.IsAny<SecurityGroupRequest>()))
                                        .Throws(new ServiceException(error, null, HttpStatusCode.BadGateway));


            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Rescheduling job at")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            var currentUtcDate = _durableOrchestrationContext.Object.CurrentUtcDateTime;
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.Is<IEnumerable<SyncJob>>(x => x.All(y => y.StartDate == currentUtcDate.AddMinutes(30))),
                                                It.Is<SyncStatus>(s => s == SyncStatus.Idle)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestMembershipAggregatorCallFailsAsync()
        {
            _membershipAgregatorResponse = new DurableHttpResponse(System.Net.HttpStatusCode.ServiceUnavailable);

            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains($"Read {_usersToReturn} users from source groups")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _blobStorageRepository.Verify(x => x.UploadFileAsync(
                                                It.IsAny<string>(),
                                                It.IsAny<string>(),
                                                It.IsAny<Dictionary<string, string>>()
                                            ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.StartsWith("MembershipAggregator response")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.Is<SyncStatus>(s => s == SyncStatus.Error)
                                            ), Times.Once);
        }

        [TestMethod]
        public async Task TestValidPartRequestAsync()
        {
            var orchestratorFunction = new OrchestratorFunction(
                                            _loggingRepository.Object,
                                            _membershipCalculator,
                                            _configuration.Object
                                            );

            await orchestratorFunction.RunOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.Contains($"Read {_usersToReturn} users from source groups")),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _blobStorageRepository.Verify(x => x.UploadFileAsync(
                                                It.IsAny<string>(),
                                                It.IsAny<string>(),
                                                It.IsAny<Dictionary<string, string>>()
                                            ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message == $"{nameof(OrchestratorFunction)} function completed"),
                                                It.IsAny<VerbosityLevel>(),
                                                It.IsAny<string>(),
                                                It.IsAny<string>()
                                            ), Times.Once);

            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(
                                                It.IsAny<IEnumerable<SyncJob>>(),
                                                It.IsAny<SyncStatus>()
                                            ), Times.Never);


        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(_loggingRepository.Object, _membershipCalculator);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task<AzureADGroup> CallSourceGroupsReaderFunctionAsync(SourceGroupsReaderRequest request)
        {
            var function = new SourceGroupsReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetSourceGroupsAsync(request);
        }

        private async Task<string> CallUsersSenderFunctionAsync(UsersSenderRequest request)
        {
            var function = new UsersSenderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.SendUsersAsync(request);
        }

        private async Task CallEmailSenderFunctionAsync(EmailSenderRequest request)
        {
            var function = new EmailSenderFunction(_loggingRepository.Object, _membershipCalculator, _emailSenderRecipient.Object);
            await function.SendEmailAsync(request);
        }
    }
}
