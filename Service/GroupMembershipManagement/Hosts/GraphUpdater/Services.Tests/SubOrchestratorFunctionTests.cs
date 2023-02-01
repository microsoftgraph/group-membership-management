// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models.Entities;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;

        private int _userCount;
        private BlobResult _blobResult;
        private string _cacheUrl;
        private CacheUserUpdaterRequest _cacheUserUpdaterRequest;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
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

            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                Status = "InProgress",
                Period = 6
            };

            var users = new List<AzureADUser>();
            for (int i = 0; i < 10; i++)
            {
                users.Add(new AzureADUser
                {
                    ObjectId = Guid.NewGuid()
                });
            }

            _cacheUserUpdaterRequest = new CacheUserUpdaterRequest
            {
                RunId = Guid.NewGuid(),
                UserIds = users,
                SyncJob = syncJob,
                GroupId = Guid.NewGuid()
            };

            _durableOrchestrationContext.Setup(x => x.GetInput<CacheUserUpdaterRequest>()).Returns(() => _cacheUserUpdaterRequest);
            _blobStorageRepository.Setup(x => x.DownloadCacheFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);
        }

        [TestMethod]
        public async Task DownloadCacheFileAsync()
        {
            _cacheUrl = "http://cache-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<string>(), It.IsAny<FileDownloaderRequest>()))
                                       .Callback<string, object>(async (name, request) =>
                                       {
                                           _cacheUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _cacheUrl);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(FileUploaderFunction)), It.IsAny<FileUploaderRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                await CallFileUploaderFunctionAsync(request as FileUploaderRequest);
                            });

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new CacheUserUpdaterSubOrchestratorFunction(_loggingRepository.Object, telemetryClient);
            await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);
            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(CacheUserUpdaterSubOrchestratorFunction)} function started"),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _blobStorageRepository.Verify(x => x.DownloadCacheFileAsync(It.IsAny<string>()), Times.Exactly(1));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Exactly(1));
            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(CacheUserUpdaterSubOrchestratorFunction)} function completed"),
                        It.IsAny<VerbosityLevel>(),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

        }

        private async Task<string> CallFileDownloaderFunctionAsync(FileDownloaderRequest request)
        {
            var function = new FileDownloaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            return await function.DownloadFileAsync(request);
        }

        private async Task CallFileUploaderFunctionAsync(FileUploaderRequest request)
        {
            var function = new FileUploaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await function.SendUsersAsync(request);
        }
    }
}

