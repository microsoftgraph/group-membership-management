// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        private Mock<IDryRunValue> _dryRunValue;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<TaskOrchestrationContext> _durableOrchestrationContext;

        private int _userCount;
        private BlobResult _blobResult;
        private string _cacheUrl;
        private CacheUserUpdaterRequest _cacheUserUpdaterRequest;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<TaskOrchestrationContext>();
            _durableOrchestrationContext.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            _userCount = 10;

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

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Group = new Group
                {
                    GroupId = Guid.NewGuid()
                },
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                Status = "InProgress",
                Period = 6,
                RunId = Guid.NewGuid()
            };

            var userIds = new HashSet<Guid>();
            for (int i = 0; i < 10; i++)
            {
                userIds.Add(Guid.NewGuid());
            }

            _cacheUserUpdaterRequest = new CacheUserUpdaterRequest
            {
                UserIds = userIds,
                SyncJob = syncJob,
                GroupId = Guid.NewGuid()
            };

            _durableOrchestrationContext.Setup(x => x.GetInput<CacheUserUpdaterRequest>()).Returns(() => _cacheUserUpdaterRequest);
            _blobStorageRepository.Setup(x => x.DownloadCacheFileAsync(It.IsAny<string>())).ReturnsAsync(() => _blobResult);
            _blobStorageRepository.Setup(x => x.ReadValuesFromBlobAsync<Guid>(It.IsAny<string>(), It.IsAny<Func<string, Guid>>()))
                                  .ReturnsAsync(() => content.SourceMembers.Select(x => x.ObjectId).ToHashSet());
        }

        [TestMethod]
        public async Task DownloadCacheFileAsync()
        {
            _cacheUrl = "http://cache-url";
            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<FileDownloaderRequest>(), It.IsAny<TaskOptions>()))
                                       .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                                       {
                                           _cacheUrl = await CallFileDownloaderFunctionAsync(request as FileDownloaderRequest);
                                       })
                                       .ReturnsAsync(() => _cacheUrl);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(CacheUpdaterFunction)), It.IsAny<CacheUpdaterRequest>(), It.IsAny<TaskOptions>()))
                            .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                            {
                                await CallCacheUpdaterFunctionAsync(request as CacheUpdaterRequest);
                            });

            var cacheBlobResult = new BlobResult
            {
                BlobStatus = BlobStatus.Found,
                Path = "cache/file-name.txt",
            };

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<BlobResult>(It.Is<TaskName>(x => x.Name == nameof(BlobCheckerFunction)), It.IsAny<BlobCheckerRequest>(), It.IsAny<TaskOptions>()))
                            .ReturnsAsync(() => cacheBlobResult);

            var telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            var subOrchestratorFunction = new CacheUserUpdaterSubOrchestratorFunction();
            await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _blobStorageRepository.Verify(x => x.ReadValuesFromBlobAsync(It.IsAny<string>(), It.IsAny<Func<string, Guid>>()), Times.Exactly(1));
            _blobStorageRepository.Verify(x => x.UploadCacheFromGuidsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<Dictionary<string, string>>()), Times.Exactly(1));

        }

        private async Task<string> CallFileDownloaderFunctionAsync(FileDownloaderRequest request)
        {
            var function = new FileDownloaderFunction(NullLogger<FileDownloaderFunction>.Instance, _blobStorageRepository.Object);
            return await function.DownloadFileAsync(request);
        }

        private async Task CallCacheUpdaterFunctionAsync(CacheUpdaterRequest request)
        {
            var function = new CacheUpdaterFunction(NullLogger<CacheUpdaterFunction>.Instance, _blobStorageRepository.Object);
            await function.UpdateCacheAsync(request);
        }

    }
}