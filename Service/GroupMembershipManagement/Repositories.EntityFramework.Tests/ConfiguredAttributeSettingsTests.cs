// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework.Tests;

[TestClass]
public class ConfiguredAttributeSettingsTests
{
    [DataTestMethod]
    [DataRow(SettingKey.IsAutoApprovalForConfiguredAttributeEnabled)]
    [DataRow(SettingKey.ConfiguredAttributeAutoApprovalConfiguration)]
    public async Task GenericPatchCannotBypassCompositeValidation(SettingKey key)
    {
        var options = new DbContextOptionsBuilder<GMMContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var write = new GMMContext(options);
        using var read = new GMMReadContext(options);
        var repository = new DatabaseSettingsRepository(write, read);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => repository.PatchSettingAsync(key, "true"));
        Assert.AreEqual(0, await write.Settings.CountAsync());
    }

    [TestMethod]
    public void StoredConfigurationFitsExistingUnboundedUnicodeColumn()
    {
        var options = new DbContextOptionsBuilder<GMMContext>()
            .UseSqlServer("Server=(unused);Database=settings;Trusted_Connection=True;").Options;
        using var context = new GMMContext(options);
        var property = context.Model.FindEntityType(typeof(Setting))!.FindProperty(nameof(Setting.SettingValue))!;
        Assert.AreEqual("nvarchar(max)", property.GetColumnType());
        Assert.IsNull(property.GetMaxLength());
    }
}
