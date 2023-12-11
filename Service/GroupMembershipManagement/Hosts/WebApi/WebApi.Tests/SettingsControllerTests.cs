// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Models;
using Repositories.Contracts;
using WebApi.Controllers.v1.Settings;
using Microsoft.AspNetCore.Http;
using SettingDTO = WebApi.Models.DTOs.Setting;
using System.Security.Claims;
using WebApi.Models;
using Services.WebApi;
using WebApi.Controllers.v1.Jobs;

namespace Services.Tests
{
    [TestClass]
    public class SettingsControllerTests
    {
        private HttpContext _context = null!;
        private Setting _settingEntity = null!;
        private SettingsController _settingsController = null!;
        private List<Setting> _settings = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSettingsRepository> _settingsRepository = null!;
        private GetAllSettingsHandler _getAllSettingsHandler = null!;
        private GetSettingHandler _getSettingHandler = null!;
        private PatchSettingHandler _patchSettingHandler = null!;
        private SettingKey _settingKey;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;


        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _loggingRepository = new Mock<ILoggingRepository>();
            _settingsRepository = new Mock<IDatabaseSettingsRepository>();
            _getAllSettingsHandler = new GetAllSettingsHandler(_loggingRepository.Object, _settingsRepository.Object);
            _getSettingHandler = new GetSettingHandler(_loggingRepository.Object, _settingsRepository.Object);
            _patchSettingHandler = new PatchSettingHandler(_loggingRepository.Object, _settingsRepository.Object);
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.TENANT_ADMINISTRATOR)
                })
            };
            _settingKey = SettingKey.DashboardUrl;
            _settingEntity = new Setting { SettingKey = _settingKey, SettingValue = "testValue " };
            _settings = new List<Setting>
            {
                new Setting { SettingKey = SettingKey.DashboardUrl, SettingValue = "SettingValue1 " },
                new Setting { SettingKey = SettingKey.OutlookWarningUrl, SettingValue = "SettingValue1 " }
            };
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(_settingKey)).ReturnsAsync(() => _settingEntity);
            _settingsRepository.Setup(x => x.GetAllSettingsAsync()).ReturnsAsync(_settings);
        }

        [TestMethod]
        public async Task GetSettingByKeyTestAsync()
        {
            var response = await _settingsController.GetSettingByKeyAsync(_settingKey);
            Assert.IsNotNull(response);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var setting = okResult.Value as SettingDTO;
            Assert.IsNotNull(setting);
            Assert.IsNotNull(setting.SettingValue);
        }

        [TestMethod]
        public async Task GetAllSettingsTestAsync()
        {
            var response = await _settingsController.GetAllSettingsAsync();

            Assert.IsNotNull(response);
            Assert.IsInstanceOfType(response, typeof(OkObjectResult));

            var okResult = response as OkObjectResult;
            Assert.IsNotNull(okResult?.Value);

            var settingsResult = okResult.Value as List<SettingDTO>;
            Assert.IsNotNull(settingsResult);
            Assert.AreEqual(settingsResult.Count, _settings.Count);
        }

        [TestMethod]
        public async Task PatchSettingWhenTenantAdminTestAsync()
        {
            _settingsRepository.Setup(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()))
                               .Verifiable();

            var response = await _settingsController.PatchSettingAsync(_settingKey, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(NoContentResult));

            _settingsRepository.Verify(x => x.PatchSettingAsync(_settingKey, "updatedValue"), Times.Once());
        }

        [TestMethod]
        public async Task PatchSettingWhenNotAdminTestAsync()
        {
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.TENANT_READER)
                })
            };

            _settingsRepository.Setup(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()))
                   .Verifiable();

            var response = await _settingsController.PatchSettingAsync(_settingKey, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(UnauthorizedResult));

            _settingsRepository.Verify(x => x.PatchSettingAsync(_settingKey, "updatedValue"), Times.Never());
        }

        private ControllerContext CreateControllerContext(HttpContext httpContext)
        {
            return new ControllerContext { HttpContext = httpContext };
        }

        private ControllerContext CreateControllerContext(List<Claim> claims)
        {
            return new ControllerContext { HttpContext = CreateHttpContext(claims) };
        }

        private HttpContext CreateHttpContext(List<Claim> claims)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();
            httpContext.User = principal;

            return httpContext;
        }
    }
}
