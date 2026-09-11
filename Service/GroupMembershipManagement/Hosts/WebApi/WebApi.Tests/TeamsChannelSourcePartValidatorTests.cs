// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Services.WebApi.Validators;
using System;

namespace Services.WebApi.Tests
{
    [TestClass]
    public class TeamsChannelSourcePartValidatorTests
    {
        private const string GroupId = "00000000-0000-0000-0000-000000000001";
        private const string ChannelId = "19:abc@thread.tacv2";

        private static string TeamsChannelDestination(string groupId = GroupId, string channelId = ChannelId) =>
            $@"[{{""type"":""TeamsChannelMembership"",""value"":{{""objectId"":""{groupId}"",""channelId"":""{channelId}""}}}}]";

        private static string GroupDestination(string groupId = GroupId) =>
            $@"[{{""type"":""GroupMembership"",""value"":{{""objectId"":""{groupId}""}}}}]";

        private static string GroupSourcePart(string source = "11111111-1111-1111-1111-111111111111") =>
            $@"{{""type"":""GroupMembership"",""source"":""{source}""}}";

        private static string TeamsChannelSourcePart(string objectId = GroupId, string channelId = ChannelId, bool exclusionary = false) =>
            $@"{{""type"":""TeamsChannelMembership"",""source"":{{""objectId"":""{objectId}"",""channelId"":""{channelId}""}},""exclusionary"":{exclusionary.ToString().ToLowerInvariant()}}}";

        // Builds a SyncJob the way the update path (PatchJobHandler) sees it: the Channel navigation is
        // NOT hydrated, so the validator must resolve the destination channel from the Destination JSON.
        private static SyncJob UpdatePathJob(string query, string destination, string membershipType = "TeamsChannelMembership") =>
            new SyncJob
            {
                Query = $"[{query}]",
                Destination = destination,
                MembershipType = membershipType,
                TargetOfficeGroupId = Guid.Parse(GroupId),
                Channel = null
            };

        [TestMethod]
        public void ReturnsNullWhenThereIsNoTeamsChannelSource()
        {
            var job = UpdatePathJob(GroupSourcePart(), GroupDestination(), membershipType: "GroupMembership");
            Assert.IsNull(TeamsChannelSourcePartValidator.Validate(job));
        }

        [TestMethod]
        public void ReturnsNullWhenQueryIsNotWellFormedJson()
        {
            // The update path may hand the validator a query that is not a JSON array (e.g. a legacy or
            // free-form value). It contains no TeamsChannel source part, so the validator must treat it as
            // valid rather than throwing on JsonNode.Parse (regression for the PATCH-path 500).
            var job = new SyncJob
            {
                Query = "UpdatedQuery",
                Destination = TeamsChannelDestination(),
                MembershipType = "TeamsChannelMembership",
                TargetOfficeGroupId = Guid.Parse(GroupId),
                Channel = null
            };
            Assert.IsNull(TeamsChannelSourcePartValidator.Validate(job));
        }

        [TestMethod]
        public void RejectsTeamsChannelSourceWithoutChannelDestination()
        {
            var job = UpdatePathJob($"{GroupSourcePart()},{TeamsChannelSourcePart()}", GroupDestination(), membershipType: "GroupMembership");
            var error = TeamsChannelSourcePartValidator.Validate(job);
            Assert.IsNotNull(error);
            Assert.AreEqual("TeamsChannelSourceRequiresChannelDestination", error!.ErrorCode);
        }

        [TestMethod]
        public void RejectsTeamsChannelSourceThatDoesNotMatchDestinationChannel()
        {
            var job = UpdatePathJob(
                $"{GroupSourcePart()},{TeamsChannelSourcePart(channelId: "19:different@thread.tacv2")}",
                TeamsChannelDestination());
            var error = TeamsChannelSourcePartValidator.Validate(job);
            Assert.IsNotNull(error);
            Assert.AreEqual("TeamsChannelSourceMustMatchDestination", error!.ErrorCode);
        }

        [TestMethod]
        public void RejectsTeamsChannelSourceAsOnlySourcePart()
        {
            var job = UpdatePathJob(TeamsChannelSourcePart(), TeamsChannelDestination());
            var error = TeamsChannelSourcePartValidator.Validate(job);
            Assert.IsNotNull(error);
            Assert.AreEqual("TeamsChannelSourceRequiresAdditionalSource", error!.ErrorCode);
        }

        [TestMethod]
        public void RejectsTeamsChannelSourceWhenOnlyOtherPartHasUnrecognizedType()
        {
            var unknownTypePart = @"{""type"":""SomethingUnsupported"",""source"":""foo""}";
            var job = UpdatePathJob($"{TeamsChannelSourcePart()},{unknownTypePart}", TeamsChannelDestination());
            var error = TeamsChannelSourcePartValidator.Validate(job);
            Assert.IsNotNull(error);
            Assert.AreEqual("TeamsChannelSourceRequiresAdditionalSource", error!.ErrorCode);
        }

        [TestMethod]
        public void RejectsExclusionaryTeamsChannelSource()
        {
            var job = UpdatePathJob($"{GroupSourcePart()},{TeamsChannelSourcePart(exclusionary: true)}", TeamsChannelDestination());
            var error = TeamsChannelSourcePartValidator.Validate(job);
            Assert.IsNotNull(error);
            Assert.AreEqual("TeamsChannelSourceCannotBeExclusionary", error!.ErrorCode);
        }

        [TestMethod]
        public void AcceptsMatchingTeamsChannelSourceResolvedFromDestinationJson()
        {
            // The update path passes a job whose Channel is not hydrated; the destination channel must be
            // resolved from the Destination JSON. A matching TeamsChannel source with a companion group
            // source is valid.
            var job = UpdatePathJob($"{GroupSourcePart()},{TeamsChannelSourcePart()}", TeamsChannelDestination());
            Assert.IsNull(TeamsChannelSourcePartValidator.Validate(job));
        }
    }
}
