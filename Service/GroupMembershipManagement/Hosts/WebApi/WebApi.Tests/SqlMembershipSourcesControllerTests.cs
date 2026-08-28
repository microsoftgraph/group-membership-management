// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models;
using Moq;
using Repositories.Contracts;
using Services.Messages.Responses;
using System.Net;
using System.Security.Claims;
using WebApi.Controllers.v1.Settings;
using WebApi.Controllers.v1.SqlMembershipSources;
using WebApi.Models;
using WebApi.Models.Responses;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Tests
{
    [TestClass]
    public class SqlMembershipSourcesControllerTests
    {
        private Mock<IDatabaseSqlMembershipSourcesRepository> _databaseSqlMembershipSourcesRepository = null!;
        private Mock<IDataFactoryRepository> _dataFactoryRepository = null!;
        private Mock<ISqlMembershipRepository> _sqlMembershipRepository = null!;

        private GetDefaultSqlMembershipSourceHandler _getDefaultSqlMembershipSourceHandler = null!;
        private GetDefaultSqlMembershipSourceAttributesHandler _getDefaultSqlMembershipSourceAttributesHandler = null!;
        private GetDefaultSqlMembershipSourceAttributeMappingsHandler _getDefaultSqlMembershipSourceAttributeMappingsHandler = null!;
        private ResolveDefaultSqlMembershipSourceAttributeMappingsHandler _resolveDefaultSqlMembershipSourceAttributeMappingsHandler = null!;
        private GetDefaultSqlMembershipSourceAttributeValuesHandler _getDefaultSqlMembershipSourceAttributeValuesHandler = null!;
        private PatchDefaultSqlMembershipSourceCustomLabelHandler _patchDefaultSqlMembershipSourceCustomLabelHandler = null!;
        private PatchDefaultSqlMembershipSourceAttributesHandler _patchDefaultSqlMembershipSourceAttributesHandler = null!;
        private GetSqlValidationHandler _getSqlValidationHandler = null!;
        private SqlMembershipSourcesController _sqlMembershipSourcesController = null!;

        [TestInitialize]
        public void Initialize()
        {
            _databaseSqlMembershipSourcesRepository = new Mock<IDatabaseSqlMembershipSourcesRepository>();
            _dataFactoryRepository = new Mock<IDataFactoryRepository>();
            _sqlMembershipRepository = new Mock<ISqlMembershipRepository>();

            _getDefaultSqlMembershipSourceHandler = new GetDefaultSqlMembershipSourceHandler(NullLogger<GetDefaultSqlMembershipSourceHandler>.Instance, _databaseSqlMembershipSourcesRepository.Object);
            _getDefaultSqlMembershipSourceAttributesHandler = new GetDefaultSqlMembershipSourceAttributesHandler(NullLogger<GetDefaultSqlMembershipSourceAttributesHandler>.Instance, _databaseSqlMembershipSourcesRepository.Object, _dataFactoryRepository.Object, _sqlMembershipRepository.Object);
            _getDefaultSqlMembershipSourceAttributeMappingsHandler = new GetDefaultSqlMembershipSourceAttributeMappingsHandler(NullLogger<GetDefaultSqlMembershipSourceAttributeMappingsHandler>.Instance, _dataFactoryRepository.Object, _sqlMembershipRepository.Object);
            _resolveDefaultSqlMembershipSourceAttributeMappingsHandler = new ResolveDefaultSqlMembershipSourceAttributeMappingsHandler(NullLogger<ResolveDefaultSqlMembershipSourceAttributeMappingsHandler>.Instance, _dataFactoryRepository.Object, _sqlMembershipRepository.Object);
            _getDefaultSqlMembershipSourceAttributeValuesHandler = new GetDefaultSqlMembershipSourceAttributeValuesHandler(NullLogger<GetDefaultSqlMembershipSourceAttributeValuesHandler>.Instance, _dataFactoryRepository.Object, _sqlMembershipRepository.Object);
            _patchDefaultSqlMembershipSourceCustomLabelHandler = new PatchDefaultSqlMembershipSourceCustomLabelHandler(NullLogger<PatchDefaultSqlMembershipSourceCustomLabelHandler>.Instance, _databaseSqlMembershipSourcesRepository.Object);
            _patchDefaultSqlMembershipSourceAttributesHandler = new PatchDefaultSqlMembershipSourceAttributesHandler(NullLogger<PatchDefaultSqlMembershipSourceAttributesHandler>.Instance, _databaseSqlMembershipSourcesRepository.Object);
            _getSqlValidationHandler = new GetSqlValidationHandler(NullLogger<GetSqlValidationHandler>.Instance, _sqlMembershipRepository.Object, _dataFactoryRepository.Object);

            _sqlMembershipSourcesController = new SqlMembershipSourcesController(_getDefaultSqlMembershipSourceHandler,
                _getDefaultSqlMembershipSourceAttributesHandler,
                _getDefaultSqlMembershipSourceAttributeMappingsHandler,
                _resolveDefaultSqlMembershipSourceAttributeMappingsHandler,
                _getDefaultSqlMembershipSourceAttributeValuesHandler,
                _patchDefaultSqlMembershipSourceCustomLabelHandler,
                _patchDefaultSqlMembershipSourceAttributesHandler,
                _getSqlValidationHandler)
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
            _sqlMembershipRepository.Setup(x => x.GetAttributeMappingsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string Code, string Description)> { ("Code1", "Description1"), ("Code2", "Description2"), ("Code3", "Description3") });
            _sqlMembershipRepository.Setup(x => x.GetAttributeMappingsPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync((Mappings: new List<(string Code, string Description)> { ("Code1", "Description1"), ("Code2", "Description2"), ("Code3", "Description3") }, HasMore: false));
            _sqlMembershipRepository.Setup(x => x.GetAttributeMappingsByCodesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new List<(string Code, string Description)> { ("Code2", "Description2") });
            _sqlMembershipRepository.Setup(x => x.GetAttributeValuesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>())).ReturnsAsync(new List<string> { "Value1", "Value2", "Value3" });
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
        public async Task TestExceptionWhenTableDoesNotExistAsync()
        {
            // Simulate the scenario where the table doesn't exist by returning 0 columns
            _sqlMembershipRepository.Setup(x => x.GetColumnDetailsAsync(It.IsAny<string>())).ReturnsAsync(new List<(string Name, string Type)>());

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributesAsync();

            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);

            // Verify that the database was not updated (destructive operation prevented)
            _databaseSqlMembershipSourcesRepository.Verify(x => x.UpdateDefaultSourceAttributesAsync(It.IsAny<List<SqlMembershipAttribute>>()), Times.Never());
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
        public async Task GetDefaultSourceAttributesHydratesIsSensitiveTestAsync()
        {
            var storedAttributes = new List<SqlMembershipAttribute>()
            {
                new SqlMembershipAttribute
                {
                    Name = "Name1",
                    CustomLabel = "CustomLabel1",
                    Type = "nvarchar",
                    IsSensitive = true
                }
            };

            _databaseSqlMembershipSourcesRepository.Setup(x => x.GetDefaultSourceAttributesAsync()).ReturnsAsync(() => storedAttributes);

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributesAsync();

            var okResult = response as OkObjectResult;
            Assert.IsNotNull(okResult);

            var attributes = okResult.Value as List<SqlMembershipAttribute>;
            Assert.IsNotNull(attributes);

            // Name1 has a stored IsSensitive = true and should be hydrated.
            Assert.IsTrue(attributes.First(a => a.Name == "Name1").IsSensitive);
            // Name2 has no stored setting (legacy row without the property) and should default to false.
            Assert.IsFalse(attributes.First(a => a.Name == "Name2").IsSensitive);
        }

        [TestMethod]
        public async Task PatchDefaultSourceAttributesPersistsIsSensitiveTestAsync()
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
                    CustomLabel = "CustomLabel4",
                    Enabled = false,
                    IsSensitive = true
                }
            };

            var response = await _sqlMembershipSourcesController.PatchDefaultSourceAttributesAsync(attributes);

            Assert.IsInstanceOfType(response, typeof(NoContentResult));

            // Sensitive is persisted independently of Enabled through the existing PATCH flow.
            _databaseSqlMembershipSourcesRepository.Verify(x => x.UpdateDefaultSourceAttributesAsync(
                It.Is<List<SqlMembershipAttribute>>(list => list[0].Name == "Name4" && list[0].IsSensitive && !list[0].Enabled)), Times.Once());
        }

        [TestMethod]
        public async Task SuccessfulGetHRFilterattributeMappingsTestAsync()
        {
            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributeMappingsAsync("attribute");
            Assert.IsNotNull(response);
            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var attributeMappings = okResult.Value as GetAttributeMappingsModel;
            Assert.IsNotNull(attributeMappings);
            Assert.AreEqual(attributeMappings.Mappings.Count, 3);
            Assert.AreEqual(attributeMappings.Mappings[0].Code, "Code1");
            Assert.AreEqual(attributeMappings.Mappings[0].Description, "Description1");
            Assert.IsFalse(attributeMappings.HasMore);
        }

        [TestMethod]
        public async Task GetHRFilterattributeMappingsReportsHasMoreTestAsync()
        {
            _sqlMembershipRepository.Setup(x => x.GetAttributeMappingsPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync((Mappings: new List<(string Code, string Description)> { ("Code1", "Description1") }, HasMore: true));

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributeMappingsAsync("attribute");

            var attributeMappings = (response as OkObjectResult)?.Value as GetAttributeMappingsModel;

            Assert.IsNotNull(attributeMappings);
            Assert.AreEqual(1, attributeMappings.Mappings.Count);
            Assert.IsTrue(attributeMappings.HasMore);
        }

        [TestMethod]
        public async Task GetHRFilterattributeMappingsPassesSearchAndTopTestAsync()
        {
            await _sqlMembershipSourcesController.GetDefaultSourceAttributeMappingsAsync("attribute", "Man", 25);

            _sqlMembershipRepository.Verify(x => x.GetAttributeMappingsPageAsync("attribute", It.IsAny<string>(), "Man", 25), Times.Once());
        }

        [TestMethod]
        public async Task SuccessfulResolveHRFilterattributeMappingsTestAsync()
        {
            var response = await _sqlMembershipSourcesController.ResolveDefaultSourceAttributeMappingsAsync("attribute", new List<string> { "Code2" });

            Assert.IsNotNull(response);
            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);

            var mappings = okResult.Value as List<global::WebApi.Models.DTOs.SqlMembershipAttributeMapping>;
            Assert.IsNotNull(mappings);
            Assert.AreEqual(1, mappings.Count);
            Assert.AreEqual("Code2", mappings[0].Code);
            Assert.AreEqual("Description2", mappings[0].Description);
        }

        [TestMethod]
        public async Task ResolveHRFilterattributeMappingsWithNoCodesSkipsLookupTestAsync()
        {
            var response = await _sqlMembershipSourcesController.ResolveDefaultSourceAttributeMappingsAsync("attribute", new List<string>());

            var mappings = (response as OkObjectResult)?.Value as List<global::WebApi.Models.DTOs.SqlMembershipAttributeMapping>;

            Assert.IsNotNull(mappings);
            Assert.AreEqual(0, mappings.Count);
            _sqlMembershipRepository.Verify(x => x.GetAttributeMappingsByCodesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [TestMethod]
        public async Task ExceptionResolveHRFilterattributeMappingsTestAsync()
        {
            _sqlMembershipRepository.Setup(x => x.GetAttributeMappingsByCodesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                .Throws(new Exception("Unexpected exception triggered for testing"));

            var response = await _sqlMembershipSourcesController.ResolveDefaultSourceAttributeMappingsAsync("attribute", new List<string> { "Code2" });

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        public async Task ExceptionGetHRFilterattributeMappingsTestAsync()
        {
            _sqlMembershipRepository.Setup(x => x.CheckIfMappingsTableExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sqlMembershipRepository.Setup(x => x.GetAttributeMappingsPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Throws(new Exception("Unexpected exception triggered for testing"));

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributeMappingsAsync("attribute");

            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        public async Task SuccessfulGetHRFilterAttributeValuesTestAsync()
        {
            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributeValuesAsync("attribute", false);
            Assert.IsNotNull(response);
            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var attributeValues = okResult.Value as List<string>;
            Assert.IsNotNull(attributeValues);
            Assert.AreEqual(attributeValues.Count, 3);
            Assert.AreEqual(attributeValues[0], "Value1");
        }

        [TestMethod]
        public async Task ExceptionGetHRFilterAttributeValuesTestAsync()
        {
            _sqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sqlMembershipRepository.Setup(x => x.GetAttributeValuesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>())).Throws(new Exception("Unexpected exception triggered for testing"));

            var response = await _sqlMembershipSourcesController.GetDefaultSourceAttributeValuesAsync("attribute", false);

            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        public async Task SuccessfulValidateNoFiltersAsync()
        {
            var sqlFilters = new Dictionary<int, string>();
            var response = await _sqlMembershipSourcesController.ValidateSqlFilterAsync(sqlFilters);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var getSqlValidationResponse = okResult.Value as GetSqlValidationResponse;
            Assert.IsNotNull(getSqlValidationResponse);
            Assert.IsTrue(getSqlValidationResponse.IsValid);
            Assert.IsNull(getSqlValidationResponse.Errors);
        }

        [TestMethod]
        public async Task SuccessfulValidateGoodFiltersAsync()
        {
            _sqlMembershipRepository.Setup(x => x.ValidateFiltersAsync(It.IsAny<Dictionary<int, string>>(), "RUN ID")).ReturnsAsync(new Dictionary<int, string>());

            var sqlFilters = new Dictionary<int, string>() { { 0, "filter1" }, { 1, "filter2" } };
            var response = await _sqlMembershipSourcesController.ValidateSqlFilterAsync(sqlFilters);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var getSqlValidationResponse = okResult.Value as GetSqlValidationResponse;
            Assert.IsNotNull(getSqlValidationResponse);
            Assert.IsTrue(getSqlValidationResponse.IsValid);
            Assert.IsNull(getSqlValidationResponse.Errors);
        }

        [TestMethod]
        public async Task SuccessfulValidateBadFiltersAsync()
        {
            var exceptionsDictionary = new Dictionary<int, string>() { { 0, "Sql Error"} };
            _sqlMembershipRepository.Setup(x => x.ValidateFiltersAsync(It.IsAny<Dictionary<int, string>>(), "RUN ID")).ReturnsAsync(exceptionsDictionary);

            var sqlFilters = new Dictionary<int, string>() { { 0, "filter1" }, { 1, "filter2" } };
            var response = await _sqlMembershipSourcesController.ValidateSqlFilterAsync(sqlFilters);

            var okResult = response as OkObjectResult;

            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);

            var getSqlValidationResponse = okResult.Value as GetSqlValidationResponse;
            Assert.IsNotNull(getSqlValidationResponse);
            Assert.IsFalse(getSqlValidationResponse.IsValid);
            Assert.AreEqual(getSqlValidationResponse.Errors!.Count, 1);
        }

        [TestMethod]
        public async Task ExceptionValidateFiltersAsync()
        {
            _sqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sqlMembershipRepository.Setup(x => x.GetAttributeValuesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>())).Throws(new Exception("Unexpected exception triggered for testing"));

            var sqlFilters = new Dictionary<int, string>() { { 0, "any filter" } };

            var response = await _sqlMembershipSourcesController.ValidateSqlFilterAsync(sqlFilters);

            Assert.IsNotNull(response);

            var internalServerErrorResponse = response as StatusCodeResult;

            Assert.IsNotNull(internalServerErrorResponse);
            Assert.AreEqual(internalServerErrorResponse.StatusCode, (int)HttpStatusCode.InternalServerError);
        }

        [TestMethod]
        public void AttributeValuesReadAcceptsBothCustomSourceRoles()
        {
            // FR-017: this read is write-gated today and deliberately widens to accept the read role.
            var roles = GetAuthorizeRoles(nameof(SqlMembershipSourcesController.GetDefaultSourceAttributeValuesAsync));

            CollectionAssert.Contains(roles, Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR);
            CollectionAssert.Contains(roles, Roles.CUSTOM_MEMBERSHIP_PROVIDER_READER);
        }

        [TestMethod]
        public void PatchRoutesRemainCustomSourceAdministratorOnly()
        {
            var patchDefaultRoles = GetAuthorizeRoles(nameof(SqlMembershipSourcesController.PatchDefaultSourceCustomLabelAsync));
            CollectionAssert.Contains(patchDefaultRoles, Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR);
            CollectionAssert.DoesNotContain(patchDefaultRoles, Roles.CUSTOM_MEMBERSHIP_PROVIDER_READER);

            var patchAttributesRoles = GetAuthorizeRoles(nameof(SqlMembershipSourcesController.PatchDefaultSourceAttributesAsync));
            CollectionAssert.Contains(patchAttributesRoles, Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR);
            CollectionAssert.DoesNotContain(patchAttributesRoles, Roles.CUSTOM_MEMBERSHIP_PROVIDER_READER);
        }

        private static string[] GetAuthorizeRoles(string methodName)
        {
            var method = typeof(SqlMembershipSourcesController).GetMethod(methodName)!;
            var attribute = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                                  .Cast<AuthorizeAttribute>()
                                  .Single();

            return (attribute.Roles ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(role => role.Trim())
                .ToArray();
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
