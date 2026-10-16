// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.AdfRunHistory;
using Moq;
using Repositories.Contracts;
using Services.Messages.Requests;
using System.Reflection;
using System.Text.Json;
using WebApi.Controllers.v1.AutoApprover;
using WebApi.Models;

namespace Services.Tests;

[TestClass]
public class ConfiguredAttributeApprovalTests
{
    private readonly Mock<IDatabaseSettingsRepository> _settings = new();
    private readonly Mock<IDatabaseSqlMembershipSourcesRepository> _sources = new();
    private readonly Mock<IAdfRunHistoryRepository> _dataFactory = new();
    private readonly Mock<ISqlMembershipRepository> _sql = new();
    private readonly Guid _run = Guid.NewGuid();

    private SaveConfiguredAttributeApprovalHandler SaveHandler() =>
        new(NullLogger<SaveConfiguredAttributeApprovalHandler>.Instance, _settings.Object,
            new GetDefaultSqlMembershipSourceAttributesHandler(
                NullLogger<GetDefaultSqlMembershipSourceAttributesHandler>.Instance, _sources.Object, _dataFactory.Object, _sql.Object),
            new ResolveDefaultSqlMembershipSourceAttributeMappingsHandler(
                NullLogger<ResolveDefaultSqlMembershipSourceAttributeMappingsHandler>.Instance, _dataFactory.Object, _sql.Object));

    private ConfiguredAttributeApprovalController Controller() => new(
        new GetConfiguredAttributeApprovalHandler(NullLogger<GetConfiguredAttributeApprovalHandler>.Instance, _settings.Object),
        SaveHandler(),
        NullLogger<ConfiguredAttributeApprovalController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
    [TestInitialize]
    public void Initialize()
    {
        _dataFactory.Setup(repository => repository.GetLatestAdfRunAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new AdfRunHistory { AdfRunId = _run, PublishedAtUtc = DateTime.UtcNow });
        _sql.Setup(repository => repository.CheckIfTableExistsAsync(_run.ToString("N"))).ReturnsAsync(true);
        _sql.Setup(repository => repository.CheckIfMappingsTableExistsAsync(_run.ToString("N"))).ReturnsAsync(true);
        _sql.Setup(repository => repository.GetColumnDetailsAsync(_run.ToString("N")))
            .ReturnsAsync([("JobTitle_Code", "nvarchar"), ("Position", "int"), ("Secret", "nvarchar"), ("Disabled", "nvarchar")]);
        _sources.Setup(repository => repository.GetDefaultSourceAttributesAsync()).ReturnsAsync([
            new() { Name = "JobTitle", Enabled = true, CustomLabel = "Job title" },
            new() { Name = "Position", Enabled = true },
            new() { Name = "Secret", Enabled = true, IsSensitive = true },
            new() { Name = "Disabled", Enabled = false }
        ]);
        _settings.Setup(repository => repository.GetConfiguredAttributeApprovalAsync())
            .ReturnsAsync(new ConfiguredAttributeApprovalConfiguration());
    }

    [TestMethod]
    public async Task DefaultReadReturnsDisabled()
    {
        var controller = Controller();
        var result = (OkObjectResult)await controller.GetConfigurationAsync();
        var configuration = (ConfiguredAttributeApprovalConfiguration)result.Value!;
        Assert.IsFalse(configuration.Enabled);
    }

    [TestMethod]
    public async Task SaveValidatesExactCodes()
    {
        _sql.Setup(repository => repository.GetAttributeMappingsByCodesAsync(
                "JobTitle", _run.ToString("N"), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([("001", "Friendly title")]);
        _settings.Setup(repository => repository.SaveConfiguredAttributeApprovalAsync(
                It.IsAny<ConfiguredAttributeApprovalConfiguration>()))
            .ReturnsAsync(new ConfiguredAttributeApprovalConfiguration
            {
                Enabled = true, Attributes = [new() { Attribute = "JobTitle", Values = ["001"] }]
            });
        var controller = Controller();
        var result = await controller.PutConfigurationAsync(new ConfiguredAttributeApprovalConfiguration
        {
            Enabled = true, SchemaVersion = 1, Attributes = [new() { Attribute = "JobTitle", Values = ["001"] }]
        });
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        _settings.Verify(repository => repository.SaveConfiguredAttributeApprovalAsync(
            It.Is<ConfiguredAttributeApprovalConfiguration>(configuration => configuration.Attributes[0].Values[0] == "001")), Times.Once);
    }

    [TestMethod]
    public async Task StaleCodeAndIneligibleFieldNeverWrite()
    {
        _sql.Setup(repository => repository.GetAttributeMappingsByCodesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        foreach (var name in new[] { "JobTitle", "Disabled", "JobTitle_Code", "Unknown" })
        {
            var controller = Controller();
                var result = await controller.PutConfigurationAsync(new()
            {
                Enabled = true, SchemaVersion = 1, Attributes = [new() { Attribute = name, Values = ["stale"] }]
            });
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult), name);
        }

        _settings.Verify(repository => repository.SaveConfiguredAttributeApprovalAsync(
            It.IsAny<ConfiguredAttributeApprovalConfiguration>()), Times.Never);
    }

    [TestMethod]
    public async Task DuplicateTypedValuesNeverWrite()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => SaveHandler().ExecuteAsync(new SaveConfiguredAttributeApprovalRequest(new()
        {
            Enabled = true,
            Attributes = [new() { Attribute = "Position", Values = ["01", "1"] }]
                })));
        _settings.Verify(repository => repository.SaveConfiguredAttributeApprovalAsync(
            It.IsAny<ConfiguredAttributeApprovalConfiguration>()), Times.Never);
    }

    [TestMethod]
    public async Task DependencyFailureIs500NotEmptySuccess()
    {
        _settings.Setup(repository => repository.GetConfiguredAttributeApprovalAsync())
            .ThrowsAsync(new InvalidOperationException("sensitive dependency detail"));
        var result = (ObjectResult)await Controller().GetConfigurationAsync();
        Assert.AreEqual(500, result.StatusCode);
        Assert.IsFalse(JsonSerializer.Serialize(result.Value).Contains("sensitive dependency detail"));
    }

    [TestMethod]
    public void ConfigurationActionsDeclareReaderAndAdministratorRoles()
    {
        var controller = typeof(ConfiguredAttributeApprovalController);
        var readRoles = controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles!;
        StringAssert.Contains(readRoles, Roles.AUTO_APPROVER_READER);
        StringAssert.Contains(readRoles, Roles.AUTO_APPROVER_ADMINISTRATOR);
        Assert.AreEqual(Roles.AUTO_APPROVER_ADMINISTRATOR,
            controller.GetMethod(nameof(ConfiguredAttributeApprovalController.PutConfigurationAsync))!
                .GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }
}
