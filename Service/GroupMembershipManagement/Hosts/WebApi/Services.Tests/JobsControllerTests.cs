// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Repositories.Contracts;
using Services.Messages.Responses;
using WebApi.Controllers.v1;

namespace Services.Tests
{
    [TestClass]
    public class JobsControllerTests
    {
        private JobsController _jobsController = null!;
        private GetJobsHandler _getJobsHandler = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;

        [TestInitialize]
        public void Initialize()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _getJobsHandler = new GetJobsHandler(_loggingRepository.Object);
            _jobsController = new JobsController(_getJobsHandler);
        }

        [TestMethod]
        public async Task GetJobsTestAsync()
        {
            var response = await _jobsController.GetJobsAsync();
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var jobs = result.Value as GetJobsResponse;

            Assert.IsNotNull(jobs);
            Assert.AreEqual(10, jobs.Model.Count);
            Assert.IsTrue(jobs.Model.All(x => x.RowKey != null && x.PartitionKey != null));
        }
    }
}
