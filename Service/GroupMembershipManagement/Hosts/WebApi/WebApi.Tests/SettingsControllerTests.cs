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
using System.Net;
using Azure.Security.KeyVault.Secrets;
using Azure;
using Services.Messages.Requests;
using Services.Contracts;
using Services.Messages.Responses;
using Microsoft.Extensions.Options;
using WebApi.Configuration;

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
        private GetSupportEmailHandler _getSupportEmailHandler = null!;
        private SettingKey _settingKey;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;
        private Mock<IOptions<WebApiSettings>> _webApiSettings = null!;

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _loggingRepository = new Mock<ILoggingRepository>();
            _settingsRepository = new Mock<IDatabaseSettingsRepository>();
            _getAllSettingsHandler = new GetAllSettingsHandler(_loggingRepository.Object, _settingsRepository.Object);
            _getSettingHandler = new GetSettingHandler(_loggingRepository.Object, _settingsRepository.Object);
            _patchSettingHandler = new PatchSettingHandler(_loggingRepository.Object, _settingsRepository.Object);
            _getSupportEmailHandler = new GetSupportEmailHandler(_loggingRepository.Object, _webApiSettings.Object);
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.HYPERLINK_ADMINISTRATOR)
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
        public async Task GetSettingByKeyNotFoundTestAsync()
        {
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(_settingKey)).ReturnsAsync(() => null);
            var response = await _settingsController.GetSettingByKeyAsync(_settingKey);
            Assert.IsNotNull(response);

            var result = response as ObjectResult;
            Assert.IsNull(result);
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
        public async Task GetSettingByKeyExceptionTestAsync()
        {
            var nonExistentSettingKey = new SettingKey();

            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(nonExistentSettingKey))
                               .ThrowsAsync(new Exception());

            var response = await _settingsController.GetSettingByKeyAsync(nonExistentSettingKey);
            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        public async Task GetAllSettingsExceptionTestAsync()
        {
            _settingsRepository.Setup(x => x.GetAllSettingsAsync())
                               .ThrowsAsync(new Exception());

            var response = await _settingsController.GetAllSettingsAsync();
            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        [DataRow(Roles.HYPERLINK_ADMINISTRATOR)]
        public async Task PatchSettingWhenHyperlinkAdminTestAsync(string role)
        {
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler,_getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role)
                })
            };

            _settingsRepository.Setup(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()))
                               .Verifiable();

            var response = await _settingsController.PatchSettingAsync(_settingKey, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(NoContentResult));

            _settingsRepository.Verify(x => x.PatchSettingAsync(_settingKey, "updatedValue"), Times.Once());
        }

        [TestMethod]
        public async Task PatchSettingNotFoundTestAsync()
        {
            var nonExistentSettingKey = SettingKey.UIUrl;
            _settingsRepository.Setup(x => x.PatchSettingAsync(nonExistentSettingKey, It.IsAny<string>()))
                               .ThrowsAsync(new KeyNotFoundException());

            var response = await _settingsController.PatchSettingAsync(nonExistentSettingKey, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetSupportEmailAddress_ReturnsSupportEmailAddress()
        {
            var expectedEmail = "support@example.com";
            var responseMock = new GetSupportEmailResponse { SupportEmailAddress = expectedEmail };
            var handlerMock = new Mock<IRequestHandler<GetSupportEmailRequest, GetSupportEmailResponse>>();
            handlerMock.Setup(h => h.ExecuteAsync(It.IsAny<GetSupportEmailRequest>()))
                .ReturnsAsync(responseMock);

            var controller = new SettingsController(
                _getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, handlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
        {
            new Claim(ClaimTypes.Name, "user@domain.com"),
            new Claim(ClaimTypes.Role, Roles.HYPERLINK_ADMINISTRATOR)
        })
            };

            var result = await controller.GetSupportEmailAddressAsync();

            var okResult = result as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.AreEqual(expectedEmail, okResult.Value);
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
