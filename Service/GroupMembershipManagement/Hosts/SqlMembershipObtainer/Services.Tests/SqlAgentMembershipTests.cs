// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.SqlMembershipRepository;
using Services.Contracts;
using SqlMembershipObtainer;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class SqlAgentMembershipTests
    {
        private const string RunTableName = "0f8fad5bd9cb469fa16570867728950e";
        private const string RunTableAdfRunId = "0f8fad5b-d9cb-469f-a165-70867728950e";

        private static readonly string[] _invalidRawShapes =
        {
            """{"type":"SqlMembership","identityTypes":null}""",
            """{"type":"SqlMembership","identityTypes":"users"}""",
            """{"type":"SqlMembership","identityTypes":123}""",
            """{"type":"SqlMembership","identityTypes":true}""",
            """{"type":"SqlMembership","identityTypes":{"users":true}}""",
            """{"type":"SqlMembership","identityTypes":[1,2]}""",
            """{"type":"SqlMembership","identityTypes":["users",null]}""",
            """{"type":"SqlMembership","identityTypes":[["users"]]}""",
        };

        [TestMethod]
        public void IdentityTypes_RejectsInvalidRawShapes()
        {
            foreach (var queryPartJson in _invalidRawShapes)
            {
                var queryPart = ParseQueryPart(queryPartJson);

                var parsed = IdentityTypeSelection.TryParse(
                    queryPart,
                    IdentityTypeSet.UsersOnly,
                    out var selection,
                    out var error);

                Assert.IsFalse(parsed, queryPartJson);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error), queryPartJson);
                Assert.AreEqual(default, selection, queryPartJson);
            }
        }

        [TestMethod]
        public void IdentityTypes_RejectsMalformedOrUndecodableJson()
        {
            var malformed = """{"type":"SqlMembership","identityTypes":["users"]""";
            var escapedUnpairedSurrogate = """{"type":"SqlMembership","identityTypes":["\uD800"]}""";
            var rawUnpairedSurrogate = "{\"type\":\"SqlMembership\",\"identityTypes\":[\"\uD800\"]}";

            AssertRejected(malformed);
            AssertRejected(escapedUnpairedSurrogate);
            AssertRejected(rawUnpairedSurrogate);
        }

        [TestMethod]
        public void IdentityTypes_RejectsMisplacedSelectionAtAnyDepth()
        {
            var nestedUnderSource =
                """{"type":"SqlMembership","source":{"identityTypes":["agents"]}}""";
            var recursivelyNested =
                """{"type":"SqlMembership","source":{"rules":[{"identityTypes":["agents"]}]}}""";

            AssertRejected(nestedUnderSource);
            AssertRejected(recursivelyNested);
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(nestedUnderSource));
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(recursivelyNested));
        }

        [TestMethod]
        public void IdentityTypes_OmissionUsesTheCallerDefault()
        {
            var queryPart = ParseQueryPart(
                """{"type":"SqlMembership","source":{"manager":{"id":1}}}""");

            var parsed = IdentityTypeSelection.TryParse(
                queryPart,
                IdentityTypeSet.Of(IdentityType.Agents),
                out var selection,
                out var error);

            Assert.IsTrue(parsed, error);
            Assert.IsNull(error);
            Assert.IsFalse(selection.Includes(IdentityType.Users));
            Assert.IsTrue(selection.Includes(IdentityType.Agents));
            Assert.IsFalse(IdentityTypeSelection.IsExplicitlySelected(queryPart));
        }

        [TestMethod]
        public void IdentityTypes_ParsesSupportedSelections()
        {
            var supportedSelections = new[]
            {
                (IdentityTypesJson: """["users"]""", Expected: IdentityTypeSet.UsersOnly),
                (IdentityTypesJson: """["agents"]""", Expected: IdentityTypeSet.Of(IdentityType.Agents)),
                (IdentityTypesJson: """["users","agents"]""", Expected: IdentityTypeSet.All),
                (IdentityTypesJson: """["agents","users"]""", Expected: IdentityTypeSet.All),
            };

            foreach (var (identityTypesJson, expected) in supportedSelections)
            {
                var queryPart = ParseQueryPart(
                    $$"""{"type":"SqlMembership","identityTypes":{{identityTypesJson}}}""");

                var parsed = IdentityTypeSelection.TryParse(
                    queryPart,
                    IdentityTypeSet.UsersOnly,
                    out var selection,
                    out var error);

                Assert.IsTrue(parsed, error);
                Assert.IsNull(error);
                Assert.AreEqual(expected, selection);
                Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(queryPart));
            }
        }

        [TestMethod]
        public void IdentityTypes_ParserDelegatesValueRules()
        {
            AssertRejected("""{"type":"SqlMembership","identityTypes":[]}""");
            AssertRejected("""{"type":"SqlMembership","identityTypes":["users","users"]}""");
            AssertRejected("""{"type":"SqlMembership","identityTypes":["Users"]}""");
            AssertRejected("""{"type":"SqlMembership","identityTypes":["devices"]}""");
        }

        [TestMethod]
        public void IdentityTypes_MalformedInputCannotLookOmitted()
        {
            Assert.IsFalse(IdentityTypeSelection.IsExplicitlySelected(string.Empty));
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected("{"));
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(
                """{"type":"SqlMembership","identityTypes":null}"""));
            Assert.IsTrue(IdentityTypeSelection.IsExplicitlySelected(
                """{"type":"SqlMembership","source":{"identityTypes":["agents"]}}"""));
        }

        [TestMethod]
        public void EvaluationResult_AllowsAgentsOnlyProjectionToRetainSelectedHumanCount()
        {
            var agentObjectId = Guid.NewGuid();

            var result = new SqlMembershipEvaluationResult(
                Array.Empty<Guid>(),
                new[] { agentObjectId, agentObjectId },
                selectedHumanCount: 3,
                new[] { IdentityType.Users });

            Assert.AreEqual(0, result.UserObjectIds.Count);
            CollectionAssert.AreEqual(
                new[] { agentObjectId },
                result.AgentObjectIds.ToArray());
            Assert.AreEqual(3, result.SelectedHumanCount);
        }

        [TestMethod]
        public void EvaluationResult_RejectsASelectedHumanCountBelowEmittedUsers()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                new SqlMembershipEvaluationResult(
                    new[] { Guid.NewGuid() },
                    Array.Empty<Guid>(),
                    selectedHumanCount: 0,
                    Array.Empty<IdentityType>()));
        }

        [TestMethod]
        public void EvaluationResult_RejectsANegativeSkippedAgentRowCount()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                new SqlMembershipEvaluationResult(
                    Array.Empty<Guid>(),
                    Array.Empty<Guid>(),
                    selectedHumanCount: 0,
                    Array.Empty<IdentityType>(),
                    skippedAgentRowCount: -1));
        }

        [TestMethod]
        public void EvaluationResult_CopiesDistinctSetsAndStillRejectsAnEmptyAgentId()
        {
            var userObjectIds = new HashSet<Guid> { Guid.NewGuid(), Guid.Empty, Guid.NewGuid() };
            var agentObjectIds = new HashSet<Guid> { Guid.NewGuid() };

            var result = new SqlMembershipEvaluationResult(
                userObjectIds,
                agentObjectIds,
                selectedHumanCount: userObjectIds.Count,
                Array.Empty<IdentityType>());

            CollectionAssert.AreEqual(userObjectIds.ToArray(), result.UserObjectIds.ToArray());
            CollectionAssert.AreEqual(agentObjectIds.ToArray(), result.AgentObjectIds.ToArray());
            Assert.ThrowsException<ArgumentException>(() =>
                new SqlMembershipEvaluationResult(
                    Array.Empty<Guid>(),
                    new HashSet<Guid> { Guid.NewGuid(), Guid.Empty },
                    selectedHumanCount: 0,
                    Array.Empty<IdentityType>()));
        }

        [TestMethod]
        public void EvaluationResult_RemovesDuplicatesFromASetWithItsOwnComparer()
        {
            var objectId = Guid.NewGuid();
            var setThatKeepsDuplicates = new HashSet<Guid>(new NeverEqualGuidComparer()) { objectId, objectId };

            var result = new SqlMembershipEvaluationResult(
                setThatKeepsDuplicates,
                Array.Empty<Guid>(),
                selectedHumanCount: 2,
                Array.Empty<IdentityType>());

            Assert.AreEqual(2, setThatKeepsDuplicates.Count);
            CollectionAssert.AreEqual(new[] { objectId }, result.UserObjectIds.ToArray());
        }

        private sealed class NeverEqualGuidComparer : IEqualityComparer<Guid>
        {
            public bool Equals(Guid x, Guid y) => false;

            public int GetHashCode(Guid obj) => obj.GetHashCode();
        }

        [TestMethod]
        public void EvaluationRequest_RejectsIncompleteExecutionContext()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                new SqlMembershipEvaluationRequest(
                    managerEmployeeId: null,
                    depth: 0,
                    filter: string.Empty,
                    identityTypes: default,
                    tableName: "run123"));
            Assert.ThrowsException<ArgumentException>(() =>
                new SqlMembershipEvaluationRequest(
                    managerEmployeeId: null,
                    depth: 0,
                    filter: string.Empty,
                    IdentityTypeSet.UsersOnly,
                    tableName: string.Empty));
        }

        [TestMethod]
        public void EvaluationRequest_ReadsAdfRunIdFromTableName()
        {
            var runTableRequest = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter: string.Empty,
                IdentityTypeSet.UsersOnly,
                tableName: RunTableName);
            var otherTableRequest = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter: string.Empty,
                IdentityTypeSet.UsersOnly,
                tableName: "run123");

            Assert.AreEqual(Guid.Parse(RunTableAdfRunId), runTableRequest.AdfRunId);
            Assert.IsNull(otherTableRequest.AdfRunId);
        }

        [TestMethod]
        public void EvaluationRequest_FromIdentityTypeNamesDefaultsLegacyPayloadsToUsers()
        {
            var request = SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                managerEmployeeId: 42,
                depth: 2,
                filter: "Department = 'Sales'",
                identityTypes: null,
                tableName: RunTableName);

            Assert.AreEqual(IdentityTypeSet.UsersOnly, request.IdentityTypes);
            Assert.AreEqual(RunTableName, request.TableName);
            Assert.AreEqual(Guid.Parse(RunTableAdfRunId), request.AdfRunId);
        }

        [TestMethod]
        public void UsersOnlyUserFilter_DoesNotReadOrProbeAgents()
        {
            const string legacyFilter = "Department = 'Sales'";

            var legacyPlans = new string[2];
            var typeNameSelections = new string[][]
            {
                null,
                new[] { "users" },
            };

            for (var index = 0; index < typeNameSelections.Length; index++)
            {
                var request = SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                    managerEmployeeId: null,
                    depth: 0,
                    legacyFilter,
                    typeNameSelections[index],
                    tableName: "run123");
                var plan = SqlMembershipQueryBuilder.BuildEvaluation(
                    request,
                    SqlFilterTestHelpers.Analyze(legacyFilter));

                Assert.AreEqual(IdentityTypeSet.UsersOnly, request.IdentityTypes);
                Assert.IsFalse(plan.RequiresAgents);
                Assert.IsFalse(
                    plan.CommandText.Contains(
                        "[agents]",
                        StringComparison.Ordinal));
                legacyPlans[index] = plan.CommandText;
            }

            Assert.AreEqual(
                legacyPlans[0],
                legacyPlans[1],
                "Omitted and explicit users-only selections must produce the same human query.");

            const string qualifiedFilter = "users.Department = 'Sales'";

            var qualifiedRequest =
                SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                    managerEmployeeId: null,
                    depth: 0,
                    qualifiedFilter,
                    new[] { "users" },
                    tableName: "run123");
            var qualifiedPlan = SqlMembershipQueryBuilder.BuildEvaluation(
                qualifiedRequest,
                SqlFilterTestHelpers.Analyze(qualifiedFilter));

            Assert.IsFalse(qualifiedPlan.RequiresAgents);
            Assert.IsFalse(
                qualifiedPlan.CommandText.Contains(
                    "[agents]",
                    StringComparison.Ordinal));
        }

        [TestMethod]
        public void EvaluationRequest_FromIdentityTypeNamesRejectsInvalidCarriedValues()
        {
            var invalidSelections = new[]
            {
                Array.Empty<string>(),
                new[] { "users", "users" },
                new[] { "Users" },
                new[] { "devices" },
                new[] { "users", null },
            };

            foreach (var selection in invalidSelections)
            {
                Assert.ThrowsException<InvalidOperationException>(() =>
                    SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                        managerEmployeeId: null,
                        depth: 0,
                        filter: string.Empty,
                        identityTypes: selection,
                        tableName: "run123"));
            }
        }

        [TestMethod]
        public async Task IdentityTypes_SqlSchemaAcceptsSupportedSelections()
        {
            foreach (var selection in new[] { "[\"users\"]", "[\"agents\"]", "[\"users\",\"agents\"]", "[\"agents\",\"users\"]" })
            {
                var part = "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":" + selection + "}";

                Assert.IsTrue(
                    await ValidatePartAsync(part),
                    $"expected {selection} to be accepted");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_SqlSchemaRejectsInvalidSelections()
        {
            var invalidParts = new[]
            {
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[]}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":null}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":\"users\"}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"users\",\"users\"]}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"Users\"]}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"devices\"]}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[1,2]}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1},\"identityTypes\":[\"users\"]}}",
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"IdentityTypes\":[\"agents\"]}",
            };

            foreach (var part in invalidParts)
            {
                Assert.IsFalse(
                    await ValidatePartAsync(part),
                    $"expected rejection: {part}");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_SqlSchemaPreservesExistingSourceValidation()
        {
            Assert.IsFalse(
                await ValidatePartAsync(
                    "{\"type\":\"SqlMembership\",\"source\":{},\"identityTypes\":[\"users\"]}"));
            Assert.IsTrue(
                await ValidatePartAsync(
                    "{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"EmployeeType_Code = 'FTE'\"},\"identityTypes\":[\"agents\"]}"));
        }

        [TestMethod]
        public async Task IdentityTypes_SqlSchemaFailsClosedWhenSchemaIsMissing()
        {
            var selectedPart = "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]}";
            var wrongCasePart = "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"IdentityTypes\":[\"agents\"]}";
            var legacyPart = "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}}}";

            var emptyProvider = new SchemaProvider();
            Assert.IsFalse(await ValidatePartAsync(selectedPart, emptyProvider));
            Assert.IsFalse(await ValidatePartAsync(wrongCasePart, emptyProvider));
            Assert.IsTrue(await ValidatePartAsync(legacyPart, emptyProvider));

            var providerWithoutSqlSchema = new SchemaProvider();
            providerWithoutSqlSchema.Schemas.Add(
                Schema.GroupMembershipSchema,
                "{\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"type\":\"object\"}");

            Assert.IsFalse(
                await ValidatePartAsync(selectedPart, providerWithoutSqlSchema));
            Assert.IsFalse(
                await ValidatePartAsync(wrongCasePart, providerWithoutSqlSchema));
            Assert.IsTrue(
                await ValidatePartAsync(legacyPart, providerWithoutSqlSchema));
        }

        [TestMethod]
        public async Task EvaluationRetry_DiscardsPartiallyMaterializedAttempt()
        {
            var staleUser = Guid.NewGuid();
            var staleAgent = Guid.NewGuid();
            var successfulUser = Guid.NewGuid();
            var successfulAgent = Guid.NewGuid();
            var attemptCount = 0;

            var selected =
                await SqlMembershipRepository.ExecuteWithFreshEvaluationBuffersAsync(
                    async operation =>
                    {
                        while (true)
                        {
                            try
                            {
                                await operation();
                                return;
                            }
                            catch (InvalidOperationException) when (attemptCount == 1)
                            {
                            }
                        }
                    },
                    (users, agents) =>
                    {
                        attemptCount++;

                        if (attemptCount == 1)
                        {
                            users.Add(staleUser);
                            agents.Add(staleAgent);
                            throw new InvalidOperationException(
                                "Injected retry after partial materialization.");
                        }

                        users.Add(successfulUser);
                        agents.Add(successfulAgent);
                        return Task.CompletedTask;
                    });

            Assert.AreEqual(2, attemptCount);
            CollectionAssert.AreEquivalent(
                new[] { successfulUser },
                selected.Users.ToArray());
            CollectionAssert.AreEquivalent(
                new[] { successfulAgent },
                selected.Agents.ToArray());
        }

        [TestMethod]
        public async Task EvaluationRetry_PermanentFailureAfterPartialMaterializationReturnsNoResult()
        {
            var partialUser = Guid.NewGuid();
            var partialAgent = Guid.NewGuid();
            var originalException =
                new InvalidOperationException(
                    "Injected permanent failure after partial materialization.");

            var thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => SqlMembershipRepository.ExecuteWithFreshEvaluationBuffersAsync(
                    operation => operation(),
                    (users, agents) =>
                    {
                        users.Add(partialUser);
                        agents.Add(partialAgent);
                        throw originalException;
                    }));

            Assert.AreSame(originalException, thrown);
        }

        [DataTestMethod]
        [DataRow("missing required agents table")]
        [DataRow("denied agents read")]
        [DataRow("missing required agents column")]
        [DataRow("malformed selected agent identifier")]
        [DataRow("failure after partial materialization")]
        [DataRow("agents available only for another run")]
        [DataRow("empty users scope with unavailable agents input")]
        public async Task RequiredInputOrEvaluationFailure_DoesNotUploadOrEnqueue(
            string failureMode)
        {
            var syncJob = CreateSyncJob();
            var service = new Mock<ISqlMembershipObtainerService>(MockBehavior.Strict);
            service
                .Setup(x => x.EvaluateMembershipAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>(),
                    syncJob,
                    syncJob.Group.GroupId,
                    1))
                .ThrowsAsync(new InvalidOperationException(failureMode));

            var activity = new ChildEntitiesFilterFunction(
                NullLogger<ChildEntitiesFilterFunction>.Instance,
                service.Object);
            var activityRequest = new ChildEntitiesFilterRequest
            {
                Query = "users.EmployeeType_Code = 'FTE'",
                TableName = "sameRunTable",
                GroupId = syncJob.Group.GroupId,
                SyncJob = syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                Exclusionary = false,
                IdentityTypes = new[] { "users", "agents" },
            };

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => activity.FilterChildEntities(activityRequest));

            service.Verify(
                x => x.UploadMembershipEvaluationAsync(
                    It.IsAny<SqlMembershipEvaluationResult>(),
                    It.IsAny<SyncJob>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>()),
                Times.Never());

            var context = new Mock<TaskOrchestrationContext>();
            context
                .Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>()))
                .Returns(NullLogger.Instance);
            context
                .Setup(x => x.GetInput<OrchestratorRequest>())
                .Returns(new OrchestratorRequest
                {
                    CurrentPart = 1,
                    TotalParts = 1,
                    SyncJob = syncJob,
                    Exclusionary = false,
                });
            context
                .Setup(x => x.CallActivityAsync<bool>(
                    nameof(SchemaValidatorFunction),
                    It.IsAny<SchemaValidatorRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(true);
            context
                .Setup(x => x.CallActivityAsync<Guid>(
                    nameof(GetGroupFunction),
                    It.IsAny<GetGroupRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(syncJob.Group.GroupId);
            context
                .Setup(x => x.CallSubOrchestratorAsync<MembershipFileResult>(
                    nameof(OrganizationProcessorFunction),
                    It.IsAny<OrganizationProcessorRequest>(),
                    It.IsAny<TaskOptions>()))
                .ThrowsAsync(new InvalidOperationException(failureMode));
            context
                .Setup(x => x.CallActivityAsync(
                    nameof(JobStatusUpdaterFunction),
                    It.IsAny<JobStatusUpdaterRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);
            context
                .Setup(x => x.CallActivityAsync(
                    nameof(TelemetryTrackerFunction),
                    It.IsAny<TelemetryTrackerRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

            await new OrchestratorFunction().RunOrchestratorAsync(context.Object);

            context.Verify(
                x => x.CallActivityAsync(
                    nameof(QueueMessageSenderFunction),
                    It.IsAny<MembershipAggregatorHttpRequest>(),
                    It.IsAny<TaskOptions>()),
                Times.Never());
            context.Verify(
                x => x.CallActivityAsync(
                    nameof(JobStatusUpdaterFunction),
                    It.Is<JobStatusUpdaterRequest>(
                        request => request.Status == SyncStatus.Error),
                    It.IsAny<TaskOptions>()),
                Times.Once());
            context.Verify(
                x => x.CallActivityAsync(
                    nameof(TelemetryTrackerFunction),
                    It.Is<TelemetryTrackerRequest>(
                        request =>
                            request.JobStatus == SyncStatus.Error &&
                            request.ResultStatus == ResultStatus.Failure),
                    It.IsAny<TaskOptions>()),
                Times.Once());
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(2)]
        public async Task LegitimateEmptyEvaluation_PublishesOneEmptyFile(
            int selectedHumanCount)
        {
            var syncJob = CreateSyncJob();
            var evaluation = new SqlMembershipEvaluationResult(
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                selectedHumanCount,
                Array.Empty<IdentityType>());

            var published = await PublishEvaluationAsync(
                evaluation,
                syncJob,
                syncJob.Group.GroupId,
                exclusionary: false);

            Assert.AreEqual(0, published.SourceMembers.Count);
            Assert.AreEqual(syncJob.Group.GroupId, published.Destination.ObjectId);
        }

        [TestMethod]
        public async Task UploadMembershipEvaluation_IsOrderIndependentAndPublishesDistinctUnion()
        {
            var userObjectId = Guid.Parse("00000000-0000-0000-0000-000000000100");
            var agentObjectId = Guid.Parse("00000000-0000-0000-0000-000000000200");
            var sharedObjectId = Guid.Parse("00000000-0000-0000-0000-000000000300");
            var syncJob = CreateSyncJob();

            var usersFirst = new SqlMembershipEvaluationResult(
                new[] { sharedObjectId, userObjectId },
                new[] { sharedObjectId, agentObjectId },
                selectedHumanCount: 2,
                new[] { IdentityType.Users, IdentityType.Agents });
            var agentsFirst = new SqlMembershipEvaluationResult(
                new[] { userObjectId, sharedObjectId },
                new[] { agentObjectId, sharedObjectId },
                selectedHumanCount: 2,
                new[] { IdentityType.Agents, IdentityType.Users });

            var firstPublication = await PublishEvaluationAsync(
                usersFirst,
                syncJob,
                syncJob.Group.GroupId,
                exclusionary: true);
            var secondPublication = await PublishEvaluationAsync(
                agentsFirst,
                syncJob,
                syncJob.Group.GroupId,
                exclusionary: true);

            var firstIdentifiers = firstPublication.SourceMembers
                .Select(member => member.ObjectId)
                .ToArray();
            var secondIdentifiers = secondPublication.SourceMembers
                .Select(member => member.ObjectId)
                .ToArray();

            CollectionAssert.AreEqual(firstIdentifiers, secondIdentifiers);
            CollectionAssert.AreEquivalent(
                new[] { userObjectId, agentObjectId, sharedObjectId },
                firstIdentifiers);
            Assert.AreEqual(3, firstIdentifiers.Length);
            Assert.IsTrue(firstPublication.Exclusionary);
            Assert.AreEqual(syncJob.Id, firstPublication.SyncJobId);
            Assert.AreEqual(syncJob.RunId, firstPublication.RunId);
            Assert.AreEqual(syncJob.Query, firstPublication.Query);
            Assert.IsFalse(firstPublication.MembershipObtainerDryRunEnabled);
        }

        [TestMethod]
        public async Task UploadMembershipEvaluation_AgentsDeselectionPublishesAuthoritativeUsersOnlySnapshot()
        {
            var userObjectId = Guid.NewGuid();
            var agentObjectId = Guid.NewGuid();
            var syncJob = CreateSyncJob();

            var priorCombinedSnapshot = new SqlMembershipEvaluationResult(
                new[] { userObjectId },
                new[] { agentObjectId },
                selectedHumanCount: 1,
                Array.Empty<IdentityType>());
            var usersOnlySnapshot = new SqlMembershipEvaluationResult(
                new[] { userObjectId },
                Array.Empty<Guid>(),
                selectedHumanCount: 1,
                Array.Empty<IdentityType>());

            var priorPublication = await PublishEvaluationAsync(
                priorCombinedSnapshot,
                syncJob,
                syncJob.Group.GroupId,
                exclusionary: false);
            var usersOnlyPublication = await PublishEvaluationAsync(
                usersOnlySnapshot,
                syncJob,
                syncJob.Group.GroupId,
                exclusionary: false);

            Assert.AreEqual(
                syncJob.Group.GroupId,
                priorPublication.Destination.ObjectId);
            Assert.AreEqual(
                syncJob.Group.GroupId,
                usersOnlyPublication.Destination.ObjectId);
            CollectionAssert.AreEqual(
                new[] { userObjectId },
                usersOnlyPublication.SourceMembers
                    .Select(member => member.ObjectId)
                    .ToArray());
            Assert.IsFalse(
                usersOnlyPublication.SourceMembers.Any(
                    member => member.ObjectId == agentObjectId));
        }

        [TestMethod]
        public async Task EvaluationCompletion_LogContainsCorrelatedCountsWithoutSensitiveInput()
        {
            var memberObjectId = Guid.NewGuid();
            var result = new SqlMembershipEvaluationResult(
                new[] { memberObjectId },
                new[] { Guid.NewGuid() },
                selectedHumanCount: 3,
                new[] { IdentityType.Agents, IdentityType.Users });
            var repository = new Mock<ISqlMembershipRepository>(MockBehavior.Strict);
            repository
                .Setup(x => x.EvaluateMembershipAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>()))
                .ReturnsAsync(result);

            var logger = new RecordingLogger<SqlMembershipObtainerService>();
            var syncJob = CreateSyncJob();
            const string literalValue = "private-literal-value";
            syncJob.Query =
                $"Server=private-server;CredentialMaterial=private-credential;Member={memberObjectId:D}";
            var request = SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                managerEmployeeId: null,
                depth: 0,
                $"agents.BlueprintId = '{literalValue}'",
                new[] { "agents", "users" },
                tableName: RunTableName);
            var service = CreateService(
                repository.Object,
                new Mock<IBlobStorageRepository>().Object,
                logger);

            await service.EvaluateMembershipAsync(
                request,
                syncJob,
                syncJob.Group.GroupId,
                currentPart: 4);

            var completion = logger.Records.Single(
                record => record.EventId.Id == 160072);

            AssertLogValue(completion, "IdentityTypes", "users,agents");
            AssertLogValue(completion, "ReferencedIdentityTypes", "users,agents");
            AssertLogValue(completion, "SelectedHumanCount", 3);
            AssertLogValue(completion, "EmittedUserCount", 1);
            AssertLogValue(completion, "EmittedAgentCount", 1);
            AssertLogValue(completion, "JobId", syncJob.Id);
            AssertLogValue(completion, "SyncRunId", syncJob.RunId);
            AssertLogValue(completion, "CurrentPart", 4);
            AssertLogValue(completion, "AdfRunId", RunTableAdfRunId);
            Assert.IsFalse(completion.Properties.ContainsKey("OrphanCount"));
            Assert.IsFalse(completion.Properties.ContainsKey("ReadDuration"));
            Assert.IsFalse(
                logger.Records.Any(record => record.EventId.Id == 160074));
            AssertDoesNotContain(
                completion,
                request.Filter,
                literalValue,
                memberObjectId.ToString("D"),
                "private-server",
                "private-credential",
                "CredentialMaterial=");
        }

        [TestMethod]
        public async Task SkippedAgentRows_AreLoggedAsWarningAndEvaluationSucceeds()
        {
            var agentObjectId = Guid.NewGuid();
            var result = new SqlMembershipEvaluationResult(
                Array.Empty<Guid>(),
                new[] { agentObjectId },
                selectedHumanCount: 2,
                Array.Empty<IdentityType>(),
                skippedAgentRowCount: 2);
            var repository = new Mock<ISqlMembershipRepository>(MockBehavior.Strict);
            repository
                .Setup(x => x.EvaluateMembershipAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>()))
                .ReturnsAsync(result);

            var logger = new RecordingLogger<SqlMembershipObtainerService>();
            var syncJob = CreateSyncJob();
            var request = SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                managerEmployeeId: null,
                depth: 0,
                string.Empty,
                new[] { "agents" },
                tableName: RunTableName);
            var service = CreateService(
                repository.Object,
                new Mock<IBlobStorageRepository>().Object,
                logger);

            var returned = await service.EvaluateMembershipAsync(
                request,
                syncJob,
                syncJob.Group.GroupId,
                currentPart: 2);

            Assert.AreSame(result, returned);
            Assert.IsTrue(
                logger.Records.Any(record => record.EventId.Id == 160072));
            Assert.IsFalse(
                logger.Records.Any(record => record.EventId.Id == 160073));

            var warning = logger.Records.Single(
                record => record.EventId.Id == 160074);
            Assert.AreEqual(LogLevel.Warning, warning.LogLevel);
            AssertLogValue(warning, "SkippedAgentRowCount", 2);
            AssertLogValue(warning, "TableName", request.TableName);
            AssertLogValue(warning, "JobId", syncJob.Id);
            AssertLogValue(warning, "SyncRunId", syncJob.RunId);
            AssertLogValue(warning, "CurrentPart", 2);
            AssertLogValue(warning, "AdfRunId", RunTableAdfRunId);
        }

        [TestMethod]
        public async Task EvaluationFailure_TracksOriginalExceptionWithoutSuccessCounts()
        {
            var originalException =
                new InvalidOperationException("Required agents input is unavailable.");
            var repository = new Mock<ISqlMembershipRepository>(MockBehavior.Strict);
            repository
                .Setup(x => x.EvaluateMembershipAsync(
                    It.IsAny<SqlMembershipEvaluationRequest>()))
                .ThrowsAsync(originalException);

            var logger = new RecordingLogger<SqlMembershipObtainerService>();
            var telemetryChannel = new RecordingTelemetryChannel();
            using var telemetryConfiguration = new TelemetryConfiguration
            {
                ConnectionString =
                    "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                TelemetryChannel = telemetryChannel,
            };
            var telemetryClient = new TelemetryClient(telemetryConfiguration);
            var syncJob = CreateSyncJob();
            const string literalValue = "private-literal-value";
            syncJob.Query =
                "Server=private-server;CredentialMaterial=private-credential";
            var memberObjectId = Guid.NewGuid();
            var request = SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                managerEmployeeId: null,
                depth: 0,
                $"agents.BlueprintId = '{literalValue}' AND users.AzureObjectId = '{memberObjectId:D}'",
                new[] { "users", "agents" },
                tableName: RunTableName);
            var service = CreateService(
                repository.Object,
                new Mock<IBlobStorageRepository>().Object,
                logger,
                telemetryClient);

            var thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => service.EvaluateMembershipAsync(
                    request,
                    syncJob,
                    syncJob.Group.GroupId,
                    currentPart: 4));

            Assert.AreSame(originalException, thrown);
            Assert.IsFalse(
                logger.Records.Any(record => record.EventId.Id == 160072));

            var failure = logger.Records.Single(
                record => record.EventId.Id == 160073);
            Assert.AreSame(originalException, failure.Exception);
            AssertLogValue(failure, "IdentityTypes", "users,agents");
            AssertLogValue(failure, "JobId", syncJob.Id);
            AssertLogValue(failure, "SyncRunId", syncJob.RunId);
            AssertLogValue(failure, "CurrentPart", 4);
            AssertLogValue(failure, "AdfRunId", RunTableAdfRunId);
            Assert.IsFalse(failure.Properties.ContainsKey("SelectedHumanCount"));
            Assert.IsFalse(failure.Properties.ContainsKey("EmittedUserCount"));
            Assert.IsFalse(failure.Properties.ContainsKey("EmittedAgentCount"));
            Assert.IsFalse(failure.Properties.ContainsKey("OrphanCount"));
            Assert.IsFalse(failure.Properties.ContainsKey("ReadDuration"));
            AssertDoesNotContain(
                failure,
                request.Filter,
                literalValue,
                memberObjectId.ToString("D"),
                "private-server",
                "private-credential",
                "CredentialMaterial=");

            var exceptionTelemetry = telemetryChannel.Items
                .OfType<ExceptionTelemetry>()
                .Single();
            Assert.AreSame(originalException, exceptionTelemetry.Exception);
            Assert.AreEqual(
                syncJob.Id.ToString("D"),
                exceptionTelemetry.Properties["JobId"]);
            Assert.AreEqual(
                syncJob.RunId.Value.ToString("D"),
                exceptionTelemetry.Properties["SyncRunId"]);
            Assert.AreEqual(
                syncJob.RunId.Value.ToString("D"),
                exceptionTelemetry.Properties["RunId"]);
            Assert.AreEqual(
                "4",
                exceptionTelemetry.Properties["CurrentPart"]);
            Assert.AreEqual(
                RunTableAdfRunId,
                exceptionTelemetry.Properties["AdfRunId"]);
            Assert.AreEqual(
                "users,agents",
                exceptionTelemetry.Properties["IdentityTypes"]);
            Assert.IsFalse(
                exceptionTelemetry.Properties.ContainsKey("SelectedHumanCount"));
            Assert.IsFalse(
                exceptionTelemetry.Properties.ContainsKey("EmittedUserCount"));
            Assert.IsFalse(
                exceptionTelemetry.Properties.ContainsKey("EmittedAgentCount"));
        }

        private static JsonElement ParseQueryPart(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static void AssertRejected(string queryPartJson)
        {
            var parsed = IdentityTypeSelection.TryParse(
                queryPartJson,
                IdentityTypeSet.UsersOnly,
                out var selection,
                out var error);

            Assert.IsFalse(parsed, queryPartJson);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error), queryPartJson);
            Assert.AreEqual(default, selection, queryPartJson);
        }

        private static SchemaProvider CreateSqlSchemaProvider()
        {
            var provider = new SchemaProvider();
            var schemaPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Schemas",
                "SqlMembershipSchema.json");

            Assert.IsTrue(
                File.Exists(schemaPath),
                $"the published SQL schema must be present at {schemaPath}");
            provider.Schemas.Add(
                Schema.SqlMembershipSchema,
                File.ReadAllText(schemaPath));

            return provider;
        }

        private static async Task<bool> ValidatePartAsync(
            string queryPart,
            SchemaProvider schemaProvider = null)
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance,
                schemaProvider ?? CreateSqlSchemaProvider());

            return await schemaValidatorFunction.ValidateSchemasAsync(
                new SchemaValidatorRequest
                {
                    Query = queryPart,
                    SyncJob = new SyncJob { RunId = Guid.Empty },
                    CurrentPart = 1,
                    TotalParts = 1,
                });
        }

        private static SyncJob CreateSyncJob()
        {
            var jobId = Guid.NewGuid();

            return new SyncJob
            {
                Id = jobId,
                RunId = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Query =
                    "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"users.EmployeeType_Code = 'FTE'\"},\"identityTypes\":[\"users\",\"agents\"]}]",
                Group = new Group
                {
                    SyncJobId = jobId,
                    GroupId = Guid.NewGuid(),
                },
            };
        }

        private static SqlMembershipObtainerService CreateService(
            ISqlMembershipRepository repository,
            IBlobStorageRepository blobStorageRepository,
            ILogger<SqlMembershipObtainerService> logger,
            TelemetryClient telemetryClient = null)
        {
            var dryRunValue = new Mock<IDryRunValue>();
            dryRunValue
                .SetupGet(value => value.DryRunEnabled)
                .Returns(false);

            return new SqlMembershipObtainerService(
                repository,
                blobStorageRepository,
                new Mock<ISyncJobStatusService>().Object,
                new Mock<IDestinationResolver>().Object,
                logger,
                telemetryClient ?? new TelemetryClient(new TelemetryConfiguration()),
                dryRunValue.Object,
                new Mock<IDataFactoryService>().Object);
        }

        private static async Task<GroupMembership> PublishEvaluationAsync(
            SqlMembershipEvaluationResult evaluation,
            SyncJob syncJob,
            Guid groupId,
            bool exclusionary)
        {
            GroupMembership published = null;
            var blobStorageRepository =
                new Mock<IBlobStorageRepository>(MockBehavior.Strict);
            blobStorageRepository
                .Setup(x => x.UploadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>()))
                .Callback<string, string, Dictionary<string, string>>(
                    (_, content, _) =>
                    {
                        published =
                            JsonSerializer.Deserialize<GroupMembership>(content);
                    })
                .Returns(Task.CompletedTask);

            var service = CreateService(
                new Mock<ISqlMembershipRepository>().Object,
                blobStorageRepository.Object,
                NullLogger<SqlMembershipObtainerService>.Instance);

            var response = await service.UploadMembershipEvaluationAsync(
                evaluation,
                syncJob,
                groupId,
                currentPart: 1,
                exclusionary);

            blobStorageRepository.Verify(
                x => x.UploadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>()),
                Times.Once());
            Assert.IsNotNull(published);
            Assert.AreEqual(SyncStatus.InProgress, response.Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.FilePath));

            return published;
        }

        private static void AssertLogValue(
            LogRecord record,
            string propertyName,
            object expected)
        {
            Assert.IsTrue(
                record.Properties.TryGetValue(propertyName, out var actual),
                $"Expected structured log property '{propertyName}'.");
            Assert.AreEqual(
                expected?.ToString(),
                actual?.ToString(),
                propertyName);
        }

        private static void AssertDoesNotContain(
            LogRecord record,
            params string[] prohibitedValues)
        {
            var searchableText = string.Join(
                "|",
                new[] { record.Message }
                    .Concat(record.Properties.Select(
                        property =>
                            $"{property.Key}={property.Value}")));

            foreach (var prohibitedValue in prohibitedValues)
            {
                Assert.IsFalse(
                    searchableText.Contains(
                        prohibitedValue,
                        StringComparison.OrdinalIgnoreCase),
                    $"The log contained prohibited data: {prohibitedValue}");
            }
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<LogRecord> Records { get; } = new List<LogRecord>();

            public IDisposable BeginScope<TState>(TState state) =>
                NoopScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                var properties =
                    state is IEnumerable<KeyValuePair<string, object>> structured
                        ? structured.ToDictionary(
                            property => property.Key,
                            property => property.Value,
                            StringComparer.Ordinal)
                        : new Dictionary<string, object>(
                            StringComparer.Ordinal);

                Records.Add(new LogRecord(
                    logLevel,
                    eventId,
                    formatter(state, exception),
                    exception,
                    properties));
            }
        }

        private sealed class LogRecord
        {
            public LogRecord(
                LogLevel logLevel,
                EventId eventId,
                string message,
                Exception exception,
                IReadOnlyDictionary<string, object> properties)
            {
                LogLevel = logLevel;
                EventId = eventId;
                Message = message;
                Exception = exception;
                Properties = properties;
            }

            public LogLevel LogLevel { get; }

            public EventId EventId { get; }

            public string Message { get; }

            public Exception Exception { get; }

            public IReadOnlyDictionary<string, object> Properties { get; }
        }

        private sealed class RecordingTelemetryChannel : ITelemetryChannel
        {
            public bool? DeveloperMode { get; set; }

            public string EndpointAddress { get; set; }

            public List<ITelemetry> Items { get; } = new List<ITelemetry>();

            public void Send(ITelemetry item)
            {
                Items.Add(item);
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public static NoopScope Instance { get; } = new NoopScope();

            public void Dispose()
            {
            }
        }
    }
}
