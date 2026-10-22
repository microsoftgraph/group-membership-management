// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    [TestClass]
    public sealed class ReaderLoggingTests
    {
        [TestMethod]
        public async Task GetMembersAsync_UncastFullUploadSucceeds_WritesOneDetailedReaderEntry()
        {
            var fixture = CreateFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ReturnsAsync((
                    new List<AzureADUser>
                    {
                        new() { ObjectId = Guid.NewGuid() }
                    },
                    new MemberReadCounts
                    {
                        MemberCount = 2,
                        UserCount = 3,
                        AgentCount = 5,
                        ServicePrincipalCount = 7,
                        DeviceCount = 11,
                        ContactCount = 13,
                        GroupCount = 17,
                        UnknownTypeCount = 19,
                        OtherCount = 23
                    },
                    (string)null));
            var logger = new RecordingLogger<MembersReaderFunction>();
            var function = new MembersReaderFunction(
                logger,
                fixture.Storage,
                fixture.Calculator);

            await function.GetMembersAsync(new MembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                ReadKind = GroupMembershipReadKind.Full
            });

            var entry = logger.Entries.Single(entry =>
                entry.EventId.Id == 150180);
            Assert.AreEqual(
                $"Reader counts for group {fixture.SourceGroupId}: members 2; users 3; agents 5; skipped: service principals 7, devices 11, contacts 13, groups 17, unknown type 19, other 23. Read kind: Full; selection: users,agents (default).",
                entry.Message);
            AssertKeys(
                entry.State,
                "ObjectId",
                "MemberCount",
                "UserCount",
                "AgentCount",
                "ReadKind",
                "IdentityTypes");
            AssertKeys(
                entry.Scopes,
                "Id",
                "RunId",
                "CurrentPart",
                "TotalParts");
        }

        [TestMethod]
        public async Task GetMembersAsync_SelectedFullUploadSucceeds_WritesSelectedAndUnexpectedCounts()
        {
            var fixture = CreateFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ReturnsAsync((
                    new List<AzureADUser>
                    {
                        new() { ObjectId = Guid.NewGuid() }
                    },
                    new MemberReadCounts
                    {
                        MemberCount = 2,
                        UserCount = 3,
                        AgentCount = 5,
                        ServicePrincipalCount = 7,
                        DeviceCount = 11,
                        ContactCount = 13,
                        GroupCount = 17,
                        UnknownTypeCount = 19,
                        OtherCount = 23
                    },
                    (string)null));
            var logger = new RecordingLogger<MembersReaderFunction>();
            var function = new MembersReaderFunction(
                logger,
                fixture.Storage,
                fixture.Calculator);

            await function.GetMembersAsync(new MembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                IdentityTypes = new List<string> { "agents" },
                ReadKind = GroupMembershipReadKind.Full
            });

            var entry = logger.Entries.Single(entry =>
                entry.EventId.Id == 150181);
            Assert.AreEqual(
                $"Reader counts for group {fixture.SourceGroupId}: members 2; selected agents 2; returned users 3; returned agents 5; skipped: service principals 7, devices 11, contacts 13, groups 17, unknown type 19, other 23. Read kind: Full; selection: agents.",
                entry.Message);
            AssertKeys(
                entry.State,
                "ObjectId",
                "MemberCount",
                "SelectedIdentityType",
                "UserCount",
                "AgentCount",
                "ReadKind",
                "IdentityTypes");
            Assert.AreEqual(
                0,
                logger.Entries.Count(entry => entry.EventId.Id == 150180));
        }

        [TestMethod]
        public async Task GetMembersAsync_UsersOnlyWithUnexpectedAgents_LogsEveryReaderBucket()
        {
            var fixture = CreateFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ReturnsAsync((
                    new List<AzureADUser>
                    {
                        new() { ObjectId = Guid.NewGuid() },
                        new() { ObjectId = Guid.NewGuid() }
                    },
                    new MemberReadCounts
                    {
                        MemberCount = 29,
                        UserCount = 31,
                        AgentCount = 37,
                        ServicePrincipalCount = 41,
                        DeviceCount = 43,
                        ContactCount = 47,
                        GroupCount = 53,
                        UnknownTypeCount = 59,
                        OtherCount = 61
                    },
                    (string)null));
            var logger = new RecordingLogger<MembersReaderFunction>();
            var function = new MembersReaderFunction(
                logger,
                fixture.Storage,
                fixture.Calculator);

            await function.GetMembersAsync(new MembersReaderRequest
            {
                SyncJob = fixture.Job,
                GroupId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                IdentityTypes = new List<string> { "users" },
                ReadKind = GroupMembershipReadKind.Full
            });

            var entry = logger.Entries.Single(candidate =>
                candidate.EventId.Id == 150181);
            Assert.AreEqual(
                $"Reader counts for group {fixture.SourceGroupId}: members 29; selected users 29; returned users 31; returned agents 37; skipped: service principals 41, devices 43, contacts 47, groups 53, unknown type 59, other 61. Read kind: Full; selection: users.",
                entry.Message);
            Assert.AreEqual(29, entry.State["MemberCount"]);
            Assert.AreEqual(31, entry.State["UserCount"]);
            Assert.AreEqual(37, entry.State["AgentCount"]);
            Assert.AreEqual(41, entry.State["ServicePrincipalCount"]);
            Assert.AreEqual(43, entry.State["DeviceCount"]);
            Assert.AreEqual(47, entry.State["ContactCount"]);
            Assert.AreEqual(53, entry.State["GroupCount"]);
            Assert.AreEqual(59, entry.State["UnknownTypeCount"]);
            Assert.AreEqual(61, entry.State["OtherCount"]);
            Assert.AreEqual(
                1,
                logger.Entries.Count(candidate =>
                    candidate.EventId.Id == 150181));
        }

        [TestMethod]
        public async Task GetDeltaUsersAsync_UploadSucceeds_WritesOneDeltaReaderEntry()
        {
            var fixture = CreateFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstDeltaUsersPageAsync(
                    fixture.SourceGroupId,
                    5))
                .ReturnsAsync((
                    new List<AzureADUser>
                    {
                        new() { ObjectId = Guid.NewGuid() }
                    },
                    new MemberReadCounts
                    {
                        MemberCount = 29,
                        ServicePrincipalCount = 31,
                        DeviceCount = 37,
                        ContactCount = 41,
                        GroupCount = 43,
                        UnknownTypeCount = 47,
                        OtherCount = 53
                    },
                    (string)null,
                    "delta"));
            var logger = new RecordingLogger<DeltaUserReaderFunction>();
            var function = new DeltaUserReaderFunction(
                logger,
                fixture.Storage,
                fixture.Calculator);

            await function.GetDeltaUsersAsync(new DeltaUserReaderRequest
            {
                SyncJob = fixture.Job,
                ObjectId = fixture.SourceGroupId,
                TargetGroupId = fixture.TargetGroupId,
                CurrentPart = 1,
                TotalParts = 2,
                PageCount = 5,
                IdentityTypes = new List<string> { "users", "agents" },
                ReadKind = GroupMembershipReadKind.DeltaInitial
            });

            var entry = logger.Entries.Single(entry =>
                entry.EventId.Id == 150182);
            Assert.AreEqual(
                $"Reader counts for group {fixture.SourceGroupId}: members 29; skipped: service principals 31, devices 37, contacts 41, groups 43, unknown type 47, other 53. Read kind: DeltaInitial; selection: users,agents.",
                entry.Message);
            AssertKeys(
                entry.State,
                "ObjectId",
                "MemberCount",
                "ServicePrincipalCount",
                "ReadKind",
                "IdentityTypes");
        }

        [TestMethod]
        public async Task ReaderAndOrchestratorLoggingFailures_DoNotChangeReadResults()
        {
            var fixture = CreateFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ReturnsAsync((
                    new List<AzureADUser>
                    {
                        new() { ObjectId = Guid.NewGuid() }
                    },
                    new MemberReadCounts
                    {
                        MemberCount = 1,
                        UserCount = 1
                    },
                    (string)null));
            var logger = new ThrowingLogger<MembersReaderFunction>(
                150180,
                150202,
                150049);
            var function = new MembersReaderFunction(
                logger,
                fixture.Storage,
                fixture.Calculator);

            var nextPageUrl = await function.GetMembersAsync(
                new MembersReaderRequest
                {
                    SyncJob = fixture.Job,
                    GroupId = fixture.SourceGroupId,
                    TargetGroupId = fixture.TargetGroupId,
                    CurrentPart = 1,
                    TotalParts = 2,
                    ReadKind = GroupMembershipReadKind.Full
                });

            Assert.IsNull(nextPageUrl);
            Assert.AreEqual(1, fixture.Storage.Paths.Count);
            GroupReadLogWriter.ReadCompleted(
                logger,
                1,
                fixture.SourceGroupId,
                fixture.TargetGroupId,
                GroupMembershipReadKind.Full,
                null);
            GroupReadLogWriter.Fallback(
                logger,
                fixture.SourceGroupId,
                "identification failed",
                GroupMembershipReadKind.DeltaInitial,
                new[] { "users" });
        }

        [TestMethod]
        public void CompletionAndFallbackEntries_RetainStructuredStateAndScope()
        {
            var logger = new RecordingLogger<ReaderLoggingTests>();
            var sourceGroupId = Guid.NewGuid();
            var targetGroupId = Guid.NewGuid();
            using (logger.BeginScope(new Dictionary<string, object>
            {
                ["RunId"] = Guid.NewGuid()
            }))
            {
                GroupReadLogWriter.ReadCompleted(
                    logger,
                    3,
                    sourceGroupId,
                    targetGroupId,
                    GroupMembershipReadKind.Full,
                    new[] { "agents" });
                GroupReadLogWriter.Fallback(
                    logger,
                    sourceGroupId,
                    "identification failed",
                    GroupMembershipReadKind.DeltaInitial,
                    new[] { "users" });
            }

            var completion = logger.Entries.Single(
                entry => entry.EventId.Id == 150202);
            AssertKeys(
                completion.State,
                "MemberCount",
                "ObjectId",
                "TargetGroupId",
                "ReadKind",
                "IdentityTypes");
            AssertKeys(completion.Scopes, "RunId");

            var fallback = logger.Entries.Single(
                entry => entry.EventId.Id == 150049);
            AssertKeys(
                fallback.State,
                "ObjectId",
                "Reason",
                "ReadKind",
                "IdentityTypes");
            AssertKeys(fallback.Scopes, "RunId");
        }

        [DataTestMethod]
        [DataRow("members", "success", 150180)]
        [DataRow("members", "request-failure", 150180)]
        [DataRow("members", "upload-failure", 150180)]
        [DataRow("subsequent-members", "success", 150181)]
        [DataRow("subsequent-members", "request-failure", 150181)]
        [DataRow("subsequent-members", "upload-failure", 150181)]
        [DataRow("delta", "success", 150182)]
        [DataRow("delta", "request-failure", 150182)]
        [DataRow("delta", "upload-failure", 150182)]
        [DataRow("subsequent-delta", "success", 150182)]
        [DataRow("subsequent-delta", "request-failure", 150182)]
        [DataRow("subsequent-delta", "upload-failure", 150182)]
        [DataRow("delta-link", "success", 150182)]
        [DataRow("delta-link", "request-failure", 150182)]
        [DataRow("delta-link", "upload-failure", 150182)]
        [DataRow("subsequent-delta-link", "success", 150182)]
        [DataRow("subsequent-delta-link", "request-failure", 150182)]
        [DataRow("subsequent-delta-link", "upload-failure", 150182)]
        public async Task ReaderFunctions_LogCountsOnlyAfterRequestAndUploadSucceed(
            string readerName,
            string outcome,
            int expectedEventId)
        {
            var fixture = CreateFixture();
            ConfigureReaderResponse(
                fixture,
                readerName,
                outcome == "request-failure");
            if (outcome == "upload-failure")
            {
                fixture.Storage.BeforeWriteAsync = _ =>
                    Task.FromException(new IOException("upload failed"));
            }

            var result = await ExecuteReaderAsync(fixture, readerName);
            var readerEntries = result.EventIds.Count(eventId =>
                eventId is 150180 or 150181 or 150182);

            if (outcome == "success")
            {
                Assert.IsNull(
                    result.Exception,
                    $"{readerName} unexpectedly failed.");
                Assert.AreEqual(
                    1,
                    readerEntries,
                    $"{readerName} did not log exactly one reader-count event.");
                CollectionAssert.Contains(
                    result.EventIds,
                    expectedEventId);
                return;
            }

            Assert.IsInstanceOfType<IOException>(
                result.Exception,
                $"{readerName} did not propagate the {outcome}.");
            Assert.AreEqual(
                0,
                readerEntries,
                $"{readerName} logged reader counts before the {outcome} completed.");
        }

        [TestMethod]
        public async Task GetMembersAsync_UploadFails_WritesNoReaderEntry()
        {
            var fixture = CreateFixture();
            fixture.GraphRepository
                .Setup(repository => repository.GetFirstTransitiveMembersPageAsync(
                    fixture.SourceGroupId,
                    It.IsAny<IReadOnlyList<string>>()))
                .ReturnsAsync((
                    new List<AzureADUser>
                    {
                        new() { ObjectId = Guid.NewGuid() }
                    },
                    new MemberReadCounts { MemberCount = 1 },
                    (string)null));
            fixture.Storage.BeforeWriteAsync = _ =>
                Task.FromException(new IOException("upload failed"));
            var logger = new RecordingLogger<MembersReaderFunction>();
            var function = new MembersReaderFunction(
                logger,
                fixture.Storage,
                fixture.Calculator);

            await Assert.ThrowsExceptionAsync<IOException>(
                async () => await function.GetMembersAsync(
                    new MembersReaderRequest
                    {
                        SyncJob = fixture.Job,
                        GroupId = fixture.SourceGroupId,
                        TargetGroupId = fixture.TargetGroupId,
                        CurrentPart = 1,
                        TotalParts = 2,
                        ReadKind = GroupMembershipReadKind.Full
                    }));

            Assert.AreEqual(
                0,
                logger.Entries.Count(entry =>
                    entry.EventId.Id is 150180 or 150181));
        }

        private static async Task<ReaderExecution> ExecuteReaderAsync(
            ReaderFixture fixture,
            string readerName)
        {
            switch (readerName)
            {
                case "members":
                    var membersLogger =
                        new RecordingLogger<MembersReaderFunction>();
                    var membersFunction = new MembersReaderFunction(
                        membersLogger,
                        fixture.Storage,
                        fixture.Calculator);
                    return await CaptureExecutionAsync(
                        membersLogger,
                        async () => await membersFunction.GetMembersAsync(
                            new MembersReaderRequest
                            {
                                SyncJob = fixture.Job,
                                GroupId = fixture.SourceGroupId,
                                TargetGroupId = fixture.TargetGroupId,
                                CurrentPart = 1,
                                TotalParts = 2,
                                IdentityTypes = new List<string>
                                {
                                    "users",
                                    "agents"
                                },
                                ReadKind = GroupMembershipReadKind.Full
                            }));

                case "subsequent-members":
                    var subsequentMembersLogger =
                        new RecordingLogger<SubsequentMembersReaderFunction>();
                    var subsequentMembersFunction =
                        new SubsequentMembersReaderFunction(
                            subsequentMembersLogger,
                            fixture.Storage,
                            fixture.Calculator);
                    return await CaptureExecutionAsync(
                        subsequentMembersLogger,
                        async () => await subsequentMembersFunction.GetMembersAsync(
                            new SubsequentMembersReaderRequest
                            {
                                SyncJob = fixture.Job,
                                GroupId = fixture.SourceGroupId,
                                TargetGroupId = fixture.TargetGroupId,
                                CurrentPart = 1,
                                TotalParts = 2,
                                NextPageUrl = "next",
                                IdentityTypes = new List<string> { "agents" },
                                ReadKind = GroupMembershipReadKind.Full
                            }));

                case "delta":
                    var deltaLogger =
                        new RecordingLogger<DeltaUserReaderFunction>();
                    var deltaFunction = new DeltaUserReaderFunction(
                        deltaLogger,
                        fixture.Storage,
                        fixture.Calculator);
                    return await CaptureExecutionAsync(
                        deltaLogger,
                        async () => await deltaFunction.GetDeltaUsersAsync(
                            new DeltaUserReaderRequest
                            {
                                SyncJob = fixture.Job,
                                ObjectId = fixture.SourceGroupId,
                                TargetGroupId = fixture.TargetGroupId,
                                CurrentPart = 1,
                                TotalParts = 2,
                                PageCount = 5,
                                IdentityTypes = new List<string>
                                {
                                    "users",
                                    "agents"
                                },
                                ReadKind = GroupMembershipReadKind.DeltaInitial
                            }));

                case "subsequent-delta":
                    var subsequentDeltaLogger =
                        new RecordingLogger<SubsequentDeltaUserReaderFunction>();
                    var subsequentDeltaFunction =
                        new SubsequentDeltaUserReaderFunction(
                            subsequentDeltaLogger,
                            fixture.Storage,
                            fixture.Calculator);
                    return await CaptureExecutionAsync(
                        subsequentDeltaLogger,
                        async () => await subsequentDeltaFunction
                            .GetSubsequentDeltaUsersAsync(
                                new SubsequentDeltaUserReaderRequest
                                {
                                    SyncJob = fixture.Job,
                                    ObjectId = fixture.SourceGroupId,
                                    TargetGroupId = fixture.TargetGroupId,
                                    CurrentPart = 1,
                                    TotalParts = 2,
                                    NextPageUrl = "next",
                                    PageCount = 5,
                                    IdentityTypes = new List<string>
                                    {
                                        "users",
                                        "agents"
                                    },
                                    ReadKind =
                                        GroupMembershipReadKind.DeltaInitial
                                }));

                case "delta-link":
                    var deltaLinkLogger =
                        new RecordingLogger<DeltaLinkUserReaderFunction>();
                    var deltaLinkFunction = new DeltaLinkUserReaderFunction(
                        deltaLinkLogger,
                        fixture.Calculator,
                        fixture.Storage);
                    return await CaptureExecutionAsync(
                        deltaLinkLogger,
                        async () => await deltaLinkFunction.GetDeltaLinkUsersAsync(
                            new DeltaLinkUserReaderRequest
                            {
                                SyncJob = fixture.Job,
                                GroupId = fixture.SourceGroupId,
                                TargetGroupId = fixture.TargetGroupId,
                                CurrentPart = 1,
                                TotalParts = 2,
                                DeltaLink = "delta",
                                NumberOfPages = 5,
                                IdentityTypes = new List<string>
                                {
                                    "users",
                                    "agents"
                                },
                                ReadKind = GroupMembershipReadKind.DeltaLink
                            }));

                case "subsequent-delta-link":
                    var subsequentDeltaLinkLogger =
                        new RecordingLogger<
                            SubsequentDeltaLinkUserReaderFunction>();
                    var subsequentDeltaLinkFunction =
                        new SubsequentDeltaLinkUserReaderFunction(
                            subsequentDeltaLinkLogger,
                            fixture.Calculator,
                            fixture.Storage);
                    return await CaptureExecutionAsync(
                        subsequentDeltaLinkLogger,
                        async () => await subsequentDeltaLinkFunction
                            .GetSubsequentDeltaLinkUsersAsync(
                                new SubsequentDeltaLinkUserReaderRequest
                                {
                                    SyncJob = fixture.Job,
                                    GroupId = fixture.SourceGroupId,
                                    TargetGroupId = fixture.TargetGroupId,
                                    CurrentPart = 1,
                                    TotalParts = 2,
                                    NextPageUrl = "next",
                                    PageCount = 5,
                                    IdentityTypes = new List<string>
                                    {
                                        "users",
                                        "agents"
                                    },
                                    ReadKind =
                                        GroupMembershipReadKind.DeltaLink
                                }));

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(readerName),
                        readerName,
                        "Unknown reader.");
            }
        }

        private static async Task<ReaderExecution> CaptureExecutionAsync<T>(
            RecordingLogger<T> logger,
            Func<Task> action)
        {
            Exception exception = null;
            try
            {
                await action();
            }
            catch (Exception currentException)
            {
                exception = currentException;
            }

            return new ReaderExecution(
                exception,
                logger.Entries
                    .Select(entry => entry.EventId.Id)
                    .ToList());
        }

        private static void AssertKeys(
            IReadOnlyDictionary<string, object> values,
            params string[] expectedKeys)
        {
            foreach (var expectedKey in expectedKeys)
            {
                Assert.IsTrue(
                    values.ContainsKey(expectedKey),
                    $"Structured logging value '{expectedKey}' was missing.");
            }
        }

        private static void ConfigureReaderResponse(
            ReaderFixture fixture,
            string readerName,
            bool requestFails)
        {
            var failure = new IOException("request failed");
            var counts = new MemberReadCounts
            {
                MemberCount = 11,
                UserCount = 2,
                AgentCount = 3,
                ServicePrincipalCount = 4,
                DeviceCount = 5,
                ContactCount = 6,
                GroupCount = 7,
                UnknownTypeCount = 8,
                OtherCount = 9
            };
            var users = new List<AzureADUser>
            {
                new() { ObjectId = Guid.NewGuid() }
            };
            var removedUsers = new List<AzureADUser>
            {
                new() { ObjectId = Guid.NewGuid() }
            };

            switch (readerName)
            {
                case "members":
                    var membersSetup = fixture.GraphRepository.Setup(
                        repository => repository.GetFirstTransitiveMembersPageAsync(
                            fixture.SourceGroupId,
                            It.IsAny<IReadOnlyList<string>>()));
                    if (requestFails)
                    {
                        membersSetup.ThrowsAsync(failure);
                    }
                    else
                    {
                        membersSetup.ReturnsAsync((users, counts, "next"));
                    }

                    return;

                case "subsequent-members":
                    var subsequentMembersSetup = fixture.GraphRepository.Setup(
                        repository => repository.GetNextTransitiveMembersPageAsync(
                            fixture.SourceGroupId,
                            "next",
                            It.IsAny<IReadOnlyList<string>>()));
                    if (requestFails)
                    {
                        subsequentMembersSetup.ThrowsAsync(failure);
                    }
                    else
                    {
                        subsequentMembersSetup.ReturnsAsync((
                            users,
                            counts,
                            (string)null));
                    }

                    return;

                case "delta":
                    var deltaSetup = fixture.GraphRepository.Setup(
                        repository => repository.GetFirstDeltaUsersPageAsync(
                            fixture.SourceGroupId,
                            5));
                    if (requestFails)
                    {
                        deltaSetup.ThrowsAsync(failure);
                    }
                    else
                    {
                        deltaSetup.ReturnsAsync((
                            users,
                            counts,
                            "next",
                            "delta"));
                    }

                    return;

                case "subsequent-delta":
                    var subsequentDeltaSetup = fixture.GraphRepository.Setup(
                        repository => repository.GetNextDeltaUsersPagesAsync(
                            fixture.SourceGroupId,
                            "next",
                            5));
                    if (requestFails)
                    {
                        subsequentDeltaSetup.ThrowsAsync(failure);
                    }
                    else
                    {
                        subsequentDeltaSetup.ReturnsAsync((
                            users,
                            counts,
                            (string)null,
                            "delta"));
                    }

                    return;

                case "delta-link":
                    var deltaLinkSetup = fixture.GraphRepository.Setup(
                        repository => repository.GetFirstDeltaLinkUsersPageAsync(
                            fixture.SourceGroupId,
                            "delta",
                            5));
                    if (requestFails)
                    {
                        deltaLinkSetup.ThrowsAsync(failure);
                    }
                    else
                    {
                        deltaLinkSetup.ReturnsAsync((
                            users,
                            removedUsers,
                            counts,
                            "next",
                            "delta"));
                    }

                    return;

                case "subsequent-delta-link":
                    var subsequentDeltaLinkSetup = fixture.GraphRepository.Setup(
                        repository => repository.GetNextDeltaLinkUsersPagesAsync(
                            fixture.SourceGroupId,
                            "next",
                            5));
                    if (requestFails)
                    {
                        subsequentDeltaLinkSetup.ThrowsAsync(failure);
                    }
                    else
                    {
                        subsequentDeltaLinkSetup.ReturnsAsync((
                            users,
                            removedUsers,
                            counts,
                            (string)null,
                            "delta"));
                    }

                    return;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(readerName),
                        readerName,
                        "Unknown reader.");
            }
        }

        private static ReaderFixture CreateFixture()
        {
            var targetGroupId = Guid.NewGuid();
            var graphRepository = new Mock<IGraphGroupRepository>();
            var storage = new InMemoryBlobStorageRepository();
            var destinationResolver = new Mock<IDestinationResolver>();
            destinationResolver
                .Setup(resolver => resolver.ResolveAsync(
                    It.IsAny<SyncJob>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedGroupDestination
                {
                    ObjectId = targetGroupId
                });
            var calculator = new SGMembershipCalculator(
                graphRepository.Object,
                storage,
                Mock.Of<IDatabaseSyncJobsRepository>(),
                destinationResolver.Object,
                Mock.Of<IServiceBusQueueRepository>(),
                Mock.Of<IDatabaseDestinationAttributesRepository>(),
                NullLogger<SGMembershipCalculator>.Instance,
                Mock.Of<IDryRunValue>(),
                Mock.Of<ISyncJobStatusService>());
            return new ReaderFixture(
                graphRepository,
                storage,
                calculator,
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    Query = "[{\"type\":\"GroupMembership\"}]"
                },
                Guid.NewGuid(),
                targetGroupId);
        }

        private sealed record ReaderFixture(
            Mock<IGraphGroupRepository> GraphRepository,
            InMemoryBlobStorageRepository Storage,
            SGMembershipCalculator Calculator,
            SyncJob Job,
            Guid SourceGroupId,
            Guid TargetGroupId);

        private sealed record ReaderExecution(
            Exception Exception,
            List<int> EventIds);
    }
}
