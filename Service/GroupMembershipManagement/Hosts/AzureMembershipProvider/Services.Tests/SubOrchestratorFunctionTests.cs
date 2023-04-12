// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.ServiceBus;
using Hosts.AzureMembershipProvider;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
//using Repositories.Mocks;
using Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Tests.Services
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        private const int Number_Of_Pages = 2;

        private Mock<IDryRunValue> _dryRunValue;
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
        private string _nextPageUrl;
        private SubOrchestratorRequest _subOrchestratorRequest;
        private AzureMembershipProviderService _service;
        private UserInformation _usersReaderResponse;
        private PlaceInformation _placesReaderResponse;
        private PlaceInformation _workSpacesReaderResponse;
        private IGraphServicePlacesCollectionPage _roomsFromPage;
        private IGraphServicePlacesCollectionPage _workSpacesFromPage;
        private IGraphServiceUsersCollectionPage _usersFromPage;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _mailRepository = new Mock<IMailRepository>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();

            _userCount = 10;

            var content = new GroupMembership
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
            };

            _blobResult = new BlobResult
            {
                BlobStatus = BlobStatus.Found,
                Content = JsonConvert.SerializeObject(content)
            };
            _groupExists = true;

            _service = new AzureMembershipProviderService(
                                            _graphGroupRepository.Object,
                                            _blobStorageRepository.Object,
                                            _mailRepository.Object,
                                            _emailSenderRecipient.Object,
                                            _syncJobRepository.Object,
                                            _loggingRepository.Object,
                                            _dryRunValue.Object
                                            );

            _durableOrchestrationContext.Setup(x => x.GetInput<SubOrchestratorRequest>()).Returns(() => _subOrchestratorRequest);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<PlaceInformation>(It.IsAny<string>(), It.IsAny<RoomsReaderRequest>()))
                                         .Callback<string, object>(async (name, request) =>
                                         {
                                             _placesReaderResponse = await CallRoomsReaderFunctionAsync(request as RoomsReaderRequest);
                                         })
                                         .ReturnsAsync(() => _placesReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<PlaceInformation>(It.IsAny<string>(), It.IsAny<WorkSpacesReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _workSpacesReaderResponse = await CallWorkSpacesReaderFunctionAsync(request as WorkSpacesReaderRequest);
                                        })
                                        .ReturnsAsync(() => _workSpacesReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<UserInformation>(It.IsAny<string>(), It.IsAny<UsersReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _usersReaderResponse = await CallUsersReaderFunctionAsync(request as UsersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _usersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<UserInformation>(It.IsAny<string>(), It.IsAny<SubsequentUsersReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _usersReaderResponse = await CallSubsequentUsersReaderFunctionAsync(request as SubsequentUsersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _usersReaderResponse);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);
        }

        [TestMethod]
        public async Task ProcessRoomsRequestTestAsync()
        {
            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = "[{ 'type': 'AzureMembershipProvider', 'source': 'https://graph.microsoft.com/v1.0/Rooms/microsoft.graph.room' }]",
                Status = "InProgress",
                Period = 6
            };


            _subOrchestratorRequest = new SubOrchestratorRequest
            {
                RunId = Guid.NewGuid(),
                Url = "https://graph.microsoft.com/v1.0/places/microsoft.graph.room",
                SyncJob = syncJob
            };

            var users = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                users.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid()
                });
            }

            _placesReaderResponse = new PlaceInformation
            {
                Users = users,
                NextPageUrl = null
            };

            _graphGroupRepository.Setup(x => x.GetRoomsPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, null);
                                 });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetRoomsPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);

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
        public async Task ProcessWorkSpacesRequestTestAsync()
        {
            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = "[{ 'type': 'AzureMembershipProvider', 'source': 'https://graph.microsoft.com/v1.0/Rooms/microsoft.graph.workspace' }]",
                Status = "InProgress",
                Period = 6
            };


            _subOrchestratorRequest = new SubOrchestratorRequest
            {
                RunId = Guid.NewGuid(),
                Url = "https://graph.microsoft.com/v1.0/places/microsoft.graph.workspace",
                SyncJob = syncJob
            };

            var users = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                users.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid()
                });
            }

            _workSpacesReaderResponse = new PlaceInformation
            {
                Users = users,
                NextPageUrl = null
            };

            _graphGroupRepository.Setup(x => x.GetWorkSpacesPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, null);
                                 });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetWorkSpacesPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);

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
        public async Task ProcessUsersRequestTestAsync()
        {
            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = "[{ 'type': 'AzureMembershipProvider', 'source': 'https://graph.microsoft.com/v1.0/users?$filter=endsWith(mail,'microsoft.com')&$orderBy=displayName&$select=id,displayName,mail' }]",
                Status = "InProgress",
                Period = 6
            };

            _subOrchestratorRequest = new SubOrchestratorRequest
            {
                RunId = Guid.NewGuid(),
                Url = "https://graph.microsoft.com/v1.0/users?$count=true&$search=\"displayName:room\"&$filter=endsWith(mail,'microsoft.com')&$orderBy=displayName&$select=id,displayName,mail",
                SyncJob = syncJob
            };

            var users = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                users.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid()
                });
            }

            _usersReaderResponse = new UserInformation
            {
                Users = users
            };

            _graphGroupRepository.Setup(x => x.GetFirstMembersPageAsync(It.IsAny<string>()))
                                  .ReturnsAsync(() =>
                                  {
                                      var users = new List<AzureADUser>();
                                      var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };

                                      for (var i = 0; i < _userCount; i++)
                                      {
                                          users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                      }

                                      return (users, nonUserGraphObjects, null);
                                  });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetFirstMembersPageAsync(It.IsAny<string>()), Times.Once);

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
        public async Task ProcessSubsequentUsersRequestTestAsync()
        {
            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = "[{ 'type': 'AzureMembershipProvider', 'source': 'https://graph.microsoft.com/v1.0/users?$filter=endsWith(mail,'microsoft.com')&$orderBy=displayName&$select=id,displayName,mail' }]",
                Status = "InProgress",
                Period = 6
            };

            _subOrchestratorRequest = new SubOrchestratorRequest
            {
                RunId = Guid.NewGuid(),
                Url = "https://graph.microsoft.com/v1.0/users?$count=true&$search=\"displayName:room\"&$filter=endsWith(mail,'microsoft.com')&$orderBy=displayName&$select=id,displayName,mail",
                SyncJob = syncJob
            };

            var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };
            var users = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                users.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid()
                });
            }

            _usersReaderResponse = new UserInformation
            {
                Users = users,
                NonUserGraphObjects = nonUserGraphObjects,
                NextPageUrl = "next-page-url"
            };

            _graphGroupRepository.Setup(x => x.GetFirstMembersPageAsync(It.IsAny<string>()))
                                  .ReturnsAsync(() =>
                                  {
                                      var users = new List<AzureADUser>();
                                      var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };

                                      for (var i = 0; i < _userCount; i++)
                                      {
                                          users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                      }

                                      return (users, nonUserGraphObjects, "next-page-url");
                                  });

            _graphGroupRepository.Setup(x => x.GetNextMembersPageAsync(It.IsAny<string>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };
                                     return (users, nonUserGraphObjects, null);
                                 });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_loggingRepository.Object, telemetryClient);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GetFirstMembersPageAsync(It.IsAny<string>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetNextMembersPageAsync(It.IsAny<string>()), Times.Once);

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

        private async Task<PlaceInformation> CallRoomsReaderFunctionAsync(RoomsReaderRequest request)
        {
            var function = new RoomsReaderFunction(_loggingRepository.Object, _service);
            return await function.GetRoomsAsync(request);
        }

        private async Task<PlaceInformation> CallWorkSpacesReaderFunctionAsync(WorkSpacesReaderRequest request)
        {
            var function = new WorkSpacesReaderFunction(_loggingRepository.Object, _service);
            return await function.GetWorkSpacesAsync(request);
        }

        private async Task<UserInformation> CallUsersReaderFunctionAsync(UsersReaderRequest request)
        {
            var function = new UsersReaderFunction(_loggingRepository.Object, _service);
            return await function.GetUsersAsync(request);
        }

        private async Task<UserInformation> CallSubsequentUsersReaderFunctionAsync(SubsequentUsersReaderRequest request)
        {
            var function = new SubsequentUsersReaderFunction(_loggingRepository.Object, _service);
            return await function.GetUsersAsync(request);
        }
    }
}

