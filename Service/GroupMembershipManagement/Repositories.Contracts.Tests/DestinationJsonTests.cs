// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Contracts.DestinationResolution;
using System;
using System.Text.Json;

namespace Repositories.Contracts.Tests
{
    /// <summary>
    /// T020: JSON compatibility tests for the generated <c>SyncJobs.Destination</c> value
    /// (FR-019, AC-7, contract: resolver-json-cutover.md). They assert the single-element
    /// array wrapper, the exact type strings, the lower-case objectId/channelId field names,
    /// and that channelId is omitted for groups and required for Teams channels.
    /// </summary>
    [TestClass]
    public class DestinationJsonTests
    {
        [TestMethod]
        public void Generate_GroupDestination_ProducesLegacyCompatibleJson()
        {
            var objectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var json = DestinationJson.Generate(new ResolvedGroupDestination
            {
                SyncJobId = Guid.NewGuid(),
                ObjectId = objectId
            });

            Assert.AreEqual(
                "[{\"type\":\"GroupMembership\",\"value\":{\"objectId\":\"11111111-1111-1111-1111-111111111111\"}}]",
                json);
        }

        [TestMethod]
        public void Generate_TeamsChannelDestination_ProducesLegacyCompatibleJson()
        {
            var objectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var json = DestinationJson.Generate(new ResolvedTeamsChannelDestination
            {
                SyncJobId = Guid.NewGuid(),
                TeamObjectId = objectId,
                ChannelId = "19:abc123@thread.tacv2"
            });

            Assert.AreEqual(
                "[{\"type\":\"TeamsChannelMembership\",\"value\":{\"objectId\":\"22222222-2222-2222-2222-222222222222\"," +
                "\"channelId\":\"19:abc123@thread.tacv2\"}}]",
                json);
        }

        [TestMethod]
        public void Generate_Group_OmitsChannelId()
        {
            var json = DestinationJson.Generate(new ResolvedGroupDestination
            {
                SyncJobId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid()
            });

            Assert.IsFalse(json.Contains("channelId"), "channelId must be omitted for group destinations.");
        }

        [TestMethod]
        public void Generate_IsSingleElementArray_WithLowerCaseFields()
        {
            var json = DestinationJson.Generate(new ResolvedTeamsChannelDestination
            {
                SyncJobId = Guid.NewGuid(),
                TeamObjectId = Guid.NewGuid(),
                ChannelId = "19:xyz@thread.tacv2"
            });

            using var doc = JsonDocument.Parse(json);
            Assert.AreEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.AreEqual(1, doc.RootElement.GetArrayLength());

            var element = doc.RootElement[0];
            Assert.IsTrue(element.TryGetProperty("type", out _), "lower-case 'type' expected.");
            Assert.IsTrue(element.TryGetProperty("value", out var value), "lower-case 'value' expected.");
            Assert.IsTrue(value.TryGetProperty("objectId", out _), "lower-case 'objectId' expected.");
            Assert.IsTrue(value.TryGetProperty("channelId", out _), "lower-case 'channelId' expected.");
        }

        [TestMethod]
        public void Generate_Output_MatchesLegacyStoredLowerCasePaths()
        {
            // The stored SyncJobs.Destination format is lower-case; the schema migration reads it
            // via JSON_VALUE(Destination, '$[0].value.channelId'). Assert the generated JSON exposes
            // the same lower-case paths and round-trips into the typed value model.
            var teamId = Guid.NewGuid();
            var json = DestinationJson.Generate(new ResolvedTeamsChannelDestination
            {
                SyncJobId = Guid.NewGuid(),
                TeamObjectId = teamId,
                ChannelId = "19:legacy@thread.tacv2"
            });

            using var doc = JsonDocument.Parse(json);
            var value = doc.RootElement[0].GetProperty("value");
            Assert.AreEqual(teamId, value.GetProperty("objectId").GetGuid());
            Assert.AreEqual("19:legacy@thread.tacv2", value.GetProperty("channelId").GetString());

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var typed = JsonSerializer.Deserialize<TeamsChannelDestinationValue>(value.GetRawText(), options);
            Assert.AreEqual(teamId, typed!.ObjectId);
            Assert.AreEqual("19:legacy@thread.tacv2", typed.ChannelId);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Generate_TeamsChannel_WithEmptyChannelId_Throws()
        {
            DestinationJson.Generate(new ResolvedTeamsChannelDestination
            {
                SyncJobId = Guid.NewGuid(),
                TeamObjectId = Guid.NewGuid(),
                ChannelId = ""
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Generate_Null_Throws()
        {
            DestinationJson.Generate(null!);
        }
    }
}
