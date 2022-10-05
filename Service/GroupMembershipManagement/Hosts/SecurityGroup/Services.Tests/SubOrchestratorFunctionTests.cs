// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Hosts.SecurityGroup;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        private const int Number_Of_Pages = 2;

        private Mock<IDryRunValue> _dryRunValue;
        private MockDeltaCachingConfig _deltaCachingConfig;
        private Mock<IMailRepository> _mailRepository;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ISyncJobRepository> _syncJobRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;

        private int _userCount;
        private bool _groupExists;
        private int _groupCount;
        private BlobResult _blobResult;
        private string _usersReaderNextPageUrl;
        private string _deltaUrl;
        private SecurityGroupRequest _securityGroupRequest;
        private SGMembershipCalculator _membershipCalculator;
        private GroupInformation _membersReaderResponse;
        private DeltaGroupInformation _usersReaderResponse;
        private DeltaGroupInformation _deltaUsersReaderResponse;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _deltaCachingConfig = new MockDeltaCachingConfig();
            _mailRepository = new Mock<IMailRepository>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();

            _userCount = 10;
            _blobResult = new BlobResult
            {
                BlobStatus = BlobStatus.Found,
                Content = new BinaryData(new GroupMembership
                {
                    SyncJobPartitionKey = "PK",
                    SyncJobRowKey = "RK",
                    MembershipObtainerDryRunEnabled = false,
                    RunId = Guid.Empty,
                    SourceMembers = Enumerable.Range(0, _userCount)
                                            .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                            .ToList(),
                    Destination = new AzureADGroup
                    {
                        ObjectId = Guid.Empty
                    }
                })
            };
            _groupExists = true;

            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = QuerySample.GenerateQuerySample("SecurityGroup").GetQuery(),
                Status = "InProgress",
                Period = 6
            };

            _securityGroupRequest = new SecurityGroupRequest
            {
                RunId = Guid.NewGuid(),
                SourceGroup = new Entities.AzureADGroup { ObjectId = Guid.NewGuid() },
                SyncJob = syncJob
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

            _durableOrchestrationContext.Setup(x => x.GetInput<SecurityGroupRequest>()).Returns(() => _securityGroupRequest);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _groupExists = await CallGroupValidatorFunctionAsync(request as GroupValidatorRequest);
                                        })
                                        .ReturnsAsync(() => _groupExists);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<DeltaUsersSenderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           await CallDeltaUsersSenderFunctionAsync(request as DeltaUsersSenderRequest);
                                       });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaGroupInformation>(It.IsAny<string>(),
                                                                                                                                    It.IsAny<DeltaUsersReaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _deltaUsersReaderResponse = await CallDeltaUsersReaderFunctionAsync(request as DeltaUsersReaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUsersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaGroupInformation>(It.IsAny<string>(),
                                                                                                                                    It.IsAny<SubsequentDeltaUsersReaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _deltaUsersReaderResponse = await CallSubsequentDeltaUsersReaderFunctionAsync(request as SubsequentDeltaUsersReaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUsersReaderResponse);


            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<GroupInformation>(It.IsAny<string>(),
                                                                                                                                     It.IsAny<MembersReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _membersReaderResponse = await CallMembersReaderFunctionAsync(request as MembersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _membersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<GroupInformation>(It.IsAny<string>(),
                                                                                                                         It.IsAny<SubsequentMembersReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _membersReaderResponse = await CallSubsequentMembersReaderFunctionAsync(request as SubsequentMembersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _membersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaGroupInformation>(It.IsAny<string>(),
                                                                                                                                    It.IsAny<UsersReaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _usersReaderResponse = await CallUsersReaderFunctionAsync(request as UsersReaderRequest);
                                       })
                                       .ReturnsAsync(() => _usersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaGroupInformation>(It.IsAny<string>(),
                                                                                                                         It.IsAny<SubsequentUsersReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _usersReaderResponse = await CallSubsequentUsersReaderFunctionAsync(request as SubsequentUsersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _usersReaderResponse);

            _graphGroupRepository.Setup(x => x.GroupExists(It.IsAny<Guid>())).ReturnsAsync(() => _groupExists);
            _graphGroupRepository.Setup(x => x.GetGroupsCountAsync(It.IsAny<Guid>())).ReturnsAsync(() => _groupCount);

            _blobStorageRepository.Setup(x => x.DownloadCacheFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);


            _graphGroupRepository.Setup(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };
                                     var usersPage = new Mock<IGroupTransitiveMembersCollectionWithReferencesPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, nonUserGraphObjects, _usersReaderNextPageUrl, usersPage.Object);
                                 });

            _graphGroupRepository.Setup(x => x.GetNextTransitiveMembersPageAsync(It.IsAny<string>(), It.IsAny<IGroupTransitiveMembersCollectionWithReferencesPage>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };
                                     var usersPage = new Mock<IGroupTransitiveMembersCollectionWithReferencesPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, nonUserGraphObjects, null, usersPage.Object);
                                 });

            _graphGroupRepository.Setup(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var usersPage = new Mock<IGroupDeltaCollectionPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, _usersReaderNextPageUrl, _deltaUrl, usersPage.Object);
                                 });

            _graphGroupRepository.Setup(x => x.GetNextUsersPageAsync(It.IsAny<string>(), It.IsAny<IGroupDeltaCollectionPage>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var usersPage = new Mock<IGroupDeltaCollectionPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, null, null, usersPage.Object);
                                 });

            _graphGroupRepository.Setup(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<string>()))
                                .ReturnsAsync(() =>
                                {
                                    var users = new List<AzureADUser>();
                                    var usersPage = new Mock<IGroupDeltaCollectionPage>();

                                    for (var i = 0; i < _userCount; i++)
                                    {
                                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                    }

                                    return (users, users, _usersReaderNextPageUrl, _deltaUrl, usersPage.Object);
                                });

            _graphGroupRepository.Setup(x => x.GetNextDeltaUsersPageAsync(It.IsAny<string>(), It.IsAny<IGroupDeltaCollectionPage>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var usersPage = new Mock<IGroupDeltaCollectionPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, users, null, _deltaUrl, usersPage.Object);
                                 });
        }

        [TestMethod]
        public async Task ProcessDeltaSinglePageRequestTestAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount} users")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(_userCount, Users.Count);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task VerifyCountTestAsync()
        {
            _groupCount = 0;
            _userCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetUserCountRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _userCount = await CallUsersReaderFunctionAsync(request as GetUserCountRequest);
                                       })
                                       .ReturnsAsync(() => _userCount);
            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<string>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()), Times.Once);
            Assert.AreEqual(_userCount, Users.Count);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount} users")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task ProcessDeltaLinkSinglePageRequestTestAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<string>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount} users")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(_userCount, Users.Count);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task ProcessTMSinglePageRequestTestAsync()
        {
            _groupCount = 2;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount} users")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(_userCount, Users.Count);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task ProcessDeltaMultiplePageRequestTestAsync()
        {
            _usersReaderNextPageUrl = "http://next-page-url";
            _deltaUrl = "http://delta-url";
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                      .Callback<string, object>(async (name, request) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount * Number_Of_Pages} users")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(_userCount * Number_Of_Pages, Users.Count);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task ProcessDeltaLinkMultiplePageRequestTestAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                      .Callback<string, object>(async (name, request) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<string>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task ProcessTMMultiplePageRequestTestAsync()
        {
            _usersReaderNextPageUrl = "http://next-page-url";
            _deltaUrl = "http://delta-url";
            _groupCount = 2;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                      .Callback<string, object>(async (name, request) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount * Number_Of_Pages} users")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(_userCount * Number_Of_Pages, Users.Count);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task ProcessDeltaMultiplePagesRequestTestAsync()
        {
            _usersReaderNextPageUrl = "http://next-page-url";
            _deltaUrl = "http://delta-url";
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                      .Callback<string, object>(async (name, request) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _userCount = 10;
            _graphGroupRepository.Setup(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()))
                                .ReturnsAsync(() =>
                                {
                                    var users = new List<AzureADUser>();
                                    var usersPage = new Mock<IGroupDeltaCollectionPage>();

                                    for (var i = 0; i < _userCount; i++)
                                    {
                                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                    }

                                    return (users, _usersReaderNextPageUrl, _deltaUrl, usersPage.Object);
                                });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);
            _graphGroupRepository.Verify(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetNextUsersPageAsync(It.IsAny<string>(), It.IsAny<IGroupDeltaCollectionPage>()), Times.Once);
        }

        [TestMethod]
        public async Task ProcessDeltaLinkMultiplePagesRequestTestAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                      .Callback<string, object>(async (name, request) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _usersReaderNextPageUrl = "http://next-page-url";
            _graphGroupRepository.Setup(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<string>()))
                                .ReturnsAsync(() =>
                                {
                                    var users = new List<AzureADUser>();
                                    var usersPage = new Mock<IGroupDeltaCollectionPage>();

                                    for (var i = 0; i < _userCount; i++)
                                    {
                                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                    }

                                    return (users, users, _usersReaderNextPageUrl, _deltaUrl, usersPage.Object);
                                });

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);


            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);
            _graphGroupRepository.Verify(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<string>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetNextDeltaUsersPageAsync(It.IsAny<string>(), It.IsAny<IGroupDeltaCollectionPage>()), Times.Once);
        }

        [TestMethod]
        public async Task ProcessTMMultiplePagesRequestTestAsync()
        {
            _usersReaderNextPageUrl = "http://next-page-url";
            _deltaUrl = "http://delta-url";
            _groupCount = 2;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<string>(), It.IsAny<GetTransitiveGroupCountRequest>()))
                                      .Callback<string, object>(async (name, request) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _userCount = 10;
            _graphGroupRepository.Setup(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };
                                     var usersPage = new Mock<IGroupTransitiveMembersCollectionWithReferencesPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, nonUserGraphObjects, _usersReaderNextPageUrl, usersPage.Object);
                                 });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);
            _graphGroupRepository.Verify(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetNextTransitiveMembersPageAsync(It.IsAny<string>(), It.IsAny<IGroupTransitiveMembersCollectionWithReferencesPage>()), Times.Once);
        }

        [TestMethod]
        public async Task GroupDoesNotExistTestAsync()
        {
            _groupExists = false;

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Once);

            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Never);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} doesn't exist")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Once);

            Assert.IsNull(Users);
            Assert.AreEqual(SyncStatus.SecurityGroupNotFound, Status);
        }

        private async Task<bool> CallGroupValidatorFunctionAsync(GroupValidatorRequest request)
        {
            var function = new GroupValidatorFunction(_loggingRepository.Object, _membershipCalculator, _emailSenderRecipient.Object);
            return await function.ValidateGroupAsync(request);
        }

        private async Task CallDeltaUsersSenderFunctionAsync(DeltaUsersSenderRequest request)
        {
            var function = new DeltaUsersSenderFunction(_loggingRepository.Object, _membershipCalculator);
            await function.SendUsersAsync(request);
        }

        private async Task<string> CallFileDownloaderFunctionAsync(FileDownloaderRequest request)
        {
            var function = new FileDownloaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            return await function.DownloadFileAsync(request);
        }

        private async Task<int> CallGroupsReaderFunctionAsync(GetTransitiveGroupCountRequest request)
        {
            var function = new GetTransitiveGroupCountFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetGroupsAsync(request);
        }

        private async Task<int> CallUsersReaderFunctionAsync(GetUserCountRequest request)
        {
            var function = new GetUserCountFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetUserCountAsync(request);
        }

        private async Task<GroupInformation> CallMembersReaderFunctionAsync(MembersReaderRequest request)
        {
            var function = new MembersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetMembersAsync(request);
        }

        private async Task<GroupInformation> CallSubsequentMembersReaderFunctionAsync(SubsequentMembersReaderRequest request)
        {
            var function = new SubsequentMembersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetMembersAsync(request);
        }

        private async Task<DeltaGroupInformation> CallUsersReaderFunctionAsync(UsersReaderRequest request)
        {
            var function = new UsersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetUsersAsync(request);
        }

        private async Task<DeltaGroupInformation> CallSubsequentUsersReaderFunctionAsync(SubsequentUsersReaderRequest request)
        {
            var function = new SubsequentUsersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetUsersAsync(request);
        }

        private async Task<DeltaGroupInformation> CallDeltaUsersReaderFunctionAsync(DeltaUsersReaderRequest request)
        {
            var function = new DeltaUsersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetDeltaUsersAsync(request);
        }

        private async Task<DeltaGroupInformation> CallSubsequentDeltaUsersReaderFunctionAsync(SubsequentDeltaUsersReaderRequest request)
        {
            var function = new SubsequentDeltaUsersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetDeltaUsersAsync(request);
        }
    }
}

