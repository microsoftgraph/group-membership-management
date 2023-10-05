// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Models;
using Repositories.Contracts;
using WebApi.Controllers.v1.Settings;
using Microsoft.AspNetCore.Http;
using SettingDTO = WebApi.Models.DTOs.Setting;

namespace Services.Tests
{
    [TestClass]
    public class SettingsControllerTests
    {
        private HttpContext _context = null!;
        private Setting _settingEntity = null!;
        private SettingsController _settingsController = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSettingsRepository> _settingsRepository = null!;
        private GetSettingHandler _getSettingHandler = null!;
        private UpdateSettingHandler _updateSettingHandler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _loggingRepository = new Mock<ILoggingRepository>();
            _settingsRepository = new Mock<IDatabaseSettingsRepository>();
            _getSettingHandler = new GetSettingHandler(_loggingRepository.Object, _settingsRepository.Object);
            _updateSettingHandler = new UpdateSettingHandler(_loggingRepository.Object, _settingsRepository.Object);
            _settingsController = new SettingsController(_getSettingHandler, _updateSettingHandler)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = _context
                }
            };
            _settingEntity = new Setting { Key = "testKey", Value = "testValue " };

            _settingsRepository.Setup(x => x.GetSettingByKeyAsync("testKey")).ReturnsAsync(() => _settingEntity);
        }

        [TestMethod]
        public async Task GetSettingByKeyTestAsync()
        {
            var response = await _settingsController.GetSettingByKeyAsync("testKey");
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var setting = result.Value as SettingDTO;

            Assert.IsNotNull(setting.Value);
        }

        [TestMethod]
        public async Task UpdateSettingTestAsync()
        {
            _settingsRepository.Setup(x => x.UpdateSettingAsync(It.IsAny<Setting>(), It.IsAny<string>()))
                               .Callback<Setting, string>((s, v) => s.Value = v);

            await _settingsController.UpdateSettingAsync("testKey", "updatedValue");
            _settingsRepository.Verify(x => x.UpdateSettingAsync(_settingEntity, "updatedValue"), Times.Once());

            var settingResponse = await _settingsController.GetSettingByKeyAsync("testKey");
            var result = settingResponse.Result as OkObjectResult;
            var settingDTO = result?.Value as SettingDTO;

            Assert.IsNotNull(settingDTO);
            Assert.AreEqual("updatedValue", settingDTO?.Value);
        }
    }
}

