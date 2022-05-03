// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Entities.ServiceBus;
using Hosts.MembershipAggregator;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        private SyncJob _syncJob;
        private string _fileContent;
        private BlobResult _blobResult;
        private DurableHttpResponse _durableHttpResponse;
        private MembershipAggregatorHttpRequest _membershipAggregatorHttpRequest;
        private Mock<IConfiguration> _configuration;
        private Mock<JobTrackerEntity> _jobTrackerEntity;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ISyncJobRepository> _syncJobRepository;
        private Mock<IDurableOrchestrationContext> _durableContext;
        private Mock<IBlobStorageRepository> _blobStorageRepository;

        [TestInitialize]
        public void SetupTest()
        {
            _configuration = new Mock<IConfiguration>();
            _jobTrackerEntity = new Mock<JobTrackerEntity>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _durableContext = new Mock<IDurableOrchestrationContext>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();

            _syncJob = new SyncJob
            {
                PartitionKey = "00-00-0000",
                RowKey = Guid.NewGuid().ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };

            _membershipAggregatorHttpRequest = new MembershipAggregatorHttpRequest
            {
                FilePath = "/file-path.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1
            };

            _blobResult = new BlobResult
            {
                BlobStatus = BlobStatus.Found,
                Content = new BinaryData(new GroupMembership
                {
                    SourceMembers = new List<AzureADUser> { { new AzureADUser { ObjectId = Guid.NewGuid() } } },
                    Destination = new AzureADGroup { ObjectId = Guid.NewGuid() }
                })
            };

            _fileContent = null;

            _configuration.Setup(x => x[It.Is<string>(x => x == "graphUpdaterUrl")])
                            .Returns("http://graph-updater-url");
            _configuration.Setup(x => x[It.Is<string>(x => x == "graphUpdaterFunctionKey")])
                            .Returns("112233445566");

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                                    .ReturnsAsync(() => _blobResult);

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<string>(), It.IsAny<string>()))
                                .ReturnsAsync(() => _syncJob);

            _durableContext.Setup(x => x.GetInput<MembershipAggregatorHttpRequest>())
                            .Returns(() => _membershipAggregatorHttpRequest);

            _durableContext.Setup(x => x.CreateEntityProxy<IJobTracker>(It.IsAny<EntityId>()))
                            .Returns(() => _jobTrackerEntity.Object);

            _durableContext.Setup(x => x.CallActivityAsync<string>(It.Is<string>(x => x == nameof(FileDownloaderFunction)), It.IsAny<FileDownloaderRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                var fileDownloaderRequest = request as FileDownloaderRequest;
                                _fileContent = await CallFileDownloaderFunctionAsync(fileDownloaderRequest);
                            })
                            .ReturnsAsync(() => _fileContent);

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(FileUploaderFunction)), It.IsAny<FileUploaderRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                var fileUploaderRequest = request as FileUploaderRequest;
                                await CallFileDownloaderFunctionAsync(fileUploaderRequest);
                            });

            _durableContext.Setup(x => x.CallHttpAsync(It.IsAny<DurableHttpRequest>()))
                            .ReturnsAsync(() => _durableHttpResponse);

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(LoggerFunction)), It.IsAny<LogMessage>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                var loggerRequest = request as LogMessage;
                                await CallLoggerFunctionAsync(loggerRequest);
                            });

            _durableContext.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>()))
                            .Callback<string, object>(async (name, request) =>
                            {
                                var updateRequest = request as JobStatusUpdaterRequest;
                                await CallJobStatusUpdaterFunctionAsync(updateRequest);
                            });
        }

        [TestMethod]
        public async Task TestJobWithSinglePartAsync()
        {
            _durableHttpResponse = new DurableHttpResponse(System.Net.HttpStatusCode.NoContent);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Once());
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), It.IsAny<string>(), It.IsAny<string>()));
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), It.IsAny<string>(), It.IsAny<string>()));
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus>()), Times.Never());
        }

        [TestMethod]
        public async Task HandleBadRequestResponseAsync()
        {
            _durableHttpResponse = new DurableHttpResponse(System.Net.HttpStatusCode.BadRequest);

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);
            await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object);

            _blobStorageRepository.Verify(x => x.DownloadFileAsync(It.IsAny<string>()), Times.Once());
            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Once());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), It.IsAny<string>(), It.IsAny<string>()));
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), It.IsAny<string>(), It.IsAny<string>()));
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.Is<SyncStatus>(s => s == SyncStatus.Error)), Times.Once());
        }

        [TestMethod]
        public async Task HandleFileNotFoundAsync()
        {
            _durableContext.Setup(x => x.CallActivityAsync<string>(It.Is<string>(x => x == nameof(FileDownloaderFunction)), It.IsAny<FileDownloaderRequest>())).Throws<FileNotFoundException>();

            var orchestratorFunction = new OrchestratorFunction(_configuration.Object);
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () => await orchestratorFunction.RunOrchestratorAsync(_durableContext.Object));

            _blobStorageRepository.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message == "Calling GraphUpdater"), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _loggingRepository.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.StartsWith("GraphUpdater response Code")), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _syncJobRepository.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<IEnumerable<SyncJob>>(), It.Is<SyncStatus>(s => s == SyncStatus.FileNotFound)), Times.Once());
        }

        private async Task<string> CallFileDownloaderFunctionAsync(FileDownloaderRequest request)
        {
            var fileDownloaderFunction = new FileDownloaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            return await fileDownloaderFunction.DownloadFileAsync(request);
        }

        private async Task CallFileDownloaderFunctionAsync(FileUploaderRequest request)
        {
            var fileUploaderFunction = new FileUploaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            await fileUploaderFunction.UploadFileAsync(request);
        }

        private async Task CallLoggerFunctionAsync(LogMessage request)
        {
            var loggerFunction = new LoggerFunction(_loggingRepository.Object);
            await loggerFunction.LogMessageAsync(request);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(_loggingRepository.Object, _syncJobRepository.Object);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }
    }
}
