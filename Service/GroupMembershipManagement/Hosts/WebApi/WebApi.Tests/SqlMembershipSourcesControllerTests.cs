// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models;
using Moq;
using Repositories.Contracts;
using System.Net;
using System.Security.Claims;
using WebApi.Controllers.v1.Settings;
using WebApi.Controllers.v1.SqlMembershipSources;
using WebApi.Models;
using WebApi.Models.Responses;

namespace Services.Tests
{
    [TestClass]
    public class SqlMembershipSourcesControllerTests
    {
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSqlMembershipSourcesRepository> _databaseSqlMembershipSourcesRepository = null!;
        private Mock<IDataFactoryRepository> _dataFactoryRepository = null!;
        private Mock<ISqlMembershipRepository> _sqlMembershipRepository = null!;

        private GetDefaultSqlMembershipSourceHandler _getDefaultSqlMembershipSourceHandler = null!;
        private GetDefaultSqlMembershipSourceAttributesHandler _getDefaultSqlMembershipSourceAttributesHandler = null!;
        private GetDefaultSqlMembershipSourceAttributeValuesHandler _getDefaultSqlMembershipSourceAttributeValuesHandler = null!;
        private PatchDefaultSqlMembershipSourceCustomLabelHandler _patchDefaultSqlMembershipSourceCustomLabelHandler = null!;
        private PatchDefaultSqlMembershipSourceAttributesHandler _patchDefaultSqlMembershipSourceAttributesHandler = null!;
        private SqlMembershipSourcesController _sqlMembershipSourcesController = null!;

        [TestInitialize]
        public void Initialize()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _databaseSqlMembershipSourcesRepository = new Mock<IDatabaseSqlMembershipSourcesRepository>();
            _dataFactoryRepository = new Mock<IDataFactoryRepository>();
            _sqlMembershipRepository = new Mock<ISqlMembershipRepository>();

            _getDefaultSqlMembershipSourceHandler = new GetDefaultSqlMembershipSourceHandler(_loggingRepository.Object, _databaseSqlMembershipSourcesRepository.Object);
            _getDefaultSqlMembershipSourceAttributesHandler = new GetDefaultSqlMembershipSourceAttributesHandler(_loggingRepository.Object, _databaseSqlMembershipSourcesRepository.Object, _dataFactoryRepository.Object, _sqlMembershipRepository.Object);
            _getDefaultSqlMembershipSourceAttributeValuesHandler = new GetDefaultSqlMembershipSourceAttributeValuesHandler(_loggingRepository.Object, _dataFactoryRepository.Object, _sqlMembershipRepository.Object);
            _patchDefaultSqlMembershipSourceCustomLabelHandler = new PatchDefaultSqlMembershipSourceCustomLabelHandler(_loggingRepository.Object, _databaseSqlMembershipSourcesRepository.Object);
            _patchDefaultSqlMembershipSourceAttributesHandler = new PatchDefaultSqlMembershipSourceAttributesHandler(_loggingRepository.Object, _databaseSqlMembershipSourcesRepository.Object);

            _sqlMembershipSourcesController = new SqlMembershipSourcesController(_getDefaultSqlMembershipSourceHandler, _getDefaultSqlMembershipSourceAttributesHandler, _getDefaultSqlMembershipSourceAttributeValuesHandler, _patchDefaultSqlMembershipSourceCustomLabelHandler, _patchDefaultSqlMembershipSourceAttributesHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR)
                })
            };

            var _defaultSource = new SqlMembershipSource()
            {
                Name = "SqlMembership",
                CustomLabel = null
            };

            var _storedAttributeSettings = new List<SqlMembershipAttribute>()
            {
                new SqlMembershipAttribute
                {
                    Name = "Name1",
                    CustomLabel = "CustomLabel1",
                    Type = "nvarchar"
                }
            };

            _databaseSqlMembershipSourcesRepository.Setup(x => x.GetDefaultSourceAsync()).ReturnsAsync(() => _defaultSource);
            _databaseSqlMembershipSourcesRepository.Setup(x => x.GetDefaultSourceAttributesAsync()).ReturnsAsync(() => _storedAttributeSettings);
            _sqlMembershipRepository.Setup(x => x.GetColumnDetailsAsync(It.IsAny<string>())).ReturnsAsync(new List<(string Name, string Type)> { ("Name1", "nvarchar"), ("Name2", "int"), ("Name3_Code", "nvarchar") });
            _sqlMembershipRepository.Setup(x => x.GetAttributeValuesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string Code, string Description)> { ("Code1", "Description1"), ("Code2", "Description2"), ("Code3", "Description3") });
            _sqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _sqlMembershipRepository.Setup(x => x.CheckIfMappingsTableExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("RUN ID");
        }

        [TestMethod]
        public async Task SuccessfulGetHRFilterattributesTestAsync()
        {
            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributesAsync();

            Assert.IsNotNull(response);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var attributes = okResult.Value as List<SqlMembershipAttribute>;
            Assert.IsNotNull(attributes);

            Assert.AreEqual(attributes.Count, 3);
            Assert.AreEqual(attributes[0].Name, "Name1");
            Assert.AreEqual(attributes[0].CustomLabel, "CustomLabel1");
            Assert.AreEqual(attributes[1].CustomLabel, "");
        }

        [TestMethod]
        public async Task NoStoredCustomLabelsTestAsync()
        {
            _databaseSqlMembershipSourcesRepository.Setup(x => x.GetDefaultSourceAttributesAsync()).ReturnsAsync(() => null);

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributesAsync();

            Assert.IsNotNull(response);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var attributes = okResult.Value as List<SqlMembershipAttribute>;
            Assert.IsNotNull(attributes);

            Assert.AreEqual(attributes.Count, 3);
            Assert.AreEqual(attributes[0].Name, "Name1");
            Assert.AreEqual(attributes[0].CustomLabel, "");
            Assert.IsFalse(attributes[0].HasMapping);
        }

        [TestMethod]
        public async Task CodeColumnRenameTestAsync()
        {
            _databaseSqlMembershipSourcesRepository.Setup(x => x.GetDefaultSourceAttributesAsync()).ReturnsAsync(() => null);

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributesAsync();

            Assert.IsNotNull(response);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var attributes = okResult.Value as List<SqlMembershipAttribute>;
            Assert.IsNotNull(attributes);

            Assert.AreEqual(attributes.Count, 3);
            Assert.AreEqual(attributes[2].Name, "Name3");
            Assert.AreEqual(attributes[2].CustomLabel, "");
            Assert.IsTrue(attributes[2].HasMapping);
        }

        [TestMethod]
        public async Task TestRemovalOfStaleStoredSettingsAsync()
        {
            var storedAttributes = new List<SqlMembershipAttribute>()
            {
                new SqlMembershipAttribute
                {
                    Name = "Name1",
                    CustomLabel = "CustomLabel1",
                    Type = "nvarchar", 
                    HasMapping = false
                },
                new SqlMembershipAttribute
                {
                    Name = "RemovedAttribute1",
                    CustomLabel = "RemovedCustomLabel1",
                    Type = "nvarchar",
                    HasMapping = false
                }
            };

            _databaseSqlMembershipSourcesRepository.Setup(x => x.GetDefaultSourceAttributesAsync()).ReturnsAsync(() => storedAttributes);

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributesAsync();

            Assert.IsNotNull(response);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var attributes = okResult.Value as List<SqlMembershipAttribute>;
            Assert.IsNotNull(attributes);

            Assert.AreEqual(attributes.Count, 3);
            Assert.AreEqual(attributes[0].Name, "Name1");
            Assert.AreEqual(attributes[0].CustomLabel, "CustomLabel1");
            Assert.IsFalse(attributes[0].HasMapping);
            Assert.AreEqual(attributes[1].CustomLabel, "");

            _databaseSqlMembershipSourcesRepository.Verify(x => x.UpdateDefaultSourceAttributesAsync(It.Is<List<SqlMembershipAttribute>>(list => !list.Any(a => a.Name == "RemovedAttribute1"))), Times.Once());
        }

        [TestMethod]
        public async Task TestExceptionWithinHandlerAsync()
        {
            _sqlMembershipRepository.Setup(x => x.GetColumnDetailsAsync(It.IsAny<string>())).Throws(new Exception("Unexpected exception triggered for testing"));

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributesAsync();

            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        public async Task GetDefaultSourceTest()
        {
            var response = await _sqlMembershipSourcesController.GetDefaultSourceAsync();
            Assert.IsNotNull(response);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var setting = okResult.Value as SqlMembershipSource;
            Assert.IsNotNull(setting);
            Assert.IsNotNull(setting.Name);
        }

        [TestMethod]
        public async Task PatchDefaultSourceCustomLabelWhenCustomMembershipProviderAdminTestAsync()
        {
            _sqlMembershipSourcesController.ControllerContext = CreateControllerContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR)
            });

            var response = await _sqlMembershipSourcesController.PatchDefaultSourceCustomLabelAsync("NewCustomLabel");

            Assert.IsInstanceOfType(response, typeof(NoContentResult));

            _databaseSqlMembershipSourcesRepository.Verify(x => x.UpdateDefaultSourceCustomLabelAsync("NewCustomLabel"), Times.Once());
        }

        [TestMethod]
        public async Task PatchDefaultSourceAttributesWhenCustomMembershipProviderAdminTestAsync()
        {
            _sqlMembershipSourcesController.ControllerContext = CreateControllerContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR)
            });

            var attributes = new List<SqlMembershipAttribute>()
            {
                new SqlMembershipAttribute
                {
                    Name = "Name4",
                    CustomLabel = "CustomLabel4"
                }
            };

            var response = await _sqlMembershipSourcesController.PatchDefaultSourceAttributesAsync(attributes);

            Assert.IsInstanceOfType(response, typeof(NoContentResult));

            _databaseSqlMembershipSourcesRepository.Verify(x => x.UpdateDefaultSourceAttributesAsync(It.Is<List<SqlMembershipAttribute>>(list => list[0].Name == "Name4" && list[0].CustomLabel == "CustomLabel4")), Times.Once());
        }

        [TestMethod]
        public async Task SuccessfulGetHRFilterattributeValuesTestAsync()
        {
            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributeValuesAsync("attribute");
            Assert.IsNotNull(response);
            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var attributeValues = okResult.Value as GetAttributeValuesModel;
            Assert.IsNotNull(attributeValues);
            Assert.AreEqual(attributeValues.Count, 3);
            Assert.AreEqual(attributeValues[0].Code, "Code1");
            Assert.AreEqual(attributeValues[0].Description, "Description1");
        }

        [TestMethod]
        public async Task ExceptionGetHRFilterattributeValuesTestAsync()
        {
            _sqlMembershipRepository.Setup(x => x.CheckIfMappingsTableExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sqlMembershipRepository.Setup(x => x.GetAttributeValuesAsync(It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception("Unexpected exception triggered for testing"));

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributeValuesAsync("attribute");

            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                            It.Is<LogMessage>(m => m.Message.StartsWith("Unable to retrieve Sql Filter Attribute Values")),
                                            It.IsAny<VerbosityLevel>(),
                                            It.IsAny<string>(),
                                            It.IsAny<string>()), Times.Once());
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
