// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Text.Json;

namespace Services.Tests
{
    [TestClass]
    public class AzureADTeamsUserConverterTests
    {
        private JsonSerializerOptions _options;

        [TestInitialize]
        public void Setup()
        {
            _options = new JsonSerializerOptions();
            _options.Converters.Add(new AzureADTeamsUserConverter());
        }

        [TestMethod]
        public void Serialize_AddAction_DoesNotIncludeProperties()
        {
            var user = new AzureADTeamsUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Add
            };

            var json = JsonSerializer.Serialize(user, _options);
            var doc = JsonDocument.Parse(json);

            Assert.IsTrue(doc.RootElement.TryGetProperty("ObjectId", out _));
            Assert.IsTrue(doc.RootElement.TryGetProperty("MembershipAction", out var action));
            Assert.AreEqual((int)MembershipAction.Add, action.GetInt32());
            Assert.IsFalse(doc.RootElement.TryGetProperty("Properties", out _));
        }

        [TestMethod]
        public void Serialize_RemoveAction_IncludesConversationMemberId()
        {
            var conversationMemberId = "conv-member-123";
            var user = new AzureADTeamsUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Remove,
                ConversationMemberId = conversationMemberId
            };

            var json = JsonSerializer.Serialize(user, _options);
            var doc = JsonDocument.Parse(json);

            Assert.IsTrue(doc.RootElement.TryGetProperty("Properties", out var props));
            Assert.IsTrue(props.TryGetProperty("ConversationMemberId", out var memberId));
            Assert.AreEqual(conversationMemberId, memberId.GetString());
        }

        [TestMethod]
        public void Deserialize_WithAllProperties_ReturnsCorrectUser()
        {
            var objectId = Guid.NewGuid();
            var json = $"{{\"ObjectId\":\"{objectId}\",\"MembershipAction\":2,\"Properties\":{{\"ConversationMemberId\":\"conv-123\"}}}}";

            var user = JsonSerializer.Deserialize<AzureADTeamsUser>(json, _options);

            Assert.IsNotNull(user);
            Assert.AreEqual(objectId, user.ObjectId);
            Assert.AreEqual(MembershipAction.Remove, user.MembershipAction);
        }

        [TestMethod]
        public void Deserialize_WithUnknownProperty_SkipsIt()
        {
            var objectId = Guid.NewGuid();
            var json = $"{{\"ObjectId\":\"{objectId}\",\"MembershipAction\":1,\"UnknownField\":\"value\"}}";

            var user = JsonSerializer.Deserialize<AzureADTeamsUser>(json, _options);

            Assert.IsNotNull(user);
            Assert.AreEqual(objectId, user.ObjectId);
            Assert.AreEqual(MembershipAction.Add, user.MembershipAction);
        }

        [TestMethod]
        public void RoundTrip_AddUser_PreservesData()
        {
            var original = new AzureADTeamsUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Add
            };

            var json = JsonSerializer.Serialize(original, _options);
            var deserialized = JsonSerializer.Deserialize<AzureADTeamsUser>(json, _options);

            Assert.AreEqual(original.ObjectId, deserialized.ObjectId);
            Assert.AreEqual(original.MembershipAction, deserialized.MembershipAction);
        }

        [TestMethod]
        public void RoundTrip_RemoveUser_PreservesConversationMemberId()
        {
            var original = new AzureADTeamsUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Remove,
                ConversationMemberId = "conv-member-456"
            };

            var json = JsonSerializer.Serialize(original, _options);
            var deserialized = JsonSerializer.Deserialize<AzureADTeamsUser>(json, _options);

            Assert.AreEqual(original.ObjectId, deserialized.ObjectId);
            Assert.AreEqual(original.MembershipAction, deserialized.MembershipAction);
        }
    }
}
