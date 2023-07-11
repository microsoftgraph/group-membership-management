// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Models;
using Repositories.Contracts;
using WebApi.Controllers.v1.Jobs;
using SyncJobDetails = WebApi.Models.DTOs.SyncJobDetails;

namespace Services.Tests
{
    [TestClass]
    public class JobDetailsControllerTests
    {
        private SyncJob _jobEntity = null!;
        private JobDetailsController _jobDetailsController = null!;
        private GetJobDetailsHandler _getJobDetailsHandler = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;

        [TestInitialize]
        public void Initialize()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();

            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => true);

            _jobEntity = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = ((SyncStatus)Random.Shared.Next(1, 15)).ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-4),
                LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-5),
                StartDate = DateTime.UtcNow.AddMonths(-1),
                Query = "",
                ThresholdViolations = 0,
                ThresholdPercentageForAdditions = 10,
                ThresholdPercentageForRemovals = 10,
                Period = 6, 
                Requestor = "example@microsoft.com",
            };
           

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _jobEntity);

            _getJobDetailsHandler = new GetJobDetailsHandler(_loggingRepository.Object,
                                                 _syncJobRepository.Object,
                                                 _graphGroupRepository.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler);
        }

        [TestMethod]
        public async Task GetJobDetailsTestAsync()
        {
            var response = await _jobDetailsController.GetJobDetailsAsync(Guid.NewGuid());
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job.StartDate);
            Assert.IsNotNull(job.Requestor);
            Assert.IsNotNull(job.Source);
        }

        [TestMethod]
        public async Task GetJobDetailsTestRequestorNotAnOwnerAsync()
        {
            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => false);

            _getJobDetailsHandler = new GetJobDetailsHandler(
                                     _loggingRepository.Object,
                                     _syncJobRepository.Object,
                                     _graphGroupRepository.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler);

            var response = await _jobDetailsController.GetJobDetailsAsync(Guid.NewGuid());
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job);
            Assert.AreEqual("example@microsoft.com (Not an Owner)", job.Requestor);
        
        }
    }
}

