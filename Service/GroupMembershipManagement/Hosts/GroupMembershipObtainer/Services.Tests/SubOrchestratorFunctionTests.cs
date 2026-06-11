// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
using Services.Contracts;

namespace Tests.Services
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        private const int Number_Of_Pages = 2;

        private Mock<IDryRunValue> _dryRunValue;
        private MockDeltaCachingConfig _deltaCachingConfig;
        private Mock<IMailRepository> _mailRepository;
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
        private Mock<ISyncJobStatusService> _syncJobStatusService;

        private int _userCount;
        private bool _groupExists;
        private int _groupCount;
        private BlobResult _blobResult;
        private BlobResult _cacheBlobResult;
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
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _groupsRepository = new Mock<IDatabaseGroupsRepository>();
            _channelsRepository = new Mock<IDatabaseChannelsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<TaskOrchestrationContext>();
            _durableOrchestrationContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _destinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _syncJobStatusService = new Mock<ISyncJobStatusService>();

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

            _cacheBlobResult = new BlobResult
            {
                BlobStatus = BlobStatus.Found,
                Path = "cache/file-name.txt",
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
                GroupId = Guid.NewGuid(),
                SourceGroup = new AzureADGroup { ObjectId = Guid.NewGuid() },
                SyncJob = syncJob
            };

            _blobStorageRepository.Setup(x => x.ReadValuesFromBlobAsync<Guid>(It.IsAny<string>(), It.IsAny<Func<string, Guid>>()))
                                  .ReturnsAsync(() => content.SourceMembers.Select(x => x.ObjectId).ToHashSet() );

            _membershipCalculator = new SGMembershipCalculator(
                                            _graphGroupRepository.Object,
                                            _blobStorageRepository.Object,
                                            _syncJobRepository.Object,
                                            _groupsRepository.Object,
                                            _channelsRepository.Object,
                                            _serviceBusQueueRepository.Object,
                                            _destinationAttributesRepository.Object,
                                            NullLogger<SGMembershipCalculator>.Instance,
                                            _dryRunValue.Object,
                                            _syncJobStatusService.Object
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

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<BlobResult>(nameof(BlobCheckerFunction), It.IsAny<BlobCheckerRequest>(), It.IsAny<TaskOptions>()))
                                        .ReturnsAsync(() => _cacheBlobResult);

            _graphGroupRepository.Setup(x => x.GroupExists(It.IsAny<Guid>())).ReturnsAsync(() => _groupExists);
            _graphGroupRepository.Setup(x => x.GetGroupsCountAsync(It.IsAny<Guid>())).ReturnsAsync(() => _groupCount);
            _graphGroupRepository.Setup(x => x.GetUsersCountAsync(It.IsAny<Guid>())).ReturnsAsync(() => _userCount);

            _blobStorageRepository.Setup(x => x.DownloadCacheFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);
            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);
            _blobStorageRepository.Setup(x => x.StreamUsersFromBlobsAsync(It.IsAny<string>())).Returns(GetEmptyAsyncEnumerable<AzureADUser>());
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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _graphGroupRepository.Verify(x => x.GetGroupsCountAsync(It.IsAny<Guid>()), Times.Once);
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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
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
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);
            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);

            Assert.AreEqual(SyncStatus.SecurityGroupNotFound, response.Status);
        }

        [TestMethod]
        public async Task TestTransientExceptionAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                        .Throws<HttpRequestException>();

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(async () => await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object));
        }

        [TestMethod]
        public async Task TestOtherExceptionAsync()
        {
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                        .Throws<OutOfMemoryException>();

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, telemetryClient);

            await Assert.ThrowsExceptionAsync<OutOfMemoryException>(async () => await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object));
        }

        private async Task<bool> CallGroupValidatorFunctionAsync(GroupValidatorRequest request)
        {
            var function = new GroupValidatorFunction(NullLogger<GroupValidatorFunction>.Instance, _membershipCalculator, _emailSenderRecipient.Object);
            return await function.ValidateGroupAsync(request);
        }

        private async Task<string> CallFileDownloaderFunctionAsync(FileDownloaderRequest request)
        {
            var function = new FileDownloaderFunction(NullLogger<FileDownloaderFunction>.Instance, _blobStorageRepository.Object);
            return await function.DownloadFileAsync(request);
        }

        private async Task CallFileDeleterFunctionAsync(FileDeleterRequest request)
        {
            var function = new FileDeleterFunction(NullLogger<FileDeleterFunction>.Instance, _blobStorageRepository.Object);
            await function.DeleteFileAsync(request);
        }

        private async Task<int> CallGroupsReaderFunctionAsync(GetTransitiveGroupCountRequest request)
        {
            var function = new GetTransitiveGroupCountFunction(NullLogger<GetTransitiveGroupCountFunction>.Instance, _membershipCalculator);
            return await function.GetGroupsAsync(request);
        }

        private async Task<int> CallUsersReaderFunctionAsync(GetUserCountRequest request)
        {
            var function = new GetUserCountFunction(NullLogger<GetUserCountFunction>.Instance, _membershipCalculator);
            return await function.GetUserCountAsync(request);
        }

        private async Task<string> CallMembersReaderFunctionAsync(MembersReaderRequest request)
        {
            var function = new MembersReaderFunction(NullLogger<MembersReaderFunction>.Instance, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetMembersAsync(request);
        }

        private async Task<string> CallSubsequentMembersReaderFunctionAsync(SubsequentMembersReaderRequest request)
        {
            var function = new SubsequentMembersReaderFunction(NullLogger<SubsequentMembersReaderFunction>.Instance, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetMembersAsync(request);
        }

        private async Task<DeltaUrls> CallDeltaUserReaderFunctionAsync(DeltaUserReaderRequest request)
        {
            var function = new DeltaUserReaderFunction(NullLogger<DeltaUserReaderFunction>.Instance, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetDeltaUsersAsync(request);
        }

        private async Task<DeltaUrls> CallSubsequentDeltaUserReaderFunctionAsync(SubsequentDeltaUserReaderRequest request)
        {
            var function = new SubsequentDeltaUserReaderFunction(NullLogger<SubsequentDeltaUserReaderFunction>.Instance, _blobStorageRepository.Object, _membershipCalculator);
            return await function.GetSubsequentDeltaUsersAsync(request);
        }
        private async Task<DeltaUrls> CallDeltaLinkUserReaderFunctionAsync(DeltaLinkUserReaderRequest request)
        {
            var function = new DeltaLinkUserReaderFunction(NullLogger<DeltaLinkUserReaderFunction>.Instance, _membershipCalculator, _blobStorageRepository.Object);
            return await function.GetDeltaLinkUsersAsync(request);
        }

        private async Task<DeltaUrls> CallSubsequentDeltaLinkUserReaderFunctionAsync(SubsequentDeltaLinkUserReaderRequest request)
        {
            var function = new SubsequentDeltaLinkUserReaderFunction(NullLogger<SubsequentDeltaLinkUserReaderFunction>.Instance, _membershipCalculator, _blobStorageRepository.Object);
            return await function.GetSubsequentDeltaLinkUsersAsync(request);
        }

        private async Task CallDeltaLinkUploaderFunctionAsync(DeltaLinkUploaderRequest request)
        {
            var function = new DeltaLinkUploaderFunction(NullLogger<DeltaLinkUploaderFunction>.Instance, _membershipCalculator);
            await function.SendDeltaLinkAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, _membershipCalculator);
            await function.UpdateJobStatusAsync(request);
        }

        private async Task CallCacheUploaderFunctionAsync(CacheUploaderRequest request)
        {
            var function = new CacheUploaderFunction(NullLogger<CacheUploaderFunction>.Instance, _membershipCalculator);
            await function.SendUsersAsync(request);
        }

        private async Task<GroupMembershipFileResult> CallTransitiveAndDeltaUsersSenderFunctionAsync(TransitiveAndDeltaUsersSenderRequest request)
        {
            var function = new TransitiveAndDeltaUsersSenderFunction(NullLogger<TransitiveAndDeltaUsersSenderFunction>.Instance, _blobStorageRepository.Object, _membershipCalculator);
            return await function.SendUsersAsync(request);
        }

        private async Task CallDeleteBlobFunctionAsync(DeleteBlobRequest request)
        {
            var function = new DeleteBlobFunction(NullLogger<DeleteBlobFunction>.Instance, _blobStorageRepository.Object);
            await function.DeleteAsync(request);
        }

        private async Task CallProcessCachedAndDeltaUsersFunctionAsync(ProcessCachedAndDeltaUsersRequest request)
        {
            var function = new ProcessCachedAndDeltaUsersFunction(NullLogger<ProcessCachedAndDeltaUsersFunction>.Instance, _membershipCalculator, _blobStorageRepository.Object);
            await function.RunAsync(request);
        }

        private async Task CallLogNestedGroupsFunctionAsync(LogNestedGroupsRequest request)
        {
            var function = new LogNestedGroupsFunction(NullLogger<LogNestedGroupsFunction>.Instance, _graphGroupRepository.Object);
            await function.LogNestedGroupsAsync(request);
        }

        [TestMethod]
        public async Task WhenSourceEqualsDestinationWithNestedGroups_ShouldDetectAndReturnNestedGroupsFoundStatus()
        {
            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var sourceGroupId = groupId; // Source equals destination
            var transitiveGroupCount = 5; // Has nested groups

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = groupId,                Status = "InProgress",
                Query = $"[{{\"type\":\"GroupMembership\",\"sources\":[{sourceGroupId}]}}]"
            };

            var request = new GroupMembershipRequest
            {                GroupId = groupId,
                SourceGroup = new AzureADGroup { ObjectId = sourceGroupId },
                SyncJob = syncJob,
                CurrentPart = 1,
                Exclusionary = false
            };

            _groupExists = true;
            _groupCount = transitiveGroupCount;

            var nestedGroups = Enumerable.Range(0, transitiveGroupCount)
                .Select(i => new AzureADGroup { ObjectId = Guid.NewGuid(), Name = $"NestedGroup{i}" })
                .ToList();

            _graphGroupRepository.Setup(x => x.GetDirectGroupTypeMembersAsync(It.IsAny<Guid>()))
                .ReturnsAsync(nestedGroups);

            _durableOrchestrationContext.Setup(x => x.GetInput<GroupMembershipRequest>()).Returns(request);
            _durableOrchestrationContext.SetupGet(x => x.IsReplaying).Returns(false);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                                        .ReturnsAsync(_groupExists);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            _groupCount = await CallGroupsReaderFunctionAsync(req as GetTransitiveGroupCountRequest);
                                        })
                                        .ReturnsAsync(() => _groupCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<List<AzureADGroup>>(It.IsAny<TaskName>(), It.IsAny<LogNestedGroupsRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            await CallLogNestedGroupsFunctionAsync(req as LogNestedGroupsRequest);
                                        })
                                        .ReturnsAsync(nestedGroups);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DestinationNameReaderRequest>(), It.IsAny<TaskOptions>()))
                                        .ReturnsAsync("Test Group Name");

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<EmailSenderRequest>(), It.IsAny<TaskOptions>()));

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<JobStatusUpdaterRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            await CallJobStatusUpdaterFunctionAsync(req as JobStatusUpdaterRequest);
                                        });

            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            Assert.AreEqual(SyncStatus.NestedGroupsFound, response.Status);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<List<AzureADGroup>>(
                It.Is<TaskName>(n => n.Name == nameof(LogNestedGroupsFunction)),
                It.Is<LogNestedGroupsRequest>(r => r.GroupId == groupId),
                It.IsAny<TaskOptions>()), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == nameof(EmailSenderFunction)),
                It.IsAny<EmailSenderRequest>(),
                It.IsAny<TaskOptions>()), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.NestedGroupsFound && r.SyncJob.Id == syncJob.Id),
                It.IsAny<TaskOptions>()), Times.Once);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(
                It.Is<TaskName>(n => n.Name == nameof(MembersReaderFunction)),
                It.IsAny<MembersReaderRequest>(),
                It.IsAny<TaskOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task WhenSourceDiffersFromDestinationWithNestedGroups_ShouldContinueNormalProcessing()
        {
            var runId = Guid.NewGuid();
            var destinationGroupId = Guid.NewGuid();
            var sourceGroupId = Guid.NewGuid(); // Source differs from destination
            var transitiveGroupCount = 3; // Has nested groups but not self-referencing

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = destinationGroupId,                Status = "InProgress",
                Query = $"[{{\"type\":\"GroupMembership\",\"sources\":[{sourceGroupId}]}}]"
            };

            var request = new GroupMembershipRequest
            {                GroupId = destinationGroupId,
                SourceGroup = new AzureADGroup { ObjectId = sourceGroupId },
                SyncJob = syncJob,
                CurrentPart = 1,
                Exclusionary = false
            };

            _groupExists = true;
            _groupCount = transitiveGroupCount;
            _membersReaderResponse = string.Empty;

            _durableOrchestrationContext.Setup(x => x.GetInput<GroupMembershipRequest>()).Returns(request);
            _durableOrchestrationContext.SetupGet(x => x.IsReplaying).Returns(false);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                                        .ReturnsAsync(_groupExists);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            _groupCount = await CallGroupsReaderFunctionAsync(req as GetTransitiveGroupCountRequest);
                                        })
                                        .ReturnsAsync(() => _groupCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<MembersReaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            _membersReaderResponse = await CallMembersReaderFunctionAsync(req as MembersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _membersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<GroupMembershipFileResult>(It.IsAny<TaskName>(), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            _membershipFileResult = await CallTransitiveAndDeltaUsersSenderFunctionAsync(req as TransitiveAndDeltaUsersSenderRequest);
                                        })
                                        .ReturnsAsync(() => new GroupMembershipFileResult { FilePath = _filePath, MemberCount = _userCount });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            await CallDeleteBlobFunctionAsync(req as DeleteBlobRequest);
                                        });

            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
            Assert.AreEqual(_filePath, response.FilePath);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == nameof(LogNestedGroupsFunction)),
                It.IsAny<LogNestedGroupsRequest>(),
                It.IsAny<TaskOptions>()), Times.Never);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.NestedGroupsFound),
                It.IsAny<TaskOptions>()), Times.Never);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync<string>(
                It.Is<TaskName>(n => n.Name == nameof(MembersReaderFunction)),
                It.IsAny<MembersReaderRequest>(),
                It.IsAny<TaskOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task WhenSourceEqualsDestinationWithoutNestedGroups_ShouldContinueNormalProcessing()
        {
            var runId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var sourceGroupId = groupId; // Source equals destination
            var transitiveGroupCount = 0; // No nested groups

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = groupId,                Status = "InProgress",
                Query = $"[{{\"type\":\"GroupMembership\",\"sources\":[{sourceGroupId}]}}]"
            };

            var request = new GroupMembershipRequest
            {                GroupId = groupId,
                SourceGroup = new AzureADGroup { ObjectId = sourceGroupId },
                SyncJob = syncJob,
                CurrentPart = 1,
                Exclusionary = false
            };

            _groupExists = true;
            _groupCount = transitiveGroupCount;
            _deltaUserReaderResponse = new DeltaUrls { DeltaUrl = "delta-url", NextPageUrl = string.Empty };

            _durableOrchestrationContext.Setup(x => x.GetInput<GroupMembershipRequest>()).Returns(request);
            _durableOrchestrationContext.SetupGet(x => x.IsReplaying).Returns(false);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.IsAny<TaskName>(), It.IsAny<GroupValidatorRequest>(), It.IsAny<TaskOptions>()))
                                        .ReturnsAsync(_groupExists);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<int>(It.IsAny<TaskName>(), It.IsAny<GetTransitiveGroupCountRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            _groupCount = await CallGroupsReaderFunctionAsync(req as GetTransitiveGroupCountRequest);
                                        })
                                        .ReturnsAsync(() => _groupCount);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<DeltaUrls>(It.IsAny<TaskName>(), It.IsAny<DeltaUserReaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            _deltaUserReaderResponse = await CallDeltaUserReaderFunctionAsync(req as DeltaUserReaderRequest);
                                        })
                                        .ReturnsAsync(() => _deltaUserReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<GroupMembershipFileResult>(It.IsAny<TaskName>(), It.IsAny<TransitiveAndDeltaUsersSenderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            _membershipFileResult = await CallTransitiveAndDeltaUsersSenderFunctionAsync(req as TransitiveAndDeltaUsersSenderRequest);
                                        })
                                        .ReturnsAsync(() => new GroupMembershipFileResult { FilePath = _filePath, MemberCount = _userCount });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeleteBlobRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            await CallDeleteBlobFunctionAsync(req as DeleteBlobRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<CacheUploaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            await CallCacheUploaderFunctionAsync(req as CacheUploaderRequest);
                                        });

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<DeltaLinkUploaderRequest>(), It.IsAny<TaskOptions>()))
                                        .Callback<TaskName, object, TaskOptions>(async (name, req, options) =>
                                        {
                                            await CallDeltaLinkUploaderFunctionAsync(req as DeltaLinkUploaderRequest);
                                        });

            var subOrchestratorFunction = new SubOrchestratorFunction(_deltaCachingConfig, _telemetryClient);
            var response = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            Assert.AreEqual(SyncStatus.InProgress, response.Status);
            Assert.AreEqual(_filePath, response.FilePath);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == nameof(LogNestedGroupsFunction)),
                It.IsAny<LogNestedGroupsRequest>(),
                It.IsAny<TaskOptions>()), Times.Never);

            _durableOrchestrationContext.Verify(x => x.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == nameof(JobStatusUpdaterFunction)),
                It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.NestedGroupsFound),
                It.IsAny<TaskOptions>()), Times.Never);
        }

        private static async IAsyncEnumerable<T> GetEmptyAsyncEnumerable<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}

