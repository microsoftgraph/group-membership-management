// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Hosts.GroupMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Polly;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    [TestClass]
    public sealed class FailureModelReproTests
    {
        /// <summary>
        /// I2: replaying saved-delta processing must reproduce identical part, cache, and token bytes.
        /// Findings 2 and 10. ProcessCachedAndDeltaUsersFunction.cs:46 rereads the original cache path while
        /// lines 127-129 delete the delta inputs, so an equal-cardinality replay republishes stale cache data.
        /// Expected failing assertion: no published artifact differs after the exact-input replay.
        /// </summary>
        [TestMethod]
        [TestCategory("FailureModelRepro")]
        public async Task I2_SavedDeltaReplay_PublishesIdenticalBytes()
        {
            var retainedMember = Guid.NewGuid();
            var removedMember = Guid.NewGuid();
            var addedMember = Guid.NewGuid();
            var fixture = await CreateProcessFixtureAsync(
                new[] { retainedMember, removedMember },
                new[] { addedMember },
                new[] { removedMember },
                expectedCount: 2,
                identityTypes: new[] { "users" });

            var firstResponse = await fixture.Function.RunAsync(fixture.Request);
            var firstArtifacts = CapturePublishedArtifacts(
                fixture.Storage,
                firstResponse.MembershipFilePath,
                fixture.Request.SourceGroupId);

            var secondResponse = await fixture.Function.RunAsync(fixture.Request);
            var secondArtifacts = CapturePublishedArtifacts(
                fixture.Storage,
                secondResponse.MembershipFilePath,
                fixture.Request.SourceGroupId);

            var changedArtifacts = firstArtifacts
                .Where(entry =>
                    !secondArtifacts.TryGetValue(entry.Key, out var secondBytes) ||
                    !string.Equals(entry.Value, secondBytes, StringComparison.Ordinal))
                .Select(entry => entry.Key)
                .ToArray();
            Assert.AreEqual(
                0,
                changedArtifacts.Length,
                $"Replay changed: {string.Join(", ", changedArtifacts)}.");
        }

        /// <summary>
        /// I4: an ID present in both the add and remove sets makes the saved-delta result ambiguous.
        /// Finding 17. ProcessCachedAndDeltaUsersFunction.cs:49-60 applies every add before every remove
        /// and can still match the expected count after removing a current member.
        /// Expected failing assertions: the cache is rejected and no membership part is published.
        /// </summary>
        [TestMethod]
        [TestCategory("FailureModelRepro")]
        public async Task I4_OverlappingAddAndRemove_ForcesInitialReadRebuild()
        {
            var overlappingMember = Guid.NewGuid();
            var retainedMember = Guid.NewGuid();
            var offsettingAddition = Guid.NewGuid();
            var fixture = await CreateProcessFixtureAsync(
                new[] { overlappingMember, retainedMember },
                new[] { overlappingMember, offsettingAddition },
                new[] { overlappingMember },
                expectedCount: 2,
                identityTypes: new[] { "users" });

            var response = await fixture.Function.RunAsync(fixture.Request);
            var failures = new List<string>();

            if (response.CacheMatchesGroupCount)
            {
                failures.Add("the ambiguous result reported that the cache matched");
            }

            if (!string.IsNullOrEmpty(response.MembershipFilePath))
            {
                failures.Add($"membership path '{response.MembershipFilePath}' was returned");
            }

            var publishedPart = fixture.Storage.Paths.FirstOrDefault(path => path.Contains(
                    $"_{fixture.Request.SyncJob.RunId}_GroupMembership_{fixture.Request.CurrentPart}.json",
                    StringComparison.OrdinalIgnoreCase));
            if (publishedPart != null)
            {
                failures.Add($"membership part '{publishedPart}' was published");
            }

            Assert.AreEqual(
                0,
                failures.Count,
                string.Join("; ", failures));
        }

        /// <summary>
        /// I13: the in-memory blob double must write the same identifier-only shape as production.
        /// Finding 36 is a test-fidelity gap. BlobStorageRepository.cs:310-358 manually writes members
        /// containing only ObjectId, while the in-memory double serializes complete AzureADUser instances.
        /// Expected failing assertion: production and in-memory output have the same parsed JSON shape.
        /// </summary>
        [TestMethod]
        [TestCategory("FailureModelRepro")]
        public async Task I13_IdentifierOnlyWriter_MatchesInMemoryDoubleAndProductionReader()
        {
            var written = new MemoryStream();
            var blobClient = new Mock<BlobClient>();
            blobClient
                .Setup(client => client.OpenWriteAsync(
                    true,
                    It.IsAny<BlobOpenWriteOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(written);
            var container = new Mock<BlobContainerClient>();
            container
                .Setup(client => client.GetBlobClient(It.IsAny<string>()))
                .Returns(blobClient.Object);
            var repository = new BlobStorageRepository(container.Object);
            var destinationId = Guid.NewGuid();
            var memberId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();
            const string path = "part.json";
            const string query = "[]";

            await repository.UploadGroupMembershipFromGuidsAsync(
                path,
                new[] { memberId },
                new AzureADGroup { ObjectId = destinationId },
                runId,
                syncJobId,
                exclusionary: true,
                membershipObtainerDryRunEnabled: false,
                query);
            var bytes = written.ToArray();
            var readMembers = new List<AzureADUser>();
            GroupMembership envelope = null;
            await using var source = new MemoryStream(bytes);
            await foreach (var member in MembershipStream.ReadAsync(
                source,
                details => envelope = details))
            {
                readMembers.Add(member);
            }

            Assert.IsNotNull(envelope);
            Assert.AreEqual(destinationId, envelope.Destination.ObjectId);
            Assert.AreEqual(runId, envelope.RunId);
            Assert.AreEqual(syncJobId, envelope.SyncJobId);
            Assert.AreEqual(1, readMembers.Count);
            Assert.AreEqual(memberId, readMembers.Single().ObjectId);

            var inMemoryStorage = new InMemoryBlobStorageRepository();
            await inMemoryStorage.UploadGroupMembershipFromGuidsAsync(
                path,
                new[] { memberId },
                new AzureADGroup { ObjectId = destinationId },
                runId,
                syncJobId,
                exclusionary: true,
                membershipObtainerDryRunEnabled: false,
                query);
            var productionJson = JsonNode.Parse(bytes);
            var inMemoryJson = JsonNode.Parse(inMemoryStorage.GetContent(path));

            Assert.IsTrue(
                JsonNode.DeepEquals(productionJson, inMemoryJson),
                $"The in-memory writer diverges from production.{Environment.NewLine}" +
                $"Production: {productionJson}{Environment.NewLine}" +
                $"In-memory: {inMemoryJson}");
        }

        /// <summary>
        /// I15: a failed agent-count query must not publish a measured AgentMembersRead value.
        /// Findings 29 and 30. GraphGroupMembershipReader.cs:142-205 converts a failed count query to zero,
        /// and SubOrchestratorFunction.cs:703-732 publishes that zero as a successful measurement.
        /// Expected failing assertion: no AgentMembersRead event is emitted and a warning records the failure.
        /// </summary>
        [TestMethod]
        [TestCategory("FailureModelRepro")]
        public async Task I15_AgentCountFailure_DoesNotEmitMeasuredZero()
        {
            const string graphApiBaseUrl = "https://graph.microsoft.com/v1.0";
            var requestAdapter = new Mock<IRequestAdapter>();
            requestAdapter.SetupProperty(
                adapter => adapter.BaseUrl,
                graphApiBaseUrl);
            requestAdapter
                .SetupGet(adapter => adapter.SerializationWriterFactory)
                .Returns(new JsonSerializationWriterFactory());
            requestAdapter
                .Setup(adapter => adapter.SendPrimitiveAsync<int?>(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (requestInformation, _, _) =>
                    {
                        var responseHandlerOption = requestInformation
                            .RequestOptions
                            .OfType<ResponseHandlerOption>()
                            .Single();
                        var responseHandler =
                            (NativeResponseHandler)responseHandlerOption.ResponseHandler;
                        responseHandler.Value = new HttpResponseMessage(
                            HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent(
                                "{}",
                                Encoding.UTF8,
                                "application/json")
                        };
                    })
                .ReturnsAsync((int?)null);
            var retryPolicyProvider = new Mock<IRetryPolicyProvider>();
            retryPolicyProvider
                .Setup(provider => provider.CreateRetryAfterPolicy(
                    It.IsAny<Guid?>()))
                .Returns(Policy.NoOpAsync<HttpResponseMessage>());
            retryPolicyProvider
                .Setup(provider => provider.CreateExceptionHandlingPolicy(
                    It.IsAny<Guid?>()))
                .Returns(Policy.NoOpAsync<HttpResponseMessage>());
            var repositoryLogger =
                new RecordingLogger<GraphGroupRepository>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory
                .Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                .Returns(repositoryLogger);
            var repository = new GraphGroupRepository(
                new GraphServiceClient(requestAdapter.Object, graphApiBaseUrl),
                new TelemetryClient(new TelemetryConfiguration()),
                retryPolicyProvider.Object,
                loggerFactory.Object);
            var request = MembershipRequest(identityTypes: null);
            var agentCount = await repository.GetAgentUserCountAsync(
                request.SourceGroup.ObjectId);
            var context = CreateBaseOrchestrationContext(request);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<string>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(FileDownloaderFunction)),
                        It.IsAny<FileDownloaderRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(string.Empty);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<string>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(MembersReaderFunction)),
                        It.IsAny<MembersReaderRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync((string)null);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<GroupMembershipFileResult>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(TransitiveAndDeltaUsersSenderFunction)),
                        It.IsAny<TransitiveAndDeltaUsersSenderRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(new GroupMembershipFileResult
                {
                    FilePath = "part.json",
                    MemberCount = 0
                });
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<int?>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GetAgentUserCountFunction)),
                        It.IsAny<GetAgentUserCountRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(agentCount);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync(
                        It.IsAny<TaskName>(),
                        It.IsAny<object>(),
                        It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);
            var telemetryChannel = new CapturingTelemetryChannel();
            var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
            telemetryConfiguration.TelemetryChannel = telemetryChannel;
            var function = new SubOrchestratorFunction(
                new FixedDeltaCachingConfig(false),
                new TelemetryClient(telemetryConfiguration));

            await function.RunSubOrchestratorAsync(context.Object);

            var failures = new List<string>();
            var events = telemetryChannel.GetEvents("AgentMembersRead");
            if (events.Count != 0)
            {
                var emittedCount = events[0].Properties["AgentCount"];
                failures.Add(
                    $"failure emitted AgentMembersRead with AgentCount={emittedCount}");
            }

            if (!repositoryLogger.Entries.Any(entry =>
                    entry.Level == LogLevel.Warning &&
                    entry.Message.Contains(
                        "agent count",
                        StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add("failure did not produce an agent-count warning");
            }

            Assert.AreEqual(
                0,
                failures.Count,
                string.Join("; ", failures));
        }

        /// <summary>
        /// I16 guard: both-types saved-delta bytes stay identical whether selection is omitted or explicit.
        /// FR-007 and SC-001. ProcessCachedAndDeltaUsersFunction.cs reads IdentityTypes but does not branch on it.
        /// Expected assertion: part, cache, and token contents are byte-identical.
        /// </summary>
        [TestMethod]
        [TestCategory("FailureModelRepro")]
        public async Task I16_BothTypesSavedDelta_PreservesLegacyBytes()
        {
            var sourceGroupId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();
            var retainedMember = Guid.NewGuid();
            var removedMember = Guid.NewGuid();
            var addedMember = Guid.NewGuid();
            var omittedSelection = await CreateProcessFixtureAsync(
                new[] { retainedMember, removedMember },
                new[] { addedMember },
                new[] { removedMember },
                expectedCount: 2,
                identityTypes: null,
                sourceGroupId,
                targetGroupId,
                runId,
                syncJobId);
            var explicitSelection = await CreateProcessFixtureAsync(
                new[] { retainedMember, removedMember },
                new[] { addedMember },
                new[] { removedMember },
                expectedCount: 2,
                identityTypes: new[] { "users", "agents" },
                sourceGroupId,
                targetGroupId,
                runId,
                syncJobId);

            var omittedResponse =
                await omittedSelection.Function.RunAsync(omittedSelection.Request);
            var explicitResponse =
                await explicitSelection.Function.RunAsync(explicitSelection.Request);
            var omittedArtifacts = CapturePublishedArtifacts(
                omittedSelection.Storage,
                omittedResponse.MembershipFilePath,
                sourceGroupId);
            var explicitArtifacts = CapturePublishedArtifacts(
                explicitSelection.Storage,
                explicitResponse.MembershipFilePath,
                sourceGroupId);

            CollectionAssert.AreEquivalent(
                omittedArtifacts.Keys.ToArray(),
                explicitArtifacts.Keys.ToArray());
            foreach (var artifact in omittedArtifacts)
            {
                Assert.AreEqual(
                    artifact.Value,
                    explicitArtifacts[artifact.Key],
                    $"Both-types output changed for {artifact.Key}.");
            }
        }

        /// <summary>
        /// I18 guard: every read kind records the destination's unfiltered before-sync count.
        /// Finding 38. TransitiveAndDeltaUsersSenderFunction.cs:31 disables the write only for one-type parts,
        /// and SGMembershipCalculator.cs:242-245 writes the merged unfiltered count for the destination part.
        /// Expected assertion: Full, DeltaInitial, and DeltaLink each persist the unfiltered count.
        /// </summary>
        [DataTestMethod]
        [DataRow((int)GroupMembershipReadKind.Full)]
        [DataRow((int)GroupMembershipReadKind.DeltaInitial)]
        [DataRow((int)GroupMembershipReadKind.DeltaLink)]
        [TestCategory("FailureModelRepro")]
        public async Task I18_DestinationBeforeSyncCount_IsUnfilteredForEveryReadKind(
            int readKindValue)
        {
            var groupId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                TargetOfficeGroupId = groupId,
                Query = "[{\"type\":\"GroupMembership\"}]",
                Status = SyncStatus.InProgress.ToString()
            };
            var storage = new InMemoryBlobStorageRepository();
            await storage.UploadFileAsync(
                $"{groupId}/userUploads/{runId}_GroupMembership_1_page.json",
                JsonSerializer.Serialize(new[]
                {
                    Member(groupId),
                    Member(groupId)
                }));
            var database = new Mock<IDatabaseSyncJobsRepository>();
            database
                .Setup(repository => repository.GetSyncJobAsync(job.Id))
                .ReturnsAsync(job);
            var destinationResolver = new Mock<IDestinationResolver>();
            destinationResolver
                .Setup(resolver => resolver.ResolveAsync(
                    job,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedGroupDestination
                {
                    ObjectId = groupId
                });
            var statusService = new Mock<ISyncJobStatusService>();
            var calculator = CreateCalculator(
                storage,
                database.Object,
                destinationResolver.Object,
                statusService.Object);
            var function = new TransitiveAndDeltaUsersSenderFunction(
                NullLogger<TransitiveAndDeltaUsersSenderFunction>.Instance,
                storage,
                calculator);

            await function.SendUsersAsync(new TransitiveAndDeltaUsersSenderRequest
            {
                SyncJob = job,
                ObjectId = groupId,
                GroupId = groupId,
                CurrentPart = 1,
                TotalParts = 1,
                IdentityTypes = null,
                ReadKind = (GroupMembershipReadKind)readKindValue
            });

            statusService.Verify(
                service => service.UpdateJobStatusAsync(
                    It.IsAny<SyncJob>(),
                    It.IsAny<SyncStatus?>(),
                    It.Is<Models.SyncJobHistory.SyncJobHistory>(
                        history => history.BeforeSyncUserCount == 2),
                    "GroupMembershipObtainer"),
                Times.Once);
        }

        private static async Task<ProcessFixture> CreateProcessFixtureAsync(
            IReadOnlyCollection<Guid> cachedMembers,
            IReadOnlyCollection<Guid> additions,
            IReadOnlyCollection<Guid> removals,
            int expectedCount,
            IReadOnlyCollection<string> identityTypes,
            Guid? sourceGroupId = null,
            Guid? targetGroupId = null,
            Guid? runId = null,
            Guid? syncJobId = null)
        {
            var storage = new InMemoryBlobStorageRepository();
            var effectiveSourceGroupId = sourceGroupId ?? Guid.NewGuid();
            var effectiveTargetGroupId = targetGroupId ?? Guid.NewGuid();
            var effectiveRunId = runId ?? Guid.NewGuid();
            var effectiveSyncJobId = syncJobId ?? Guid.NewGuid();
            var cachePath = $"cache/original-{effectiveSourceGroupId}.txt";
            await storage.UploadFileAsync(
                cachePath,
                string.Join(Environment.NewLine, cachedMembers) +
                (cachedMembers.Count == 0 ? string.Empty : Environment.NewLine));
            var addsPrefix =
                $"{effectiveTargetGroupId}/userUploads/deltaLink/adds/{effectiveRunId}_GroupMembership_1_";
            var removesPrefix =
                $"{effectiveTargetGroupId}/userUploads/deltaLink/removes/{effectiveRunId}_GroupMembership_1_";
            if (additions.Count > 0)
            {
                await storage.UploadFileAsync(
                    $"{addsPrefix}page.json",
                    JsonSerializer.Serialize(
                        additions.Select(id => new AzureADUser
                        {
                            ObjectId = id
                        }).ToArray()));
            }

            if (removals.Count > 0)
            {
                await storage.UploadFileAsync(
                    $"{removesPrefix}page.json",
                    JsonSerializer.Serialize(
                        removals.Select(id => new AzureADUser
                        {
                            ObjectId = id
                        }).ToArray()));
            }

            var request = new ProcessCachedAndDeltaUsersRequest
            {
                SyncJob = new SyncJob
                {
                    Id = effectiveSyncJobId,
                    RunId = effectiveRunId,
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                SourceGroupId = effectiveSourceGroupId,
                TargetGroupId = effectiveTargetGroupId,
                CacheFilePath = cachePath,
                DeltaUrl = "replacement-delta-token",
                CountOfUsersFromAADGroup = expectedCount,
                CurrentPart = 1,
                TotalParts = 1,
                IdentityTypes = identityTypes?.ToList(),
                WriteStaging = false,
                ReadKind = GroupMembershipReadKind.DeltaLink
            };
            var function = new ProcessCachedAndDeltaUsersFunction(
                NullLogger<ProcessCachedAndDeltaUsersFunction>.Instance,
                CreateCalculator(storage),
                storage);
            return new ProcessFixture(function, storage, request);
        }

        private static SGMembershipCalculator CreateCalculator(
            IBlobStorageRepository storage,
            IDatabaseSyncJobsRepository database = null,
            IDestinationResolver destinationResolver = null,
            ISyncJobStatusService statusService = null)
        {
            return new SGMembershipCalculator(
                Mock.Of<IGraphGroupRepository>(),
                storage,
                database ?? Mock.Of<IDatabaseSyncJobsRepository>(),
                destinationResolver ?? Mock.Of<IDestinationResolver>(),
                Mock.Of<IServiceBusQueueRepository>(),
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                NullLogger<SGMembershipCalculator>.Instance,
                Mock.Of<IDryRunValue>(),
                statusService ?? Mock.Of<ISyncJobStatusService>());
        }

        private static Mock<TaskOrchestrationContext> CreateBaseOrchestrationContext(
            GroupMembershipRequest request)
        {
            var context = new Mock<TaskOrchestrationContext>();
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.GetInput<GroupMembershipRequest>())
                .Returns(request);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CreateReplaySafeLogger(
                        It.IsAny<string>()))
                .Returns(NullLogger.Instance);
            context
                .SetupGet(orchestrationContext => orchestrationContext.IsReplaying)
                .Returns(false);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<bool>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GroupValidatorFunction)),
                        It.IsAny<GroupValidatorRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(true);
            context
                .Setup(orchestrationContext =>
                    orchestrationContext.CallActivityAsync<int>(
                        It.Is<TaskName>(name =>
                            name.Name == nameof(GetTransitiveGroupCountFunction)),
                        It.IsAny<GetTransitiveGroupCountRequest>(),
                        It.IsAny<TaskOptions>()))
                .ReturnsAsync(0);
            return context;
        }

        private static GroupMembershipRequest MembershipRequest(
            IReadOnlyCollection<string> identityTypes)
        {
            return new GroupMembershipRequest
            {
                SyncJob = new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                SourceGroup = new AzureADGroup
                {
                    ObjectId = Guid.NewGuid()
                },
                GroupId = Guid.NewGuid(),
                CurrentPart = 1,
                TotalParts = 1,
                IdentityTypes = identityTypes?.ToList()
            };
        }

        private static Dictionary<string, string> CapturePublishedArtifacts(
            InMemoryBlobStorageRepository storage,
            string membershipPath,
            Guid sourceGroupId)
        {
            var cachePath = storage.Paths
                .Where(path => path.StartsWith(
                    CacheFileNaming.BuildCacheFileNamePrefix(sourceGroupId),
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Last();
            var tokenPath = storage.Paths
                .Where(path => path.StartsWith(
                    $"cache/delta_{sourceGroupId}_",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Last();
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["part"] = storage.GetContent(membershipPath),
                ["cache"] = storage.GetContent(cachePath),
                ["token"] = storage.GetContent(tokenPath)
            };
        }

        private static GroupMembership ReadMembership(string content) =>
            JsonSerializer.Deserialize<GroupMembership>(
                content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        private static AzureADUser Member(Guid sourceGroupId) =>
            new()
            {
                ObjectId = Guid.NewGuid(),
                SourceGroup = sourceGroupId
            };

        private sealed record ProcessFixture(
            ProcessCachedAndDeltaUsersFunction Function,
            InMemoryBlobStorageRepository Storage,
            ProcessCachedAndDeltaUsersRequest Request);

        private sealed class FixedDeltaCachingConfig : IDeltaCachingConfig
        {
            public FixedDeltaCachingConfig(bool enabled)
            {
                DeltaCacheEnabled = enabled;
            }

            public bool DeltaCacheEnabled { get; }
        }

        private sealed class CapturingTelemetryChannel : ITelemetryChannel
        {
            private readonly List<EventTelemetry> _events = new();

            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; }

            public void Send(ITelemetry item)
            {
                if (item is EventTelemetry eventTelemetry)
                {
                    _events.Add(eventTelemetry);
                }
            }

            public IReadOnlyList<EventTelemetry> GetEvents(string name) =>
                _events.Where(eventTelemetry =>
                    eventTelemetry.Name == name).ToList();

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
