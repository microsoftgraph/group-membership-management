// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Models.Tests;

[TestClass]
public class ConfiguredAttributeApprovalTests
{
    [TestMethod]
    public void AbsentConfigurationIsDisabled()
    {
        var result = ConfiguredAttributeApprovalConfiguration.FromSettings([]);
        Assert.IsFalse(result.Enabled);
        Assert.AreEqual(0, result.Attributes.Count);
    }

    [TestMethod]
    public void EnabledFlagWithoutDocumentIsInvalid()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            ConfiguredAttributeApprovalConfiguration.FromSettings([
                new Setting { SettingKey = SettingKey.IsAutoApprovalForConfiguredAttributeEnabled, SettingValue = "true" }
            ]));
    }

    [TestMethod]
    public void StoredConfigurationRoundTripsWithoutPuttingFlagInDocument()
    {
        var configuration = ValidConfiguration();
        var json = configuration.SerializeDocument();
        Assert.IsFalse(json.Contains("enabled", StringComparison.OrdinalIgnoreCase));
        var result = ConfiguredAttributeApprovalConfiguration.FromSettings([
            new Setting { SettingKey = SettingKey.IsAutoApprovalForConfiguredAttributeEnabled, SettingValue = "true" },
            new Setting { SettingKey = SettingKey.ConfiguredAttributeAutoApprovalConfiguration, SettingValue = json }
        ]);
        Assert.IsTrue(result.Enabled);
        CollectionAssert.AreEqual(new[] { "001", "002" }, result.Attributes[0].Values);
    }

    [TestMethod]
    public void RejectsEmptyEnabledConfigurationAndIncompleteDisabledRows()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            new ConfiguredAttributeApprovalConfiguration { Enabled = true }.Validate());
        Assert.ThrowsException<ArgumentException>(() =>
            new ConfiguredAttributeApprovalConfiguration { Attributes = [new() { Attribute = "JobTitle" }] }.Validate());
    }

    [TestMethod]
    public void RejectsDuplicateAttributesAndValues()
    {
        var configuration = ValidConfiguration();
        configuration.Attributes.Add(new() { Attribute = "jobtitle", Values = ["003"] });
        Assert.ThrowsException<ArgumentException>(() => configuration.Validate());
        configuration = ValidConfiguration();
        configuration.Attributes[0].Values.Add("001");
        Assert.ThrowsException<ArgumentException>(() => configuration.Validate());
    }

    [TestMethod]
    public void StringCodesStayDistinctAndFieldMatchingUsesAnd()
    {
        var configuration = ValidConfiguration();
        configuration.Attributes.Add(new() { Attribute = "Position", Values = ["1", "2"] });
        Assert.IsTrue(configuration.Matches(new Dictionary<string, string> { ["JobTitle"] = "001", ["Position"] = "2" }));
        Assert.IsFalse(configuration.Matches(new Dictionary<string, string> { ["JobTitle"] = "1", ["Position"] = "2" }));
        Assert.IsFalse(configuration.Matches(new Dictionary<string, string> { ["JobTitle"] = "001", ["Position"] = "3" }));
        Assert.IsFalse(configuration.Matches(new Dictionary<string, string> { ["JobTitle"] = "001" }));
    }

    [TestMethod]
    public void RejectsMalformedAndUnknownVersionStoredDocuments()
    {
        foreach (var json in new[] { "null", "{}", "{", """{"schemaVersion":2,"attributes":[]}""" })
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                ConfiguredAttributeApprovalConfiguration.FromSettings([
                    new Setting { SettingKey = SettingKey.ConfiguredAttributeAutoApprovalConfiguration, SettingValue = json }
                ]));
        }
    }

    private static ConfiguredAttributeApprovalConfiguration ValidConfiguration() => new()
    {
        Enabled = true,
        Attributes = [new() { Attribute = "JobTitle", Values = ["001", "002"] }]
    };

    [DataTestMethod]
    [DataRow("001", "nvarchar", "001")]
    [DataRow("001", "int", "1")]
    [DataRow("1.00", "decimal", "1")]
    [DataRow("2026-10-15", "date", "2026-10-15")]
    public void CanonicalizationHonorsSourceType(string value, string type, string expected)
    {
        Assert.IsTrue(ConfiguredAttributeApprovalMetadata.TryCanonicalize(value, type, out var canonical));
        Assert.AreEqual(expected, canonical);
    }

    [DataTestMethod]
    [DataRow("", "nvarchar")]
    [DataRow("-1", "int")]
    [DataRow("unknown", "nvarchar")]
    [DataRow("256", "tinyint")]
    [DataRow("2147483648", "INT")]
    [DataRow("1 OR 1=1", "int")]
    [DataRow("not-a-date", "date")]
    [DataRow("value", "unsupported")]
    public void MissingAndInvalidValuesCannotBeCanonicalized(string value, string type)
    {
        Assert.IsFalse(ConfiguredAttributeApprovalMetadata.TryCanonicalize(value, type, out _));
    }

    [TestMethod]
    public void AmbiguousSourceMetadataFailsExplicitly()
    {
        Assert.ThrowsException<InvalidOperationException>(() => ConfiguredAttributeApprovalMetadata.Resolve(
            [("Title", "nvarchar"), ("Title_Code", "nvarchar")], []));
        Assert.ThrowsException<InvalidOperationException>(() => ConfiguredAttributeApprovalMetadata.Resolve(
            [("Title", "nvarchar")], [new() { Name = "Title" }, new() { Name = "title" }]));
    }
}
