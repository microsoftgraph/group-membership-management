// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository;
        private Mock<IDatabaseGroupsRepository> _groupsRepository;
        private Mock<IDatabaseChannelsRepository> _channelsRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<TaskOrchestrationContext> _durableOrchestrationContext;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        private Mock<IDatabaseDestinationAttributesRepository> _destinationAttributesRepository;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository;

        private int _userCount;
        private bool _groupExists;
        private int _groupCount;
        private BlobResult _blobResult;
        private BlobResult _extraUserBlobResult;
        private string _usersReaderNextPageUrl;
        private string _deltaUrl;
        private string _filePath;
        private GroupMembershipRequest _groupMembershipRequest;
        private SGMembershipCalculator _membershipCalculator;
        private string _membersReaderResponse;
        private DeltaUrls _deltaUserReaderResponse;
        private DeltaUrls _deltaLinkUserReaderResponse;
        private ProcessCachedAndDeltaUsersResponse _processCachedAndDeltaUsersResponse;
        private TelemetryClient _telemetryClient;
        private GroupMembershipFileResult _membershipFileResult;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _deltaCachingConfig = new MockDeltaCachingConfig();
            _mailRepository = new Mock<IMailRepository>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _groupsRepository = new Mock<IDatabaseGroupsRepository>();
            _channelsRepository = new Mock<IDatabaseChannelsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<TaskOrchestrationContext>();
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _destinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();

            _deltaUserReaderResponse = new DeltaUrls
            {
                DeltaUrl = "delta-url"
            };
            _deltaLinkUserReaderResponse = new DeltaUrls
            {
                DeltaUrl = "delta-url"
            };
            _processCachedAndDeltaUsersResponse = new ProcessCachedAndDeltaUsersResponse
            {
                MembershipFilePath = "file-path",
                CacheMatchesGroupCount = true,
            };

            _userCount = 10;
            _filePath = "file-path";
            var content = new GroupMembership
            {
                SyncJobId = Guid.Empty,
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

                Content = JsonSerializer.Serialize(content)
            };

            var extraUserContent = new GroupMembership
            {
                SyncJobId = Guid.Empty,
                MembershipObtainerDryRunEnabled = false,
                RunId = Guid.Empty,
                SourceMembers = Enumerable.Range(0, _userCount + 1)
                                            .Select(x => new AzureADUser { ObjectId = Guid.NewGuid() })
                                            .ToList(),
                Destination = new AzureADGroup
                {
                    ObjectId = Guid.Empty
                }
            };
            _extraUserBlobResult = new BlobResult
            {
                BlobStatus = BlobStatus.Found,

                Content = JsonSerializer.Serialize(extraUserContent)
            };
            _groupExists = true;

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                Query = QuerySample.GenerateQuerySample("GroupMembership").GetQuery(),
                Status = "InProgress",
                Period = 6
            };

            _groupMembershipRequest = new GroupMembershipRequest
            {
                RunId = Guid.NewGuid(),
                GroupId = Guid.NewGuid(),
                SourceGroup = new AzureADGroup { ObjectId = Guid.NewGuid() },
                SyncJob = syncJob
            };

            _membershipCalculator = new SGMembershipCalculator(
                                            _graphGroupRepository.Object,
                                            _blobStorageRepository.Object,
                                            _syncJobRepository.Object,
                                            _groupsRepository.Object,
                                            _channelsRepository.Object,
                                            _serviceBusQueueRepository.Object,
                                            _destinationAttributesRepository.Object,
                                            _loggingRepository.Object,
                                            _dryRunValue.Object
                                            );

            _durableOrchestrationContext.Setup(x => x.GetInput<GroupMembershipRequest>()).Returns(() => _groupMembershipRequest);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                       {
                                           await CallJobStatusUpdaterFunctionAsync(request as JobStatusUpdaterRequest);
                                       });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                        {
                                            _groupExists = await CallGroupValidatorFunctionAsync(request as GroupValidatorRequest);
                                        })
                                        .ReturnsAsync(() => _groupExists);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<DeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                       {
                                           await CallDeltaUsersSenderFunctionAsync(request as DeltaUsersSenderRequest);
                                       });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<FileDeleterRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                      {
                                          await CallFileDeleterFunctionAsync(request as FileDeleterRequest);
                                      });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<MembersReaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                        {
                                            _membersReaderResponse = await CallMembersReaderFunctionAsync(request as MembersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _membersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<SubsequentMembersReaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                        {
                                            _membersReaderResponse = await CallSubsequentMembersReaderFunctionAsync(request as SubsequentMembersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _membersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaUrls>(It.IsAny<TaskName>(), It.IsAny<DeltaUserReaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                       {
                                           _deltaUserReaderResponse = await CallDeltaUserReaderFunctionAsync(request as DeltaUserReaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUserReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaUrls>(It.IsAny<TaskName>(), It.IsAny<SubsequentDeltaUserReaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                        {
                                            _deltaUserReaderResponse = await CallSubsequentDeltaUserReaderFunctionAsync(request as SubsequentDeltaUserReaderRequest);
                                        })
                                        .ReturnsAsync(() => _deltaUserReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeltaLinkUploaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                       {
                                           await CallDeltaLinkUploaderFunctionAsync(request as DeltaLinkUploaderRequest);
                                       });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<GroupMembershipFileResult>(It.IsAny<TaskName>(), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                       {
                                           _membershipFileResult = await CallTransitiveAndDeltaUsersSenderFunctionAsync(request as TransitiveAndDeltaUsersSenderRequest);
                                       })
                                       .ReturnsAsync(() => new GroupMembershipFileResult { FilePath = _filePath, MemberCount = _userCount });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaUrls>(It.IsAny<TaskName>(), It.IsAny<DeltaLinkUserReaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                       {
                                           _deltaLinkUserReaderResponse = await CallDeltaLinkUserReaderFunctionAsync(request as DeltaLinkUserReaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaLinkUserReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaUrls>(It.IsAny<TaskName>(), It.IsAny<SubsequentDeltaLinkUserReaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                       {
                                           _deltaLinkUserReaderResponse = await CallSubsequentDeltaLinkUserReaderFunctionAsync(request as SubsequentDeltaLinkUserReaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaLinkUserReaderResponse);


            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                        {
                                            await CallDeleteBlobFunctionAsync(request as DeleteBlobRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<CacheUploaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                        {
                                            await CallCacheUploaderFunctionAsync(request as CacheUploaderRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<ProcessCachedAndDeltaUsersResponse>(It.IsAny<TaskName>(), It.IsAny<ProcessCachedAndDeltaUsersRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, request, input) =>
                                        {
                                            await CallProcessCachedAndDeltaUsersFunctionAsync(request as ProcessCachedAndDeltaUsersRequest);
                                        })
                                        .ReturnsAsync(() => _processCachedAndDeltaUsersResponse);

            _graphGroupRepository.Setup(x => x.GroupExists(It.IsAny<Guid>())).ReturnsAsync(() => _groupExists);
            _graphGroupRepository.Setup(x => x.GetGroupsCountAsync(It.IsAny<Guid>())).ReturnsAsync(() => _groupCount);
            _graphGroupRepository.Setup(x => x.GetUsersCountAsync(It.IsAny<Guid>())).ReturnsAsync(() => _userCount);

            _blobStorageRepository.Setup(x => x.DownloadCacheFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);
            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);
            _blobStorageRepository.Setup(x => x.ReadBlobsAsync(It.IsAny<string>())).ReturnsAsync(() =>
            {
                var users = new List<AzureADUser>();
                for (var i = 0; i < _userCount; i++)
                {
                    users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                }

                return users;
            });


            _graphGroupRepository.Setup(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, nonUserGraphObjects, _usersReaderNextPageUrl);
                                 });

            _graphGroupRepository.Setup(x => x.GetNextTransitiveMembersPageAsync(It.IsAny<Guid>(),It.IsAny<string>()))
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

            _graphGroupRepository.Setup(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, _usersReaderNextPageUrl, _deltaUrl);
                                 });

            _graphGroupRepository.Setup(x => x.GetNextDeltaUsersPagesAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, null, null);
                                 });

            _graphGroupRepository.Setup(x => x.GetFirstDeltaLinkUsersPageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()))
                                .ReturnsAsync(() =>
                                {
                                    var users = new List<AzureADUser>();

                                    for (var i = 0; i < _userCount; i++)
                                    {
                                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                    }

                                    return (users, users, _usersReaderNextPageUrl, _deltaUrl);
                                });

            _graphGroupRepository.Setup(x => x.GetNextDeltaLinkUsersPagesAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, users, null, _deltaUrl);
                                 });
        }

        [TestMethod]
        public async Task ProcessDeltaSinglePageRequestTestAsync()
        {
            string content = null;
            _groupCount = 0;
            _deltaUserReaderResponse.NextPageUrl = null;
            _deltaUserReaderResponse.DeltaUrl = "delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           var fileDownloaderRequest = request as FileDownloaderRequest;

                                           if (fileDownloaderRequest.FilePath.StartsWith("cache/delta_"))
                                               content = string.Empty;
                                           else
                                               content = await CallFileDownloaderFunctionAsync(fileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => content);

            _graphGroupRepository.Setup(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, null, "delta-url");
                                 });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<CacheUploaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           await CallCacheUploaderFunctionAsync(request as CacheUploaderRequest);
                                       });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeltaLinkUploaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           await CallDeltaLinkUploaderFunctionAsync(request as DeltaLinkUploaderRequest);
                                       });
            _graphGroupRepository.Setup(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                                .ReturnsAsync(() =>
                                {
                                    var users = new List<AzureADUser>();

                                    for (var i = 0; i < _userCount; i++)
                                    {
                                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                    }

                                    return (users, null, "http://delta-url");
                                });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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
            _graphGroupRepository.Verify(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<GroupMembershipFileResult>(nameof(TransitiveAndDeltaUsersSenderFunction), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(DeleteBlobFunction), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }

        [TestMethod]
        public async Task ProcessDeltaSinglePageRequestWithExtraUserTestAsync()
        {
            string content = null;
            _groupCount = 0;
            _blobStorageRepository.Setup(x => x.DownloadCacheFileAsync(It.IsAny<string>())).ReturnsAsync(() => _extraUserBlobResult);
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetUserCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _userCount = await CallUsersReaderFunctionAsync(request as GetUserCountRequest);
                                       })
                                       .ReturnsAsync(() => _userCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaUrls>(It.IsAny<TaskName>(), It.IsAny<DeltaUserReaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _deltaUserReaderResponse = await CallDeltaUserReaderFunctionAsync(request as DeltaUserReaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUserReaderResponse);


            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           var fileDownloaderRequest = request as FileDownloaderRequest;

                                           if (fileDownloaderRequest.FilePath.StartsWith("cache/delta_"))
                                               content = await CallFileDownloaderFunctionAsync(fileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => content);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDeleterRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                      {
                                          var fileDeleterRequest = request as FileDeleterRequest;

                                           await CallFileDeleterFunctionAsync(fileDeleterRequest);
                                       });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(nameof(CacheUploaderFunction), It.IsAny<CacheUploaderRequest>(), It.IsAny<TaskOptions>()))
                   .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                   {
                       await CallCacheUploaderFunctionAsync(request as CacheUploaderRequest);
                   });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }

        [TestMethod]
        public async Task VerifyCountTestAsync()
        {
            _groupCount = 0;
            _userCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetUserCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _userCount = await CallUsersReaderFunctionAsync(request as GetUserCountRequest);
                                       })
                                       .ReturnsAsync(() => _userCount);
            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }

        [TestMethod]
        public async Task ProcessDeltaLinkSinglePageRequestTestAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);


            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetUserCountRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                      {
                                          _userCount = await CallUsersReaderFunctionAsync(request as GetUserCountRequest);
                                      })
                                      .ReturnsAsync(() => _userCount);

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
            Assert.AreEqual(_filePath, response.FilePath);
        }

        [TestMethod]
        public async Task HandleDeltaLinkExceptionAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);
            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaUrls>(It.IsAny<TaskName>(), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _membershipFileResult = await CallTransitiveAndDeltaUsersSenderFunctionAsync(request as TransitiveAndDeltaUsersSenderRequest);
                                       })
                                       .Throws<KeyNotFoundException>();
            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }



        [TestMethod]
        public async Task ProcessTMSinglePageRequestTestAsync()
        {
            _groupCount = 2;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                       })
                                       .ReturnsAsync(() => _groupCount);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(MembersReaderFunction), It.IsAny<MembersReaderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<GroupMembershipFileResult>(nameof(TransitiveAndDeltaUsersSenderFunction), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(DeleteBlobFunction), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }

        [TestMethod]
        public async Task ProcessDeltaMultiplePageRequestTestAsync()
        {
            string content = null;

            _usersReaderNextPageUrl = "http://next-page-url";
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                           .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                           {
                               var fileDownloaderRequest = request as FileDownloaderRequest;

                                           if (fileDownloaderRequest.FilePath.StartsWith("cache/delta_"))
                                               content = string.Empty;
                                           else
                                               content = await CallFileDownloaderFunctionAsync(fileDownloaderRequest);
                                       })
                           .ReturnsAsync(() => content);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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
            _graphGroupRepository.Verify(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Once);

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }

        [TestMethod]
        public async Task ProcessDeltaLinkMultiplePageRequestTestAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }

        [TestMethod]
        public async Task ProcessTMMultiplePageRequestTestAsync()
        {
            _usersReaderNextPageUrl = "http://next-page-url";
            _deltaUrl = "http://delta-url";
            _groupCount = 2;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists.")),
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

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(MembersReaderFunction), It.IsAny<MembersReaderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(SubsequentMembersReaderFunction), It.IsAny<SubsequentMembersReaderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<GroupMembershipFileResult>(nameof(TransitiveAndDeltaUsersSenderFunction), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(DeleteBlobFunction), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
        }

        [TestMethod]
        public async Task ProcessDeltaMultiplePagesRequestTestAsync()
        {
            string content = null;

            _usersReaderNextPageUrl = "http://next-page-url";
            _deltaUrl = "http://delta-url";
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
               .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
               {
                   var fileDownloaderRequest = request as FileDownloaderRequest;

                                           if (fileDownloaderRequest.FilePath.StartsWith("cache/delta_"))
                                               content = string.Empty;
                                           else
                                               content = await CallFileDownloaderFunctionAsync(fileDownloaderRequest);
                                       })
               .ReturnsAsync(() => content);

            _userCount = 10;
            _graphGroupRepository.Setup(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                                .ReturnsAsync(() =>
                                {
                                    var users = new List<AzureADUser>();

                                    for (var i = 0; i < _userCount; i++)
                                    {
                                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                    }

                                    return (users, _usersReaderNextPageUrl, _deltaUrl);
                                });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var compressedResponse = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _graphGroupRepository.Verify(x => x.GetFirstDeltaUsersPageAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetNextDeltaUsersPagesAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<GroupMembershipFileResult>(nameof(TransitiveAndDeltaUsersSenderFunction), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(DeleteBlobFunction), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
        }

        [TestMethod]
        public async Task ProcessDeltaLinkMultiplePagesRequestTestAsync()
        {
            _groupCount = 0;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                      {
                                          _groupCount = await CallGroupsReaderFunctionAsync(request as GetTransitiveGroupCountRequest);
                                      })
                                      .ReturnsAsync(() => _groupCount);

            _usersReaderNextPageUrl = "http://next-page-url";
            _graphGroupRepository.Setup(x => x.GetFirstDeltaLinkUsersPageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()))
                                .ReturnsAsync(() =>
                                {
                                    var users = new List<AzureADUser>();

                                    for (var i = 0; i < _userCount; i++)
                                    {
                                        users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                    }

                                    return (users, users, _usersReaderNextPageUrl, _deltaUrl);
                                });

            _deltaUrl = "http://delta-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _deltaUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _deltaUrl);


            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var compressedResponse = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

        }

        [TestMethod]
        public async Task ProcessTMMultiplePagesRequestTestAsync()
        {
            _usersReaderNextPageUrl = "http://next-page-url";
            _deltaUrl = "http://delta-url";
            _groupCount = 2;
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                      .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
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

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, nonUserGraphObjects, _usersReaderNextPageUrl);
                                 });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var compressedResponse = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _graphGroupRepository.Verify(x => x.GetFirstTransitiveMembersPageAsync(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetNextTransitiveMembersPageAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(MembersReaderFunction), It.IsAny<MembersReaderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(SubsequentMembersReaderFunction), It.IsAny<SubsequentMembersReaderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<GroupMembershipFileResult>(nameof(TransitiveAndDeltaUsersSenderFunction), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));
            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(nameof(DeleteBlobFunction), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(1));

        }

        [TestMethod]
        public async Task GroupDoesNotExistTestAsync()
        {
            _groupExists = false;

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Once);

            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} exists")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Never);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_groupMembershipRequest.SourceGroup.ObjectId} doesn't exist")),
                                It.IsAny<VerbosityLevel>(),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Once);

            Assert.AreEqual(SyncStatus.SecurityGroupNotFound, response.Status);
        }

        [TestMethod]
        public async Task TestTransientExceptionAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                        .Throws<HttpRequestException>();

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(async () => await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Caught HttpRequestException")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);
        }

        [TestMethod]
        public async Task TestOtherExceptionAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                        .Throws<OutOfMemoryException>();

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _loggingRepository.Object, telemetryClient);

            await Assert.ThrowsExceptionAsync<OutOfMemoryException>(async () => await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object));

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Caught Exception")),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);
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

        private async Task CallFileDeleterFunctionAsync(FileDeleterRequest request)
        {
            var function = new FileDeleterFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await function.DeleteFileAsync(request);
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

        private async Task<string> CallMembersReaderFunctionAsync(MembersReaderRequest request)
        {
            var function = new MembersReaderFunction(_loggingRepository.Object, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetMembersAsync(request);
        }

        private async Task<string> CallSubsequentMembersReaderFunctionAsync(SubsequentMembersReaderRequest request)
        {
            var function = new SubsequentMembersReaderFunction(_loggingRepository.Object, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetMembersAsync(request);
        }

        private async Task<DeltaUrls> CallDeltaUserReaderFunctionAsync(DeltaUserReaderRequest request)
        {
            var function = new DeltaUserReaderFunction(_loggingRepository.Object, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetDeltaUsersAsync(request);
        }

        private async Task<DeltaUrls> CallSubsequentDeltaUserReaderFunctionAsync(SubsequentDeltaUserReaderRequest request)
        {
            var function = new SubsequentDeltaUserReaderFunction(_loggingRepository.Object, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetSubsequentDeltaUsersAsync(request);
        }
        private async Task<DeltaUrls> CallDeltaLinkUserReaderFunctionAsync(DeltaLinkUserReaderRequest request)
        {
            var function = new DeltaLinkUserReaderFunction(_loggingRepository.Object, _membershipCalculator, _blobStorageRepository.Object);
            return await function.GetDeltaLinkUsersAsync(request);
        }

        private async Task<DeltaUrls> CallSubsequentDeltaLinkUserReaderFunctionAsync(SubsequentDeltaLinkUserReaderRequest request)
        {
            var function = new SubsequentDeltaLinkUserReaderFunction(_loggingRepository.Object, _membershipCalculator, _blobStorageRepository.Object);
            return await function.GetSubsequentDeltaLinkUsersAsync(request);
        }

        private async Task CallDeltaLinkUploaderFunctionAsync(DeltaLinkUploaderRequest request)
        {
            var function = new DeltaLinkUploaderFunction(_loggingRepository.Object, _membershipCalculator);
            await function.SendDeltaLinkAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(_loggingRepository.Object, _membershipCalculator);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task CallCacheUploaderFunctionAsync(CacheUploaderRequest request)
        {
            var function = new CacheUploaderFunction(_loggingRepository.Object, _membershipCalculator);
            await function.SendUsersAsync(request);
        }

        private async Task<GroupMembershipFileResult> CallTransitiveAndDeltaUsersSenderFunctionAsync(TransitiveAndDeltaUsersSenderRequest request)
        {
            var function = new TransitiveAndDeltaUsersSenderFunction(_loggingRepository.Object, _blobStorageRepository.Object, _membershipCalculator);
            return await function.SendUsersAsync(request);
        }

        private async Task CallDeleteBlobFunctionAsync(DeleteBlobRequest request)
        {
            var function = new DeleteBlobFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await function.DeleteAsync(request);
        }

        private async Task CallProcessCachedAndDeltaUsersFunctionAsync(ProcessCachedAndDeltaUsersRequest request)
        {
            var function = new ProcessCachedAndDeltaUsersFunction(_loggingRepository.Object, _membershipCalculator, _blobStorageRepository.Object);
            await function.RunAsync(request);
        }
    }
}

