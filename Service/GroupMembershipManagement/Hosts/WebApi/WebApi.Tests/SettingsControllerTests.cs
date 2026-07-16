// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Models;
using Repositories.Contracts;
using WebApi.Controllers.v1.Settings;
using Microsoft.AspNetCore.Http;
using SettingDTO = WebApi.Models.DTOs.Setting;
using AlertBannerConfigDto = WebApi.Models.DTOs.AlertBannerConfigDto;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Tests
{
    [TestClass]
    public class SettingsControllerTests
    {
        private HttpContext _context = null!;
        private Setting _settingEntity = null!;
        private SettingsController _settingsController = null!;
        private List<Setting> _settings = null!;
        private Mock<IDatabaseSettingsRepository> _settingsRepository = null!;
        private IConfiguration _configuration = null!;
        private GetAllSettingsHandler _getAllSettingsHandler = null!;
        private GetSettingHandler _getSettingHandler = null!;
        private PatchSettingHandler _patchSettingHandler = null!;
        private SettingKey _settingKey;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;
        private Mock<IRequestHandler<GetSupportEmailRequest, GetSupportEmailResponse>> _getSupportEmailHandlerMock = null!;
        private IRequestHandler<GetSupportEmailRequest, GetSupportEmailResponse> _getSupportEmailHandler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _settingsRepository = new Mock<IDatabaseSettingsRepository>();
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ConfigurationKeyNames.RunHistoryOpenViewingAndUnifiedTab] = "false"
                })
                .Build();
            _getSupportEmailHandlerMock = new Mock<IRequestHandler<GetSupportEmailRequest, GetSupportEmailResponse>>();
            _getSupportEmailHandlerMock.Setup(h => h.ExecuteAsync(It.IsAny<GetSupportEmailRequest>()))
                                       .ReturnsAsync(new GetSupportEmailResponse { SupportEmailAddress = "support@example.com" });

            _getSupportEmailHandler = _getSupportEmailHandlerMock.Object;
            _getAllSettingsHandler = new GetAllSettingsHandler(
                NullLogger<GetAllSettingsHandler>.Instance,
                _settingsRepository.Object,
                _configuration);
            _getSettingHandler = new GetSettingHandler(NullLogger<GetSettingHandler>.Instance, _settingsRepository.Object);
            _patchSettingHandler = new PatchSettingHandler(NullLogger<PatchSettingHandler>.Instance, _settingsRepository.Object);
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
        public async Task GetSettingByKeyWhenAISettingAndUserLacksAISettingsRoleReturnsForbidTestAsync()
        {
            var aiSettingKey = SettingKey.CopilotInstructions;

            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.HYPERLINK_ADMINISTRATOR)
                })
            };

            var response = await _settingsController.GetSettingByKeyAsync(aiSettingKey);

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
            _settingsRepository.Verify(x => x.GetSettingByKeyAsync(It.IsAny<SettingKey>()), Times.Never());
        }

        [TestMethod]
        public async Task GetSettingByKeyWhenAISettingAndUserHasAISettingsRoleReturnsOkTestAsync()
        {
            var aiSettingKey = SettingKey.CopilotInstructions;
            var aiSettingEntity = new Setting { SettingKey = aiSettingKey, SettingValue = "ai prompt" };
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(aiSettingKey)).ReturnsAsync(aiSettingEntity);

            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.AI_SETTINGS_ADMINISTRATOR)
                })
            };

            var response = await _settingsController.GetSettingByKeyAsync(aiSettingKey);

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
            _settingsRepository.Verify(x => x.GetSettingByKeyAsync(aiSettingKey), Times.Once());
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
            Assert.AreEqual(_settings.Count + 1, settingsResult.Count);
            var phase2Setting = settingsResult.Single(
                setting => setting.SettingKey == SettingKey.RunHistoryOpenViewingAndUnifiedTab);
            Assert.AreEqual("false", phase2Setting.SettingValue);
        }

        [TestMethod]
        public async Task GetAllSettingsWhenUserLacksAISettingsRoleFiltersOutAISettingsTestAsync()
        {
            var mixedSettings = new List<Setting>
            {
                new Setting { SettingKey = SettingKey.DashboardUrl, SettingValue = "dashboard" },
                new Setting { SettingKey = SettingKey.CopilotInstructions, SettingValue = "sensitive prompt" },
                new Setting { SettingKey = SettingKey.IsAICopilotEnabled, SettingValue = "true" }
            };
            _settingsRepository.Setup(x => x.GetAllSettingsAsync()).ReturnsAsync(mixedSettings);

            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.HYPERLINK_ADMINISTRATOR)
                })
            };

            var response = await _settingsController.GetAllSettingsAsync();

            var okResult = response as OkObjectResult;
            Assert.IsNotNull(okResult);
            var settingsResult = okResult.Value as List<SettingDTO>;
            Assert.IsNotNull(settingsResult);
            Assert.AreEqual(2, settingsResult.Count);
            Assert.AreEqual(SettingKey.DashboardUrl, settingsResult[0].SettingKey);
            Assert.IsTrue(settingsResult.Any(
                setting => setting.SettingKey == SettingKey.RunHistoryOpenViewingAndUnifiedTab));
        }

        [TestMethod]
        public async Task GetAllSettingsWhenUserHasAISettingsRoleReturnsAISettingsTestAsync()
        {
            var mixedSettings = new List<Setting>
            {
                new Setting { SettingKey = SettingKey.DashboardUrl, SettingValue = "dashboard" },
                new Setting { SettingKey = SettingKey.CopilotInstructions, SettingValue = "sensitive prompt" },
                new Setting { SettingKey = SettingKey.IsAICopilotEnabled, SettingValue = "true" }
            };
            _settingsRepository.Setup(x => x.GetAllSettingsAsync()).ReturnsAsync(mixedSettings);

            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.AI_SETTINGS_ADMINISTRATOR)
                })
            };

            var response = await _settingsController.GetAllSettingsAsync();

            var okResult = response as OkObjectResult;
            Assert.IsNotNull(okResult);
            var settingsResult = okResult.Value as List<SettingDTO>;
            Assert.IsNotNull(settingsResult);
            Assert.AreEqual(4, settingsResult.Count);
            Assert.IsTrue(settingsResult.Any(setting => setting.SettingKey == SettingKey.CopilotInstructions));
            Assert.IsTrue(settingsResult.Any(setting => setting.SettingKey == SettingKey.IsAICopilotEnabled));
            Assert.IsTrue(settingsResult.Any(
                setting => setting.SettingKey == SettingKey.RunHistoryOpenViewingAndUnifiedTab));
        }

        [TestMethod]
        public async Task GetAllSettingsWhenRunHistoryPhase2EnabledReturnsEnabledValueTestAsync()
        {
            _configuration[ConfigurationKeyNames.RunHistoryOpenViewingAndUnifiedTab] = "true";

            var response = await _settingsController.GetAllSettingsAsync();

            var okResult = response as OkObjectResult;
            Assert.IsNotNull(okResult);
            var settingsResult = okResult.Value as List<SettingDTO>;
            Assert.IsNotNull(settingsResult);
            var phase2Setting = settingsResult.Single(
                setting => setting.SettingKey == SettingKey.RunHistoryOpenViewingAndUnifiedTab);
            Assert.AreEqual("true", phase2Setting.SettingValue);
        }

        [TestMethod]
        public async Task PatchRunHistoryPhase2SettingReturnsBadRequestTestAsync()
        {
            var response = await _settingsController.PatchSettingAsync(
                SettingKey.RunHistoryOpenViewingAndUnifiedTab,
                "true");

            Assert.IsInstanceOfType(response, typeof(BadRequestObjectResult));
            _settingsRepository.Verify(
                x => x.PatchSettingAsync(
                    SettingKey.RunHistoryOpenViewingAndUnifiedTab,
                    It.IsAny<string>()),
                Times.Never);
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
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
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
        public async Task PatchAISettingWhenUserLacksAISettingsRoleReturnsForbidTestAsync()
        {
            var response = await _settingsController.PatchSettingAsync(SettingKey.CopilotInstructions, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task PatchAISettingWhenUserHasAISettingsRoleReturnsNoContentTestAsync()
        {
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.AI_SETTINGS_ADMINISTRATOR)
                })
            };

            _settingsRepository.Setup(x => x.PatchSettingAsync(SettingKey.CopilotInstructions, "updatedValue"))
                               .Returns(Task.CompletedTask);

            var response = await _settingsController.PatchSettingAsync(SettingKey.CopilotInstructions, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(NoContentResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(SettingKey.CopilotInstructions, "updatedValue"), Times.Once());
        }

        [TestMethod]
        public async Task PatchNonAISettingWhenUserHasGeneralSettingsRoleReturnsNoContentTestAsync()
        {
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.GENERAL_SETTINGS_ADMINISTRATOR)
                })
            };

            _settingsRepository.Setup(x => x.PatchSettingAsync(SettingKey.CreateGroupFeatureEnabled, "true"))
                               .Returns(Task.CompletedTask);

            var response = await _settingsController.PatchSettingAsync(SettingKey.CreateGroupFeatureEnabled, "true");

            Assert.IsInstanceOfType(response, typeof(NoContentResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(SettingKey.CreateGroupFeatureEnabled, "true"), Times.Once());
        }

        [TestMethod]
        public async Task PatchAISettingWhenUserHasGeneralSettingsRoleReturnsForbidTestAsync()
        {
            _settingsController = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.GENERAL_SETTINGS_ADMINISTRATOR)
                })
            };

            var response = await _settingsController.PatchSettingAsync(SettingKey.CopilotInstructions, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()), Times.Never());
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
        public async Task PatchSettingExceptionTestAsync()
        {
            _settingsRepository.Setup(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()))
                               .ThrowsAsync(new Exception());

            var response = await _settingsController.PatchSettingAsync(_settingKey, "updatedValue");

            Assert.IsInstanceOfType(response, typeof(StatusCodeResult));

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        public async Task GetSupportEmailAddress_ReturnsSupportEmailAddress()
        {
            var result = await _settingsController.GetSupportEmailAddressAsync();

            var okResult = result as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.AreEqual("support@example.com", okResult.Value);
        }

        // ---------------------------------------------------------------------
        // Alert Banner endpoints (US1 PATCH, US2 GET, US3 edit)
        // ---------------------------------------------------------------------

        private static readonly System.Text.Json.JsonSerializerOptions AlertBannerJsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        private SettingsController CreateGeneralSettingsAdminController()
        {
            return new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "admin@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.GENERAL_SETTINGS_ADMINISTRATOR)
                })
            };
        }

        private static AlertBannerConfigDto CreateValidAlertConfig()
        {
            return new AlertBannerConfigDto
            {
                Message = "Scheduled maintenance tonight.",
                IsEnabled = true,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
                LinkUrl = "https://status.example.com",
                LinkText = "View details"
            };
        }

        [TestMethod]
        public async Task PatchAlertBanner_ValidConfig_PersistsAndReturnsOk()
        {
            var controller = CreateGeneralSettingsAdminController();
            string? persistedJson = null;
            _settingsRepository.Setup(x => x.PatchSettingAsync(SettingKey.AlertBannerConfig, It.IsAny<string>()))
                               .Callback<SettingKey, string>((_, value) => persistedJson = value)
                               .Returns(Task.CompletedTask);

            var config = CreateValidAlertConfig();
            var response = await controller.PatchAlertBannerAsync(config);

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(SettingKey.AlertBannerConfig, It.IsAny<string>()), Times.Once());
            Assert.IsNotNull(persistedJson);
            var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AlertBannerConfigDto>(persistedJson!, AlertBannerJsonOptions);
            Assert.IsNotNull(roundTrip);
            Assert.AreEqual(config.Message, roundTrip!.Message);
            Assert.IsTrue(roundTrip.IsEnabled);
        }

        [TestMethod]
        public async Task PatchAlertBanner_EmptyMessageWhenEnabled_ReturnsBadRequest()
        {
            var controller = CreateGeneralSettingsAdminController();
            var config = CreateValidAlertConfig();
            config.Message = "   ";

            var response = await controller.PatchAlertBannerAsync(config);

            Assert.IsInstanceOfType(response, typeof(BadRequestObjectResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task PatchAlertBanner_MessageTooLong_ReturnsBadRequest()
        {
            var controller = CreateGeneralSettingsAdminController();
            var config = CreateValidAlertConfig();
            config.Message = new string('a', 251);

            var response = await controller.PatchAlertBannerAsync(config);

            Assert.IsInstanceOfType(response, typeof(BadRequestObjectResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task PatchAlertBanner_StartDateNotBeforeEndDate_ReturnsBadRequest()
        {
            var controller = CreateGeneralSettingsAdminController();
            var config = CreateValidAlertConfig();
            config.StartDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
            config.EndDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

            var response = await controller.PatchAlertBannerAsync(config);

            Assert.IsInstanceOfType(response, typeof(BadRequestObjectResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task PatchAlertBanner_NonHttpsLinkUrl_ReturnsBadRequest()
        {
            var controller = CreateGeneralSettingsAdminController();
            var config = CreateValidAlertConfig();
            config.LinkUrl = "http://insecure.example.com";

            var response = await controller.PatchAlertBannerAsync(config);

            Assert.IsInstanceOfType(response, typeof(BadRequestObjectResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task PatchAlertBanner_NonAdmin_ReturnsForbid()
        {
            // Default controller has only HYPERLINK_ADMINISTRATOR role.
            var response = await _settingsController.PatchAlertBannerAsync(CreateValidAlertConfig());

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
            _settingsRepository.Verify(x => x.PatchSettingAsync(It.IsAny<SettingKey>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task GetAlertBanner_ReturnsStoredConfig()
        {
            var stored = CreateValidAlertConfig();
            var json = System.Text.Json.JsonSerializer.Serialize(stored, AlertBannerJsonOptions);
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.AlertBannerConfig))
                               .ReturnsAsync(new Setting { SettingKey = SettingKey.AlertBannerConfig, SettingValue = json });

            var response = await _settingsController.GetAlertBannerAsync();

            var okResult = response as OkObjectResult;
            Assert.IsNotNull(okResult);
            var config = okResult!.Value as AlertBannerConfigDto;
            Assert.IsNotNull(config);
            Assert.AreEqual(stored.Message, config!.Message);
            Assert.IsTrue(config.IsEnabled);
            Assert.AreEqual("https://status.example.com", config.LinkUrl);
        }

        [TestMethod]
        public async Task GetAlertBanner_NoRowExists_ReturnsDefaultDisabledConfig()
        {
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.AlertBannerConfig))
                               .ReturnsAsync(() => null);

            var response = await _settingsController.GetAlertBannerAsync();

            var okResult = response as OkObjectResult;
            Assert.IsNotNull(okResult);
            var config = okResult!.Value as AlertBannerConfigDto;
            Assert.IsNotNull(config);
            Assert.AreEqual(string.Empty, config!.Message);
            Assert.IsFalse(config.IsEnabled);
        }

        [TestMethod]
        public async Task GetAlertBanner_AccessibleToAnyAuthenticatedUser()
        {
            // Controller with a non-admin role should still be able to read the banner.
            var controller = new SettingsController(_getSettingHandler, _getAllSettingsHandler, _patchSettingHandler, _getSupportEmailHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_READER)
                })
            };
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.AlertBannerConfig))
                               .ReturnsAsync(() => null);

            var response = await controller.GetAlertBannerAsync();

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
        }

        [TestMethod]
        public async Task AlertBanner_EditExistingConfig_PersistsUpdatedValuesAndGetReturnsThem()
        {
            var controller = CreateGeneralSettingsAdminController();
            string? persistedJson = null;
            _settingsRepository.Setup(x => x.PatchSettingAsync(SettingKey.AlertBannerConfig, It.IsAny<string>()))
                               .Callback<SettingKey, string>((_, value) => persistedJson = value)
                               .Returns(Task.CompletedTask);

            // Edit: change message and move end date into the past.
            var edited = CreateValidAlertConfig();
            edited.Message = "Updated maintenance window.";
            edited.EndDate = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);

            var patchResponse = await controller.PatchAlertBannerAsync(edited);
            Assert.IsInstanceOfType(patchResponse, typeof(OkObjectResult));
            Assert.IsNotNull(persistedJson);

            // GET now returns the persisted (updated) values.
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.AlertBannerConfig))
                               .ReturnsAsync(new Setting { SettingKey = SettingKey.AlertBannerConfig, SettingValue = persistedJson! });

            var getResponse = await controller.GetAlertBannerAsync();
            var okResult = getResponse as OkObjectResult;
            Assert.IsNotNull(okResult);
            var config = okResult!.Value as AlertBannerConfigDto;
            Assert.IsNotNull(config);
            Assert.AreEqual("Updated maintenance window.", config!.Message);
            Assert.AreEqual(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), config.EndDate);
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
