// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Groups.Delta;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.GraphGroups;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class GraphGroupRepositoryReadTests
    {
        private const string GraphApiBaseUrl = "https://graph.microsoft.com/v1.0";

        private readonly TelemetryClient _telemetryClient =
            new(new TelemetryConfiguration("instrumentationkey"));

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_UsersOnly_UsesUserCastAdvancedQueryAndUserResponse()
        {
            var userId = Guid.NewGuid();
            RequestInformation capturedRequest = null;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<UserCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<UserCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        capturedRequest = request;
                        SetNativeResponse(request, CreateJsonResponse(
                            $$"""{"value":[{"id":"{{userId}}","accountEnabled":true}]}"""));
                    })
                .ReturnsAsync((UserCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "users" });

            Assert.AreEqual(1, result.users.Count);
            Assert.AreEqual(userId, result.users[0].ObjectId);
            Assert.AreEqual(1, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.UserCount);
            Assert.IsNull(result.counts.AgentCount);
            var requestUrl = Uri.UnescapeDataString(capturedRequest.URI.AbsoluteUri);
            StringAssert.Contains(requestUrl, "/transitiveMembers/microsoft.graph.user");
            StringAssert.Contains(requestUrl, "$filter=not isof('microsoft.graph.agentUser')");
            StringAssert.Contains(requestUrl, "$count=true");
            StringAssert.Contains(requestUrl, "$top=999");
            AssertHeader(capturedRequest, "ConsistencyLevel", "eventual");
            requestAdapter.Verify(
                adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DirectoryObjectCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_AgentsOnly_UsesAgentFilterAndDirectoryObjectResponse()
        {
            var agentId = Guid.NewGuid();
            RequestInformation capturedRequest = null;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DirectoryObjectCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DirectoryObjectCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        capturedRequest = request;
                        SetNativeResponse(request, CreateJsonResponse(
                            $$"""{"value":[{"@odata.type":"#microsoft.graph.agentUser","id":"{{agentId}}"}]}"""));
                    })
                .ReturnsAsync((DirectoryObjectCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "agents" });

            Assert.AreEqual(1, result.users.Count);
            Assert.AreEqual(agentId, result.users[0].ObjectId);
            Assert.AreEqual(1, result.counts.MemberCount);
            Assert.IsNull(result.counts.UserCount);
            Assert.AreEqual(1, result.counts.AgentCount);
            var requestUrl = Uri.UnescapeDataString(capturedRequest.URI.AbsoluteUri);
            StringAssert.Contains(requestUrl, "/transitiveMembers?");
            StringAssert.Contains(requestUrl, "$filter=isof('microsoft.graph.agentUser')");
            StringAssert.Contains(requestUrl, "$count=true");
            StringAssert.Contains(requestUrl, "$top=999");
            AssertHeader(capturedRequest, "ConsistencyLevel", "eventual");
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_OmittedSelection_UsesUncastResponseAndCountsBothTypes()
        {
            var userId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            RequestInformation capturedRequest = null;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DirectoryObjectCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DirectoryObjectCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        capturedRequest = request;
                        SetNativeResponse(request, CreateJsonResponse(
                            $$"""
                            {
                              "value": [
                                {"@odata.type":"#microsoft.graph.user","id":"{{userId}}"},
                                {"@odata.type":"#microsoft.graph.agentUser","id":"{{agentId}}"}
                              ]
                            }
                            """));
                    })
                .ReturnsAsync((DirectoryObjectCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(Guid.NewGuid());

            Assert.AreEqual(2, result.users.Count);
            Assert.AreEqual(2, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.UserCount);
            Assert.AreEqual(1, result.counts.AgentCount);
            Assert.AreEqual(0, result.counts.UnknownTypeCount);
            Assert.IsFalse(capturedRequest.URI.AbsoluteUri.Contains(
                "/microsoft.graph.user",
                StringComparison.Ordinal));
            Assert.IsFalse(capturedRequest.Headers.ContainsKey("ConsistencyLevel"));
        }

        [DataTestMethod]
        [DataRow("users", "agents")]
        [DataRow("agents", "users")]
        public async Task GetFirstTransitiveMembersPageAsync_BothTypes_UsesUncastResponseAndCountsBothTypes(
            string firstIdentityType,
            string secondIdentityType)
        {
            var userId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            RequestInformation capturedRequest = null;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DirectoryObjectCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DirectoryObjectCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        capturedRequest = request;
                        SetNativeResponse(request, CreateJsonResponse(
                            $$"""
                            {
                              "value": [
                                {"@odata.type":"#microsoft.graph.user","id":"{{userId}}"},
                                {"@odata.type":"#microsoft.graph.agentUser","id":"{{agentId}}"}
                              ]
                            }
                            """));
                    })
                .ReturnsAsync((DirectoryObjectCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { firstIdentityType, secondIdentityType });

            CollectionAssert.AreEquivalent(
                new[] { userId, agentId },
                result.users.Select(user => user.ObjectId).ToArray());
            Assert.AreEqual(2, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.UserCount);
            Assert.AreEqual(1, result.counts.AgentCount);
            var requestUrl = Uri.UnescapeDataString(capturedRequest.URI.AbsoluteUri);
            StringAssert.Contains(requestUrl, "/transitiveMembers");
            StringAssert.Contains(requestUrl, "$top=999");
            Assert.IsFalse(requestUrl.Contains(
                "/microsoft.graph.user",
                StringComparison.Ordinal));
            Assert.IsFalse(requestUrl.Contains("$filter=", StringComparison.Ordinal));
            Assert.IsFalse(requestUrl.Contains("$count=true", StringComparison.Ordinal));
            Assert.IsFalse(capturedRequest.Headers.ContainsKey("ConsistencyLevel"));
        }

        [DataTestMethod]
        [DataRow("duplicate")]
        [DataRow("empty")]
        [DataRow("wrong-case")]
        public async Task GetFirstTransitiveMembersPageAsync_InvalidSelection_ThrowsBeforeRequest(
            string selectionCase)
        {
            IReadOnlyList<string> identityTypes = selectionCase switch
            {
                "duplicate" => new[] { "users", "users" },
                "empty" => Array.Empty<string>(),
                "wrong-case" => new[] { "Users" },
                _ => throw new ArgumentOutOfRangeException(nameof(selectionCase)),
            };
            var requestAdapter = CreateRequestAdapter();
            var repository = CreateRepository(requestAdapter.Object);

            var exception = await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => repository.GetFirstTransitiveMembersPageAsync(
                    Guid.NewGuid(),
                    identityTypes));

            Assert.AreEqual("identityTypes", exception.ParamName);
            StringAssert.Contains(
                exception.Message,
                "A group identity selection must contain users, agents, or both exactly once.");
            requestAdapter.Verify(
                adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<UserCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            requestAdapter.Verify(
                adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DirectoryObjectCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_OmittedSelectionMissingType_SkipsAndCountsUnknownType()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupDirectoryResponse<DirectoryObjectCollectionResponse>(
                requestAdapter,
                $$"""{"value":[{"id":"{{Guid.NewGuid()}}"}]}""");
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(Guid.NewGuid());

            Assert.AreEqual(0, result.users.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.UnknownTypeCount);
        }

        [DataTestMethod]
        [DataRow("omitted")]
        [DataRow("users")]
        [DataRow("agents")]
        public async Task GetFirstTransitiveMembersPageAsync_SuccessWithoutContentType_Throws(
            string selectionName)
        {
            var requestAdapter = CreateRequestAdapter();
            var response = CreateJsonResponse("""{"value":[]}""");
            response.Content.Headers.ContentType = null;
            if (selectionName == "users")
            {
                SetupDirectoryResponse<UserCollectionResponse>(requestAdapter, response);
            }
            else
            {
                SetupDirectoryResponse<DirectoryObjectCollectionResponse>(requestAdapter, response);
            }

            var repository = CreateRepository(requestAdapter.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () =>
                {
                    if (selectionName == "omitted")
                    {
                        await repository.GetFirstTransitiveMembersPageAsync(
                            Guid.NewGuid());
                    }
                    else
                    {
                        await repository.GetFirstTransitiveMembersPageAsync(
                            Guid.NewGuid(),
                            new[] { selectionName });
                    }
                });

            Assert.AreEqual(
                "Graph returned an unreadable transitive-members page.",
                exception.Message);
        }

        [DataTestMethod]
        [DataRow("omitted")]
        [DataRow("users")]
        [DataRow("agents")]
        public async Task GetFirstTransitiveMembersPageAsync_EmptyValueArray_ReturnsEmptyPage(
            string selectionName)
        {
            var requestAdapter = CreateRequestAdapter();
            if (selectionName == "users")
            {
                SetupDirectoryResponse<UserCollectionResponse>(
                    requestAdapter,
                    """{"value":[]}""");
            }
            else
            {
                SetupDirectoryResponse<DirectoryObjectCollectionResponse>(
                    requestAdapter,
                    """{"value":[]}""");
            }

            var repository = CreateRepository(requestAdapter.Object);
            var result = selectionName == "omitted"
                ? await repository.GetFirstTransitiveMembersPageAsync(
                    Guid.NewGuid())
                : await repository.GetFirstTransitiveMembersPageAsync(
                    Guid.NewGuid(),
                    new[] { selectionName });

            Assert.AreEqual(0, result.users.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.IsNull(result.nextPageUrl);
        }

        [TestMethod]
        public async Task GetNextTransitiveMembersPageAsync_UsersOnly_FollowsNextLinkAndRepeatsHeader()
        {
            var nextLink = $"{GraphApiBaseUrl}/groups/{Guid.NewGuid()}/transitiveMembers/microsoft.graph.user?$skiptoken=next";
            RequestInformation capturedRequest = null;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<UserCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<UserCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        capturedRequest = request;
                        SetNativeResponse(request, CreateJsonResponse("""{"value":[]}"""));
                    })
                .ReturnsAsync((UserCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetNextTransitiveMembersPageAsync(
                Guid.NewGuid(),
                nextLink,
                new[] { "users" });

            Assert.AreEqual(nextLink, capturedRequest.URI.AbsoluteUri);
            AssertHeader(capturedRequest, "ConsistencyLevel", "eventual");
            Assert.IsNull(result.nextPageUrl);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_UsersOnlyAgentResponse_SkipsAndCountsAgent()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupDirectoryResponse<UserCollectionResponse>(
                requestAdapter,
                $$"""{"value":[{"@odata.type":"#microsoft.graph.agentUser","id":"{{Guid.NewGuid()}}"}]}""");
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "users" });

            Assert.AreEqual(0, result.users.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.AreEqual(0, result.counts.UserCount);
            Assert.AreEqual(1, result.counts.AgentCount);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_UsersOnlyDeviceDiscriminator_SkipsAndCountsDevice()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupDirectoryResponse<UserCollectionResponse>(
                requestAdapter,
                $$"""{"value":[{"@odata.type":"#microsoft.graph.device","id":"{{Guid.NewGuid()}}"}]}""");
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "users" });

            Assert.AreEqual(0, result.users.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.DeviceCount);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_UsersOnlyUnknownDiscriminator_SkipsAndCountsOther()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupDirectoryResponse<UserCollectionResponse>(
                requestAdapter,
                $$"""{"value":[{"@odata.type":"#microsoft.graph.futureType","id":"{{Guid.NewGuid()}}"}]}""");
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "users" });

            Assert.AreEqual(0, result.users.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.OtherCount);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_AgentsOnlyUserResponse_SkipsAndCountsUser()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupDirectoryResponse<DirectoryObjectCollectionResponse>(
                requestAdapter,
                $$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{Guid.NewGuid()}}"}]}""");
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "agents" });

            Assert.AreEqual(0, result.users.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.UserCount);
            Assert.AreEqual(0, result.counts.AgentCount);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_AgentsOnlyUnexpectedCategories_AreCounted()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupDirectoryResponse<DirectoryObjectCollectionResponse>(
                requestAdapter,
                $$"""
                {
                  "value": [
                    {"@odata.type":"#microsoft.graph.device","id":"{{Guid.NewGuid()}}"},
                    {"@odata.type":"#microsoft.graph.futureType","id":"{{Guid.NewGuid()}}"},
                    {"id":"{{Guid.NewGuid()}}"}
                  ]
                }
                """);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "agents" });

            Assert.AreEqual(0, result.users.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.DeviceCount);
            Assert.AreEqual(1, result.counts.OtherCount);
            Assert.AreEqual(1, result.counts.UnknownTypeCount);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_OmittedSelectionMixedPage_ReportsEveryBucket()
        {
            var userId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var requestAdapter = CreateRequestAdapter();
            SetupDirectoryResponse<DirectoryObjectCollectionResponse>(
                requestAdapter,
                $$"""
                {
                  "value": [
                    {"@odata.type":"#microsoft.graph.user","id":"{{userId}}"},
                    {"@odata.type":"#microsoft.graph.agentUser","id":"{{agentId}}"},
                    {"@odata.type":"#microsoft.graph.servicePrincipal","id":"{{Guid.NewGuid()}}"},
                    {"@odata.type":"#microsoft.graph.device","id":"{{Guid.NewGuid()}}"},
                    {"@odata.type":"#microsoft.graph.orgContact","id":"{{Guid.NewGuid()}}"},
                    {"@odata.type":"#microsoft.graph.group","id":"{{Guid.NewGuid()}}"},
                    {"@odata.type":"#microsoft.graph.futureType","id":"{{Guid.NewGuid()}}"},
                    {"id":"{{Guid.NewGuid()}}"}
                  ]
                }
                """);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid());

            CollectionAssert.AreEquivalent(
                new[] { userId, agentId },
                result.users.Select(user => user.ObjectId).ToArray());
            Assert.AreEqual(2, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.UserCount);
            Assert.AreEqual(1, result.counts.AgentCount);
            Assert.AreEqual(1, result.counts.ServicePrincipalCount);
            Assert.AreEqual(1, result.counts.DeviceCount);
            Assert.AreEqual(1, result.counts.ContactCount);
            Assert.AreEqual(1, result.counts.GroupCount);
            Assert.AreEqual(1, result.counts.OtherCount);
            Assert.AreEqual(1, result.counts.UnknownTypeCount);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_UsersOnlyCrossesMixedPagesAndCountsEveryBucket()
        {
            var firstUserId = Guid.NewGuid();
            var secondUserId = Guid.NewGuid();
            var nextLink =
                $"{GraphApiBaseUrl}/groups/{Guid.NewGuid()}/transitiveMembers/microsoft.graph.user?$filter=not isof('microsoft.graph.agentUser')&$count=true&$top=999&$skiptoken=page2";
            var firstPageEntries = new List<Dictionary<string, string>>
            {
                CreateDirectoryEntry(firstUserId),
            };
            firstPageEntries.AddRange(CreateDirectoryEntries(1, "microsoft.graph.agentUser"));
            firstPageEntries.AddRange(CreateDirectoryEntries(1, "microsoft.graph.servicePrincipal"));
            firstPageEntries.AddRange(CreateDirectoryEntries(2, "microsoft.graph.device"));
            firstPageEntries.AddRange(CreateDirectoryEntries(2, "microsoft.graph.orgContact"));
            firstPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.group"));
            firstPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.futureType"));
            var secondPageEntries = new List<Dictionary<string, string>>
            {
                CreateDirectoryEntry(secondUserId),
            };
            secondPageEntries.AddRange(CreateDirectoryEntries(2, "microsoft.graph.agentUser"));
            secondPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.servicePrincipal"));
            secondPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.device"));
            secondPageEntries.AddRange(CreateDirectoryEntries(4, "microsoft.graph.orgContact"));
            secondPageEntries.AddRange(CreateDirectoryEntries(4, "microsoft.graph.group"));
            secondPageEntries.AddRange(CreateDirectoryEntries(5, "microsoft.graph.futureType"));
            var requests = new List<RequestInformation>();
            var requestCount = 0;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<UserCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<UserCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        requests.Add(request);
                        var response = requestCount++ == 0
                            ? CreateJsonResponse(CreateMembersPage(firstPageEntries, nextLink))
                            : CreateJsonResponse(CreateMembersPage(secondPageEntries));
                        SetNativeResponse(request, response);
                    })
                .ReturnsAsync((UserCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "users" });

            CollectionAssert.AreEqual(
                new[] { firstUserId, secondUserId },
                result.users.Select(user => user.ObjectId).ToArray());
            Assert.AreEqual(2, result.counts.MemberCount);
            Assert.AreEqual(2, result.counts.UserCount);
            Assert.AreEqual(3, result.counts.AgentCount);
            Assert.AreEqual(4, result.counts.ServicePrincipalCount);
            Assert.AreEqual(5, result.counts.DeviceCount);
            Assert.AreEqual(6, result.counts.ContactCount);
            Assert.AreEqual(7, result.counts.GroupCount);
            Assert.AreEqual(8, result.counts.OtherCount);
            Assert.AreEqual(0, result.counts.UnknownTypeCount);
            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual(
                nextLink,
                Uri.UnescapeDataString(requests[1].URI.AbsoluteUri));
            foreach (var request in requests)
            {
                var requestUrl = Uri.UnescapeDataString(request.URI.AbsoluteUri);
                StringAssert.Contains(requestUrl, "$filter=not isof('microsoft.graph.agentUser')");
                StringAssert.Contains(requestUrl, "$count=true");
                StringAssert.Contains(requestUrl, "$top=999");
                AssertHeader(request, "ConsistencyLevel", "eventual");
            }

            Assert.IsNull(result.nextPageUrl);
        }

        [TestMethod]
        public async Task GetFirstTransitiveMembersPageAsync_AgentsOnlyCrossesMixedPagesAndCountsEveryBucket()
        {
            var firstAgentId = Guid.NewGuid();
            var secondAgentId = Guid.NewGuid();
            var nextLink =
                $"{GraphApiBaseUrl}/groups/{Guid.NewGuid()}/transitiveMembers?$filter=isof('microsoft.graph.agentUser')&$count=true&$top=999&$skiptoken=page2";
            var firstPageEntries = new List<Dictionary<string, string>>
            {
                CreateDirectoryEntry(firstAgentId, "microsoft.graph.agentUser"),
            };
            firstPageEntries.AddRange(CreateDirectoryEntries(1, "microsoft.graph.user"));
            firstPageEntries.AddRange(CreateDirectoryEntries(1, "microsoft.graph.servicePrincipal"));
            firstPageEntries.AddRange(CreateDirectoryEntries(2, "microsoft.graph.device"));
            firstPageEntries.AddRange(CreateDirectoryEntries(2, "microsoft.graph.orgContact"));
            firstPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.group"));
            firstPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.futureType"));
            firstPageEntries.AddRange(CreateDirectoryEntries(4));
            var secondPageEntries = new List<Dictionary<string, string>>
            {
                CreateDirectoryEntry(secondAgentId, "microsoft.graph.agentUser"),
            };
            secondPageEntries.AddRange(CreateDirectoryEntries(2, "microsoft.graph.user"));
            secondPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.servicePrincipal"));
            secondPageEntries.AddRange(CreateDirectoryEntries(3, "microsoft.graph.device"));
            secondPageEntries.AddRange(CreateDirectoryEntries(4, "microsoft.graph.orgContact"));
            secondPageEntries.AddRange(CreateDirectoryEntries(4, "microsoft.graph.group"));
            secondPageEntries.AddRange(CreateDirectoryEntries(5, "microsoft.graph.futureType"));
            secondPageEntries.AddRange(CreateDirectoryEntries(5));
            var requests = new List<RequestInformation>();
            var requestCount = 0;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DirectoryObjectCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DirectoryObjectCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        requests.Add(request);
                        var response = requestCount++ == 0
                            ? CreateJsonResponse(CreateMembersPage(firstPageEntries, nextLink))
                            : CreateJsonResponse(CreateMembersPage(secondPageEntries));
                        SetNativeResponse(request, response);
                    })
                .ReturnsAsync((DirectoryObjectCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstTransitiveMembersPageAsync(
                Guid.NewGuid(),
                new[] { "agents" });

            CollectionAssert.AreEqual(
                new[] { firstAgentId, secondAgentId },
                result.users.Select(user => user.ObjectId).ToArray());
            Assert.AreEqual(2, result.counts.MemberCount);
            Assert.AreEqual(3, result.counts.UserCount);
            Assert.AreEqual(2, result.counts.AgentCount);
            Assert.AreEqual(4, result.counts.ServicePrincipalCount);
            Assert.AreEqual(5, result.counts.DeviceCount);
            Assert.AreEqual(6, result.counts.ContactCount);
            Assert.AreEqual(7, result.counts.GroupCount);
            Assert.AreEqual(8, result.counts.OtherCount);
            Assert.AreEqual(9, result.counts.UnknownTypeCount);
            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual(
                nextLink,
                Uri.UnescapeDataString(requests[1].URI.AbsoluteUri));
            foreach (var request in requests)
            {
                var requestUrl = Uri.UnescapeDataString(request.URI.AbsoluteUri);
                StringAssert.Contains(requestUrl, "$filter=isof('microsoft.graph.agentUser')");
                StringAssert.Contains(requestUrl, "$count=true");
                StringAssert.Contains(requestUrl, "$top=999");
                AssertHeader(request, "ConsistencyLevel", "eventual");
            }

            Assert.IsNull(result.nextPageUrl);
        }

        [TestMethod]
        public async Task GetNextTransitiveMembersPageAsync_AgentsOnly_FollowsNextLinkAndRepeatsHeader()
        {
            var nextLink =
                $"{GraphApiBaseUrl}/groups/{Guid.NewGuid()}/transitiveMembers?$filter=isof('microsoft.graph.agentUser')&$count=true&$top=999&$skiptoken=agent-next";
            RequestInformation capturedRequest = null;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DirectoryObjectCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DirectoryObjectCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        capturedRequest = request;
                        SetNativeResponse(
                            request,
                            CreateJsonResponse("""{"value":[]}"""));
                    })
                .ReturnsAsync((DirectoryObjectCollectionResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetNextTransitiveMembersPageAsync(
                Guid.NewGuid(),
                nextLink,
                new[] { "agents" });

            Assert.AreEqual(nextLink, capturedRequest.URI.AbsoluteUri);
            var requestUrl = Uri.UnescapeDataString(capturedRequest.URI.AbsoluteUri);
            StringAssert.Contains(requestUrl, "$filter=isof('microsoft.graph.agentUser')");
            StringAssert.Contains(requestUrl, "$count=true");
            StringAssert.Contains(requestUrl, "$top=999");
            AssertHeader(capturedRequest, "ConsistencyLevel", "eventual");
            Assert.IsNull(result.nextPageUrl);
        }

        [TestMethod]
        public async Task GetFirstDeltaUsersPageAsync_MixedInitialDelta_ReportsMembersAndSkippedBuckets()
        {
            var groupId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DeltaResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DeltaResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        SetNativeResponse(
                            request,
                            CreateJsonResponse(
                                $$$$"""
                                {
                                  "value": [{
                                    "id": "{{{{groupId}}}}",
                                    "members@delta": [
                                      {"@odata.type":"#microsoft.graph.user","id":"{{{{userId}}}}"},
                                      {"@odata.type":"#microsoft.graph.agentUser","id":"{{{{agentId}}}}"},
                                      {"@odata.type":"#microsoft.graph.device","id":"{{{{Guid.NewGuid()}}}}"},
                                      {"id":"{{{{Guid.NewGuid()}}}}"}
                                    ]
                                  }],
                                  "@odata.deltaLink": "{{{{GraphApiBaseUrl}}}}/groups/delta?$deltatoken=done"
                                }
                                """));
                    })
                .ReturnsAsync((DeltaResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstDeltaUsersPageAsync(
                groupId,
                1);

            CollectionAssert.AreEquivalent(
                new[] { userId, agentId },
                result.users.Select(user => user.ObjectId).ToArray());
            Assert.AreEqual(2, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.DeviceCount);
            Assert.AreEqual(1, result.counts.UnknownTypeCount);
            Assert.IsNull(result.counts.UserCount);
            Assert.IsNull(result.counts.AgentCount);
        }

        [TestMethod]
        public void ToUsers_TypelessDirectoryObject_IsCountedAsUnknown()
        {
            var requestAdapter = CreateRequestAdapter();
            var repository = CreateRepository(requestAdapter.Object);
            var graphUserReader = typeof(GraphGroupRepository)
                .GetField(
                    "_graphUserReader",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(repository);
            var toUsers = graphUserReader
                .GetType()
                .BaseType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method =>
                {
                    if (method.Name != "ToUsers")
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 2 &&
                        parameters[1].ParameterType ==
                            typeof(Dictionary<string, int>);
                });
            var nonUserGraphObjects = new Dictionary<string, int>();
            var users = ((IEnumerable<AzureADUser>)toUsers.Invoke(
                graphUserReader,
                new object[]
                {
                    new[]
                    {
                        new DirectoryObject
                        {
                            Id = Guid.NewGuid().ToString(),
                            OdataType = null,
                        },
                    },
                    nonUserGraphObjects,
                })).ToList();

            Assert.AreEqual(0, users.Count);
            Assert.AreEqual(1, nonUserGraphObjects["Unknown"]);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_Success_NormalizesTypesAndLeavesOmissionsAbsent()
        {
            var userId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var omittedId = Guid.NewGuid();
            RequestInformation capturedRequest = null;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        capturedRequest = request;
                        SetNativeResponse(request, CreateJsonResponse(
                            $$"""
                            {
                              "value": [
                                {"@odata.type":"#microsoft.graph.user","id":"{{userId}}"},
                                {"@odata.type":"#microsoft.graph.agentUser","id":"{{agentId}}"}
                              ]
                            }
                            """));
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetDirectoryObjectTypesAsync(
                new[] { userId, agentId, omittedId },
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("user", result[userId]);
            Assert.AreEqual("agentUser", result[agentId]);
            Assert.IsFalse(result.ContainsKey(omittedId));
            Assert.AreEqual(Method.POST, capturedRequest.HttpMethod);
            var requestUrl = Uri.UnescapeDataString(capturedRequest.URI.AbsoluteUri);
            StringAssert.Contains(requestUrl, "/directoryObjects/getByIds");
            StringAssert.Contains(requestUrl, "$select=id");
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_Success_TracksGraphResourceUnits()
        {
            var id = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var channel = new CapturingChannel();
            using var configuration = new TelemetryConfiguration
            {
                TelemetryChannel = channel
            };
            var telemetryClient = new TelemetryClient(configuration);
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        var response = CreateJsonResponse(
                            $$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{id}}"}]}""");
                        response.Headers.Add("x-ms-resource-unit", "3");
                        SetNativeResponse(request, response);
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(
                requestAdapter.Object,
                telemetryClient: telemetryClient);

            await repository.GetDirectoryObjectTypesAsync(
                new[] { id },
                runId,
                CancellationToken.None);

            var resourceUnitEvent = channel.Items
                .OfType<EventTelemetry>()
                .Single(item => item.Name == TelemetryConstants.ResourceUnitsEventName);
            Assert.AreEqual("3", resourceUnitEvent.Properties["ResourceUnitsUsed"]);
            Assert.AreEqual(runId.ToString(), resourceUnitEvent.Properties["RunId"]);
            Assert.AreEqual(
                QueryType.Other.ToString(),
                resourceUnitEvent.Properties[TelemetryConstants.QueryTypeDimensionName]);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_UnsupportedType_Throws()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupGetByIdsResponse(
                requestAdapter,
                $$"""{"value":[{"@odata.type":"#microsoft.graph.servicePrincipal","id":"{{Guid.NewGuid()}}"}]}""");
            var repository = CreateRepository(requestAdapter.Object);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    CancellationToken.None));
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_MissingType_Throws()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupGetByIdsResponse(
                requestAdapter,
                $$"""{"value":[{"id":"{{Guid.NewGuid()}}"}]}""");
            var repository = CreateRepository(requestAdapter.Object);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    CancellationToken.None));
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_MalformedResponse_Throws()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupGetByIdsResponse(requestAdapter, """{"value": [""");
            var repository = CreateRepository(requestAdapter.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    CancellationToken.None));

            StringAssert.Contains(exception.Message, "invalid JSON");
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_MissingValueArray_Throws()
        {
            var requestAdapter = CreateRequestAdapter();
            SetupGetByIdsResponse(requestAdapter, "{}");
            var repository = CreateRepository(requestAdapter.Object);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    CancellationToken.None));

            StringAssert.Contains(exception.Message, "no value array");
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_Forbidden_ThrowsWithoutRetry()
        {
            var requestCount = 0;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        requestCount++;
                        SetNativeResponse(request, new HttpResponseMessage(HttpStatusCode.Forbidden));
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(requestAdapter.Object);

            var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(1, requestCount);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_TooManyRequests_UsesInjectedRetryAfterPolicy()
        {
            var id = Guid.NewGuid();
            var requestCount = 0;
            TimeSpan? observedRetryAfter = null;
            var retryAfterPolicy = Policy
                .HandleResult<HttpResponseMessage>(response => response.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    1,
                    _ => TimeSpan.Zero,
                    (response, _, _) =>
                    {
                        observedRetryAfter = response.Result.Headers.RetryAfter?.Delta;
                    });
            var retryPolicyProvider = new TestRetryPolicyProvider(
                retryAfterPolicy,
                Policy.NoOpAsync<HttpResponseMessage>());
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        requestCount++;
                        var response = requestCount == 1
                            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                            : CreateJsonResponse($$"""{"value":[{"@odata.type":"#microsoft.graph.user","id":"{{id}}"}]}""");
                        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                        SetNativeResponse(request, response);
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(requestAdapter.Object, retryPolicyProvider);

            var result = await repository.GetDirectoryObjectTypesAsync(
                new[] { id },
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.AreEqual(2, requestCount);
            Assert.AreEqual(TimeSpan.FromSeconds(7), observedRetryAfter);
            Assert.AreEqual("user", result[id]);
            Assert.AreEqual(2, retryPolicyProvider.PolicyRequests, "getByIds composes the shared 429 and transient policies as its single retry owner.");
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_TooManyRequestsAfterRetries_Throws()
        {
            var requestCount = 0;
            var retryAfterPolicy = Policy
                .HandleResult<HttpResponseMessage>(
                    response => response.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(2, _ => TimeSpan.Zero);
            var retryPolicyProvider = new TestRetryPolicyProvider(
                retryAfterPolicy,
                Policy.NoOpAsync<HttpResponseMessage>());
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        requestCount++;
                        SetNativeResponse(request, new HttpResponseMessage(HttpStatusCode.TooManyRequests));
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(requestAdapter.Object, retryPolicyProvider);

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
            Assert.AreEqual(3, requestCount);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_InternalServerErrorAfterRetries_Throws()
        {
            var requestCount = 0;
            var exceptionHandlingPolicy = Policy
                .HandleResult<HttpResponseMessage>(
                    response => response.StatusCode == HttpStatusCode.InternalServerError)
                .WaitAndRetryAsync(2, _ => TimeSpan.Zero);
            var retryPolicyProvider = new TestRetryPolicyProvider(
                Policy.NoOpAsync<HttpResponseMessage>(),
                exceptionHandlingPolicy);
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        requestCount++;
                        SetNativeResponse(request, new HttpResponseMessage(HttpStatusCode.InternalServerError));
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(requestAdapter.Object, retryPolicyProvider);

            var exception = await Assert.ThrowsExceptionAsync<GraphRetriesExhaustedException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(3, requestCount);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_MaximumIdentifierCount_IsAccepted()
        {
            var ids = Enumerable.Range(0, 1000)
                .Select(_ => Guid.NewGuid())
                .ToArray();
            var requestAdapter = CreateRequestAdapter();
            SetupGetByIdsResponse(requestAdapter, """{"value":[]}""");
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetDirectoryObjectTypesAsync(
                ids,
                null,
                CancellationToken.None);

            Assert.AreEqual(0, result.Count);
            requestAdapter.Verify(
                adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_MoreThanMaximumIdentifiers_ThrowsBeforeRequest()
        {
            var ids = Enumerable.Range(0, 1001)
                .Select(_ => Guid.NewGuid())
                .ToArray();
            var requestAdapter = CreateRequestAdapter();
            var repository = CreateRepository(requestAdapter.Object);

            await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    ids,
                    null,
                    CancellationToken.None));

            requestAdapter.Verify(
                adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_CancellationWhileWaitingToRetry_StopsRequest()
        {
            using var cancellationSource = new CancellationTokenSource();
            var retryStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var retryAfterPolicy = Policy
                .HandleResult<HttpResponseMessage>(
                    response => response.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    1,
                    _ => TimeSpan.FromMinutes(5),
                    (_, _, _, _) =>
                    {
                        retryStarted.TrySetResult(true);
                        return Task.CompletedTask;
                    });
            var retryPolicyProvider = new TestRetryPolicyProvider(
                retryAfterPolicy,
                Policy.NoOpAsync<HttpResponseMessage>());
            var requestCount = 0;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) =>
                    {
                        requestCount++;
                        SetNativeResponse(request, new HttpResponseMessage(HttpStatusCode.TooManyRequests));
                    })
                .Returns(Task.CompletedTask);
            var repository = CreateRepository(requestAdapter.Object, retryPolicyProvider);

            var lookupTask = repository.GetDirectoryObjectTypesAsync(
                new[] { Guid.NewGuid() },
                null,
                cancellationSource.Token);
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellationSource.Cancel();

            OperationCanceledException observedException = null;
            try
            {
                await lookupTask;
            }
            catch (OperationCanceledException exception)
            {
                observedException = exception;
            }

            Assert.IsNotNull(observedException);
            Assert.AreEqual(1, requestCount);
        }

        [TestMethod]
        public async Task GetDirectoryObjectTypesAsync_Cancellation_PropagatesToRequest()
        {
            using var cancellationSource = new CancellationTokenSource();
            CancellationToken observedToken = default;
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (_, _, token) =>
                    {
                        observedToken = token;
                        cancellationSource.Cancel();
                        return Task.FromCanceled(token);
                    });
            var repository = CreateRepository(requestAdapter.Object);

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => repository.GetDirectoryObjectTypesAsync(
                    new[] { Guid.NewGuid() },
                    null,
                    cancellationSource.Token));

            Assert.AreEqual(cancellationSource.Token, observedToken);
            Assert.IsTrue(observedToken.IsCancellationRequested);
        }

        [TestMethod]
        public async Task GetFirstDeltaLinkUsersPageAsync_UserAndAgentChanges_AcceptsBothLabelsAndDoesNotCountRemoval()
        {
            var groupId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var addedAgentId = Guid.NewGuid();
            var removedAgentId = Guid.NewGuid();
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DeltaResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DeltaResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        SetNativeResponse(request, CreateJsonResponse(
                            $$$$"""
                            {
                              "value": [{
                                "id": "{{{{groupId}}}}",
                                "members@delta": [
                                  {"@odata.type":"#microsoft.graph.user","id":"{{{{userId}}}}"},
                                  {"@odata.type":"#microsoft.graph.agentUser","id":"{{{{addedAgentId}}}}"},
                                  {"@odata.type":"#microsoft.graph.agentUser","id":"{{{{removedAgentId}}}}","@removed":{"reason":"changed"}},
                                  {"@odata.type":"#microsoft.graph.servicePrincipal","id":"{{{{Guid.NewGuid()}}}}"},
                                  {"id":"{{{{Guid.NewGuid()}}}}"}
                                ]
                              }],
                              "@odata.deltaLink": "{{{{GraphApiBaseUrl}}}}/groups/delta?$deltatoken=done"
                            }
                            """));
                    })
                .ReturnsAsync((DeltaResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstDeltaLinkUsersPageAsync(
                groupId,
                $"{GraphApiBaseUrl}/groups/delta?$deltatoken=start",
                1);

            Assert.AreEqual(2, result.usersToAdd.Count);
            Assert.AreEqual(1, result.usersToRemove.Count);
            Assert.AreEqual(removedAgentId, result.usersToRemove[0].ObjectId);
            Assert.AreEqual(2, result.counts.MemberCount);
            Assert.AreEqual(1, result.counts.ServicePrincipalCount);
            Assert.AreEqual(1, result.counts.UnknownTypeCount);
            Assert.IsNull(result.counts.UserCount);
            Assert.IsNull(result.counts.AgentCount);
        }

        [TestMethod]
        public async Task GetFirstDeltaLinkUsersPageAsync_RemovalWithAnyOrNoType_IsReturnedAndNotCounted()
        {
            var groupId = Guid.NewGuid();
            var typelessRemovalId = Guid.NewGuid();
            var directoryObjectRemovalId = Guid.NewGuid();
            var groupTypedRemovalId = Guid.NewGuid();
            var userRemovalId = Guid.NewGuid();
            var requestAdapter = CreateRequestAdapter();
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<DeltaResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<DeltaResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) =>
                    {
                        SetNativeResponse(request, CreateJsonResponse(
                            $$$$"""
                            {
                              "value": [{
                                "id": "{{{{groupId}}}}",
                                "members@delta": [
                                  {"id":"{{{{typelessRemovalId}}}}","@removed":{"reason":"changed"}},
                                  {"@odata.type":"#microsoft.graph.directoryObject","id":"{{{{directoryObjectRemovalId}}}}","@removed":{"reason":"deleted"}},
                                  {"@odata.type":"#microsoft.graph.group","id":"{{{{groupTypedRemovalId}}}}","@removed":{"reason":"changed"}},
                                  {"@odata.type":"#microsoft.graph.user","id":"{{{{userRemovalId}}}}","@removed":{"reason":"changed"}},
                                  {"id":"not-a-guid","@removed":{"reason":"changed"}}
                                ]
                              }],
                              "@odata.deltaLink": "{{{{GraphApiBaseUrl}}}}/groups/delta?$deltatoken=done"
                            }
                            """));
                    })
                .ReturnsAsync((DeltaResponse)null);
            var repository = CreateRepository(requestAdapter.Object);

            var result = await repository.GetFirstDeltaLinkUsersPageAsync(
                groupId,
                $"{GraphApiBaseUrl}/groups/delta?$deltatoken=start",
                1);

            CollectionAssert.AreEqual(
                new[] { typelessRemovalId, directoryObjectRemovalId, groupTypedRemovalId, userRemovalId },
                result.usersToRemove.Select(user => user.ObjectId).ToArray());
            Assert.IsTrue(result.usersToRemove.All(user => user.MembershipAction == MembershipAction.Remove));
            Assert.AreEqual(0, result.usersToAdd.Count);
            Assert.AreEqual(0, result.counts.MemberCount);
            Assert.AreEqual(0, result.counts.UnknownTypeCount);
            Assert.AreEqual(0, result.counts.GroupCount);
        }

        private GraphGroupRepository CreateRepository(
            IRequestAdapter requestAdapter,
            IRetryPolicyProvider retryPolicyProvider = null,
            TelemetryClient telemetryClient = null)
        {
            return new GraphGroupRepository(
                new GraphServiceClient(requestAdapter, GraphApiBaseUrl),
                telemetryClient ?? _telemetryClient,
                retryPolicyProvider ?? TestRetryPolicyProvider.NoOp,
                NullLoggerFactory.Instance);
        }

        private static Mock<IRequestAdapter> CreateRequestAdapter()
        {
            var requestAdapter = new Mock<IRequestAdapter>();
            requestAdapter.SetupProperty(adapter => adapter.BaseUrl, GraphApiBaseUrl);
            requestAdapter
                .SetupGet(adapter => adapter.SerializationWriterFactory)
                .Returns(new JsonSerializationWriterFactory());
            return requestAdapter;
        }

        private static void SetupGetByIdsResponse(Mock<IRequestAdapter> requestAdapter, string responseContent)
        {
            requestAdapter
                .Setup(adapter => adapter.SendNoContentAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _) => SetNativeResponse(request, CreateJsonResponse(responseContent)))
                .Returns(Task.CompletedTask);
        }

        private static void SetupDirectoryResponse<TResponse>(
            Mock<IRequestAdapter> requestAdapter,
            string responseContent)
            where TResponse : IParsable
        {
            SetupDirectoryResponse<TResponse>(
                requestAdapter,
                CreateJsonResponse(responseContent));
        }

        private static void SetupDirectoryResponse<TResponse>(
            Mock<IRequestAdapter> requestAdapter,
            HttpResponseMessage response)
            where TResponse : IParsable
        {
            requestAdapter
                .Setup(adapter => adapter.SendAsync(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<ParsableFactory<TResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<TResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, _, _, _) => SetNativeResponse(request, response))
                .ReturnsAsync(default(TResponse));
        }

        private static void SetNativeResponse(RequestInformation request, HttpResponseMessage response)
        {
            var responseHandlerOption = request.RequestOptions
                .OfType<ResponseHandlerOption>()
                .Single();
            var nativeResponseHandler = (NativeResponseHandler)responseHandlerOption.ResponseHandler;
            nativeResponseHandler.Value = response;
        }

        private static HttpResponseMessage CreateJsonResponse(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }

        private static Dictionary<string, string> CreateDirectoryEntry(
            Guid id,
            string odataType = null)
        {
            var entry = new Dictionary<string, string>
            {
                ["id"] = id.ToString(),
            };
            if (odataType != null)
            {
                entry["@odata.type"] = $"#{odataType}";
            }

            return entry;
        }

        private static IEnumerable<Dictionary<string, string>> CreateDirectoryEntries(
            int count,
            string odataType = null)
        {
            return Enumerable.Range(0, count)
                .Select(_ => CreateDirectoryEntry(Guid.NewGuid(), odataType));
        }

        private static string CreateMembersPage(
            IEnumerable<Dictionary<string, string>> entries,
            string nextLink = null)
        {
            var response = new Dictionary<string, object>
            {
                ["value"] = entries.ToArray(),
            };
            if (nextLink != null)
            {
                response["@odata.nextLink"] = nextLink;
            }

            return JsonSerializer.Serialize(response);
        }

        private static void AssertHeader(
            RequestInformation request,
            string headerName,
            string expectedValue)
        {
            Assert.IsTrue(request.Headers.TryGetValue(headerName, out var values));
            CollectionAssert.Contains(values.ToList(), expectedValue);
        }

        private sealed class TestRetryPolicyProvider : IRetryPolicyProvider
        {
            private readonly AsyncPolicy<HttpResponseMessage> _retryAfterPolicy;
            private readonly AsyncPolicy<HttpResponseMessage> _exceptionHandlingPolicy;

            public static TestRetryPolicyProvider NoOp { get; } =
                new(
                    Policy.NoOpAsync<HttpResponseMessage>(),
                    Policy.NoOpAsync<HttpResponseMessage>());

            public TestRetryPolicyProvider(
                AsyncPolicy<HttpResponseMessage> retryAfterPolicy,
                AsyncPolicy<HttpResponseMessage> exceptionHandlingPolicy)
            {
                _retryAfterPolicy = retryAfterPolicy;
                _exceptionHandlingPolicy = exceptionHandlingPolicy;
            }

            public AsyncPolicy<HttpResponseMessage> CreateRetryAfterPolicy(Guid? runId)
            {
                PolicyRequests++;
                return _retryAfterPolicy;
            }

            public AsyncPolicy<HttpResponseMessage> CreateExceptionHandlingPolicy(Guid? runId)
            {
                PolicyRequests++;
                return _exceptionHandlingPolicy;
            }

            public int PolicyRequests { get; private set; }
        }

        private sealed class CapturingChannel : ITelemetryChannel
        {
            public ConcurrentQueue<ITelemetry> Items { get; } = new();
            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; } = string.Empty;
            public void Send(ITelemetry item) => Items.Enqueue(item);
            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
