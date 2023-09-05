// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Models;
using Repositories.Contracts;
using WebApi.Controllers.v1.Settings;
using Microsoft.AspNetCore.Http;
using SettingsDTO = WebApi.Models.DTOs.Settings;

namespace Services.Tests
{
    [TestClass]
    public class SettingsControllerTests
    {
        private HttpContext _context = null!;
        private Settings _settingsEntity = null!;
        private SettingsController _settingsController = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSettingsRepository> _settingsRepository = null!;
        private GetSettingsHandler _getSettingsHandler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _loggingRepository = new Mock<ILoggingRepository>();
            _settingsRepository = new Mock<IDatabaseSettingsRepository>();
            _getSettingsHandler = new GetSettingsHandler(_loggingRepository.Object, _settingsRepository.Object);
            _settingsController = new SettingsController(_getSettingsHandler);
            _settingsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };
            _settingsEntity = new Settings
            {
                Key = "settingId",
                Value = "settingValue"
            };

            _settingsRepository.Setup(x => x.GetSettingsAsync(It.IsAny<string>())).
                ReturnsAsync(() => _settingsEntity);

        }

        [TestMethod]
        public async Task GetSettingsTestAsync()
        {
            var response = await _settingsController.GetSettingsAsync("settingId");
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var settings = result.Value as SettingsDTO;

            Assert.IsNotNull(settings.Value);
        }
    }
}

