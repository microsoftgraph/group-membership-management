// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tests.FunctionApps
{
    /// <summary>
    /// Verifies membership metadata is serialized before the member collection.
    /// </summary>
    [TestClass]
    public class MembershipSerializationOrderTests
    {
        private static readonly JsonSerializerOptions _whenWritingDefault = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        [TestMethod]
        public void AMembershipSerializesItsMembersLast()
        {
            var membership = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
                RunId = Guid.NewGuid(),
                SyncJobId = Guid.NewGuid(),
                Exclusionary = true,
                MembershipObtainerDryRunEnabled = true,
                IsLastMessage = true,
                MessageIndex = 3,
                TotalMessageCount = 4,
                Query = "[{\"type\":\"SqlMembership\"}]",
                SourceMembers = new List<AzureADUser> { new AzureADUser { ObjectId = Guid.NewGuid() } }
            };

            AssertMembersComeLast(JsonSerializer.Serialize(membership), "default options");
            AssertMembersComeLast(JsonSerializer.Serialize(membership, _whenWritingDefault), "WhenWritingDefault");
        }

        [TestMethod]
        public void ATeamsMembershipSerializesItsMembersLast()
        {
            var membership = new TeamsGroupMembership
            {
                Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
                RunId = Guid.NewGuid(),
                SyncJobId = Guid.NewGuid(),
                Exclusionary = true,
                Query = "[{\"type\":\"TeamsChannelMembership\"}]",
                SourceMembers = new List<AzureADTeamsUser> { new AzureADTeamsUser { ObjectId = Guid.NewGuid() } }
            };

            AssertMembersComeLast(JsonSerializer.Serialize(membership), "default options");
            AssertMembersComeLast(JsonSerializer.Serialize(membership, _whenWritingDefault), "WhenWritingDefault");
        }

        [TestMethod]
        public void AMembershipWrittenInThePreviousShapeStillReadsCorrectly()
        {
            var runId = Guid.NewGuid();
            var memberId = Guid.NewGuid();
            var previousShape =
                "{\"Destination\":{\"ObjectId\":\"" + Guid.NewGuid().ToString("D") + "\"}," +
                "\"SourceMembers\":[{\"ObjectId\":\"" + memberId.ToString("D") + "\"}]," +
                "\"RunId\":\"" + runId.ToString("D") + "\",\"Exclusionary\":true}";

            var membership = JsonSerializer.Deserialize<GroupMembership>(previousShape);

            Assert.AreEqual(runId, membership.RunId);
            Assert.IsTrue(membership.Exclusionary);
            Assert.AreEqual(1, membership.SourceMembers.Count);
            Assert.AreEqual(memberId, membership.SourceMembers[0].ObjectId);
        }

        private static void AssertMembersComeLast(string json, string description)
        {
            using var document = JsonDocument.Parse(json);
            var propertyNames = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();

            Assert.AreEqual(
                "SourceMembers",
                propertyNames[^1],
                $"{description}: SourceMembers must be the final serialized property.");
        }
    }
}
