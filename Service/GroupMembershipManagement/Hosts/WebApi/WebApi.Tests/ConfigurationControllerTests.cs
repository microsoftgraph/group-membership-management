// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Models;
using Repositories.Contracts;
using WebApi.Controllers.v1.Configuration;
using Configuration = WebApi.Models.DTOs.Configuration;

namespace Services.Tests
{
    [TestClass]
    public class ConfigurationControllerTests
    {
        private ConfigurationController _configurationController = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseConfigurationRepository> _configurationRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private GetConfigurationHandler _getConfigurationHandler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _configurationRepository = new Mock<IDatabaseConfigurationRepository>();
            _getConfigurationHandler = new GetConfigurationHandler(_loggingRepository.Object, _configurationRepository.Object);
            _configurationController = new ConfigurationController(_getConfigurationHandler);
        }

        [TestMethod]
        public async Task GetConfigurationTestAsync()
        {
            var response = await _configurationController.GetConfigurationAsync(Guid.NewGuid());
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var configuration = result.Value as Configuration;

            Assert.IsNotNull(configuration.DashboardUrl);
        }
    }
}

