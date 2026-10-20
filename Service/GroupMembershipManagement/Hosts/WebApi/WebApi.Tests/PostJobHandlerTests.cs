// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Net;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services;
using Services.Messages.Requests;
using AzureADUser = Models.AzureADUser;
using AzureADTeamsChannel = Models.Entities.AzureADTeamsChannel;
using SyncJobEntity = Models.SyncJob;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace WebApi.Tests
{
    [TestClass]
    public class PostJobHandlerTests
    {
        private const string GroupId = "00000000-0000-0000-0000-000000000042";
        private const string ChannelId = "19:channel@thread.tacv2";
        private const string OtherGroupSource = "00000000-0000-0000-0000-000000000001";

        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository = null!;
        private PostJobHandler _handler = null!;

        [TestInitialize]
        public void SetUp()
        {
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();

            _handler = new PostJobHandler(
                Mock.Of<ILogger<PostJobHandler>>(),
                _syncJobRepository.Object,
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                Mock.Of<IDatabaseTitlesRepository>(),
                _graphGroupRepository.Object,
                Mock.Of<ISyncJobChangeRepository>(),
                Mock.Of<IDatabaseSettingsRepository>(),
                Mock.Of<IPendingConfigurationConfig>(),
                Mock.Of<IServiceBusQueueRepository>(),
                new TelemetryClient(new TelemetryConfiguration()),
                _teamsChannelRepository.Object);
        }

        private static string TeamsChannelDestination(string groupId = GroupId, string channelId = ChannelId) =>
            $@"[{{""type"":""TeamsChannelMembership"",""value"":{{""objectId"":""{groupId}"",""channelId"":""{channelId}""}}}}]";

        private static string GroupDestination(string groupId = GroupId) =>
            $@"[{{""type"":""GroupMembership"",""value"":{{""objectId"":""{groupId}""}}}}]";

        private static string GroupSourcePart(string source = OtherGroupSource) =>
            $@"{{""type"":""GroupMembership"",""source"":""{source}""}}";

        private static string TeamsChannelSourcePart(string objectId = GroupId, string channelId = ChannelId, bool exclusionary = false) =>
            $@"{{""type"":""TeamsChannelMembership"",""source"":{{""objectId"":""{objectId}"",""channelId"":""{channelId}""}},""exclusionary"":{exclusionary.ToString().ToLowerInvariant()}}}";

        private PostJobRequest BuildRequest(string query, string destination)
        {
            var newSyncJob = new NewSyncJobDTO
            {
                Query = $"[{query}]",
                Destination = destination,
                StartDate = DateTime.UtcNow.ToString("o"),
                Period = 24,
                Requestor = "requestor@contoso.com",
                Status = "Idle"
            };

            return new PostJobRequest(Guid.NewGuid().ToString(), newSyncJob, false, "Test User", "justification", false);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Onboarding_PassesCompleteDestinationGraphToSingleCreate(bool isChannel)
        {
            _graphGroupRepository.Setup(r => r.GetUserByUpnOrIdAsync(It.IsAny<string>(), false))
                .ReturnsAsync(new AzureADUser { UserPrincipalName = "owner@contoso.com" });
            _graphGroupRepository.Setup(r => r.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                .ReturnsAsync(true);
            _graphGroupRepository.Setup(r => r.GetGroupNameAsync(Guid.Parse(GroupId))).ReturnsAsync("Display name");
            _graphGroupRepository.Setup(r => r.GetGroupEmailAsync(Guid.Parse(GroupId))).ReturnsAsync("group@contoso.com");
            _teamsChannelRepository.Setup(r => r.GetTeamsChannelNameAsync(It.IsAny<AzureADTeamsChannel>()))
                .ReturnsAsync("Channel name");
            SyncJobEntity? captured = null;
            _syncJobRepository.Setup(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()))
                .Callback<SyncJobEntity>(job => captured = job).ReturnsAsync(Guid.Empty);
            var request = BuildRequest(GroupSourcePart(), isChannel ? TeamsChannelDestination() : GroupDestination());

            await _handler.ExecuteAsync(request);

            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Once);
            Assert.IsNotNull(captured);
            Assert.AreEqual(request.NewSyncJob.Destination, captured.Destination);
            Assert.AreEqual(captured.MembershipType, captured.DestinationRecord.DestinationType);
            using var audit = JsonDocument.Parse(SyncJobSerializationHelper.SerializeSyncJob(captured));
            Assert.AreEqual(request.NewSyncJob.Destination, audit.RootElement.GetProperty("Destination").GetString());
            Assert.IsFalse(audit.RootElement.TryGetProperty("DestinationRecord", out _),
                "The EF-only abstract navigation must not change audit or queue JSON.");
            Assert.IsFalse(global::Models.DictionaryHelper.ToDictionary(captured).ContainsKey("DestinationRecord"));
            var roundTrip = JsonSerializer.Deserialize<SyncJobEntity>(JsonSerializer.Serialize(captured));
            Assert.IsNotNull(roundTrip);
            Assert.AreEqual(captured.Destination, roundTrip.Destination);
            Assert.IsNull(roundTrip.DestinationRecord);
            if (isChannel)
            {
                var channel = captured.DestinationRecord as global::Models.TeamsChannelDestination;
                Assert.IsNotNull(channel);
                Assert.AreEqual(captured.Channel.GroupId, channel.TeamId);
                Assert.AreEqual(captured.Channel.ChannelId, channel.ChannelId);
                Assert.AreEqual("Display name", channel.TeamName);
                Assert.AreEqual("Channel name", channel.ChannelName);
            }
            else
            {
                var group = captured.DestinationRecord as global::Models.GroupDestination;
                Assert.IsNotNull(group);
                Assert.AreEqual(captured.Group.GroupId, group.GroupId);
                Assert.AreEqual("Display name", group.Name);
                Assert.AreEqual("group@contoso.com", group.Email);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Onboarding_AttributeLookupFailure_DoesNotCreateJob(bool isChannel)
        {
            _graphGroupRepository.Setup(r => r.GetUserByUpnOrIdAsync(It.IsAny<string>(), false))
                .ReturnsAsync(new AzureADUser { UserPrincipalName = "owner@contoso.com" });
            _graphGroupRepository.Setup(r => r.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                .ReturnsAsync(true);
            if (isChannel)
            {
                _teamsChannelRepository.Setup(r => r.GetTeamsChannelNameAsync(It.IsAny<AzureADTeamsChannel>()))
                    .ThrowsAsync(new InvalidOperationException("Directory unavailable"));
            }
            else
            {
                _graphGroupRepository.Setup(r => r.GetGroupNameAsync(It.IsAny<Guid>()))
                    .ThrowsAsync(new InvalidOperationException("Directory unavailable"));
            }

            var response = await _handler.ExecuteAsync(
                BuildRequest(GroupSourcePart(), isChannel ? TeamsChannelDestination() : GroupDestination()));

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Never);
        }

        [TestMethod]
        public async Task RejectsTeamsChannelSourceWithoutChannelDestination()
        {
            var request = BuildRequest(
                $"{GroupSourcePart()},{TeamsChannelSourcePart()}",
                GroupDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceRequiresChannelDestination", response.ErrorCode);
            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Never);
        }

        [TestMethod]
        public async Task RejectsTeamsChannelSourceThatDoesNotMatchDestinationChannel()
        {
            var request = BuildRequest(
                $"{GroupSourcePart()},{TeamsChannelSourcePart(channelId: "19:different@thread.tacv2")}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceMustMatchDestination", response.ErrorCode);
            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Never);
        }

        [TestMethod]
        public async Task RejectsTeamsChannelSourceThatDoesNotMatchDestinationGroup()
        {
            var request = BuildRequest(
                $"{GroupSourcePart()},{TeamsChannelSourcePart(objectId: "00000000-0000-0000-0000-000000000099")}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceMustMatchDestination", response.ErrorCode);
        }

        [TestMethod]
        public async Task RejectsTeamsChannelSourceAsOnlySourcePart()
        {
            var request = BuildRequest(
                TeamsChannelSourcePart(),
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceRequiresAdditionalSource", response.ErrorCode);
        }

        [TestMethod]
        public async Task RejectsExclusionaryTeamsChannelSource()
        {
            var request = BuildRequest(
                $"{GroupSourcePart()},{TeamsChannelSourcePart(exclusionary: true)}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceCannotBeExclusionary", response.ErrorCode);
        }

        [TestMethod]
        public async Task AllowsMatchingTeamsChannelSource_PassesValidationAndReachesOwnerCheck()
        {
            // A matching TeamsChannel source with a companion group source is not blocked by the
            // TeamsChannel source validation; it proceeds to the ownership gate. With ownership
            // denied it returns Forbidden, proving the source validation did not reject it.
            _graphGroupRepository
                .Setup(r => r.GetUserByUpnOrIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new AzureADUser { ObjectId = Guid.NewGuid() });
            _graphGroupRepository
                .Setup(r => r.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                .ReturnsAsync(false);

            var request = BuildRequest(
                $"{GroupSourcePart()},{TeamsChannelSourcePart()}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.AreNotEqual("TeamsChannelSourceRequiresChannelDestination", response.ErrorCode);
            Assert.AreNotEqual("TeamsChannelSourceMustMatchDestination", response.ErrorCode);
            Assert.AreNotEqual("TeamsChannelSourceRequiresAdditionalSource", response.ErrorCode);
            Assert.AreNotEqual("TeamsChannelSourceCannotBeExclusionary", response.ErrorCode);
        }

        [TestMethod]
        public async Task RejectsMisCasedTeamsChannelSourceTypeThatDoesNotMatchDestination()
        {
            // F2: type matching must be case-insensitive so a mis-cased "type" cannot skip validation.
            var misCasedSource =
                $@"{{""type"":""teamschannelmembership"",""source"":{{""objectId"":""{GroupId}"",""channelId"":""19:different@thread.tacv2""}}}}";
            var request = BuildRequest(
                $"{GroupSourcePart()},{misCasedSource}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceMustMatchDestination", response.ErrorCode);
            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Never);
        }

        [TestMethod]
        public async Task RejectsTeamsChannelSourceWhenOnlyOtherPartIsTypeless()
        {
            // F3: a typeless/garbage part must not satisfy the "additional source" requirement.
            var typelessPart = @"{""source"":""foo""}";
            var request = BuildRequest(
                $"{TeamsChannelSourcePart()},{typelessPart}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceRequiresAdditionalSource", response.ErrorCode);
            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Never);
        }

        [TestMethod]
        public async Task RejectsTeamsChannelSourceWhenOnlyOtherPartHasUnrecognizedType()
        {
            // #5: a sibling part with a non-empty but unrecognized type must not satisfy the
            // "additional source" requirement; no obtainer would process it and the sync would stall.
            var unknownTypePart = @"{""type"":""SomethingUnsupported"",""source"":""foo""}";
            var request = BuildRequest(
                $"{TeamsChannelSourcePart()},{unknownTypePart}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceRequiresAdditionalSource", response.ErrorCode);
            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Never);
        }

        [TestMethod]
        public async Task RejectsTeamsChannelSourceWithStringExclusionaryTrue()
        {
            // F4: a stringified exclusionary boolean must be honored so it is rejected at submit
            // rather than passing here and crashing the trigger's (bool) cast later.
            var stringExclusionarySource =
                $@"{{""type"":""TeamsChannelMembership"",""source"":{{""objectId"":""{GroupId}"",""channelId"":""{ChannelId}""}},""exclusionary"":""true""}}";
            var request = BuildRequest(
                $"{GroupSourcePart()},{stringExclusionarySource}",
                TeamsChannelDestination());

            var response = await _handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("TeamsChannelSourceCannotBeExclusionary", response.ErrorCode);
            _syncJobRepository.Verify(r => r.CreateSyncJobAsync(It.IsAny<SyncJobEntity>()), Times.Never);
        }

        [TestMethod]
        public async Task IgnoresQueriesWithoutTeamsChannelSource()
        {
            _graphGroupRepository
                .Setup(r => r.GetUserByUpnOrIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new AzureADUser { ObjectId = Guid.NewGuid() });
            _graphGroupRepository
                .Setup(r => r.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                .ReturnsAsync(false);

            var request = BuildRequest(
                GroupSourcePart(),
                GroupDestination());

            var response = await _handler.ExecuteAsync(request);

            // No TeamsChannel source, so validation is a no-op; the request proceeds to the owner gate.
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
