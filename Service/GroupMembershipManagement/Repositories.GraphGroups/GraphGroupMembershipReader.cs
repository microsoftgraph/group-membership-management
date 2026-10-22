// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.DirectoryObjects.GetByIds;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Models;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.Graph.Chats.Item.Members.MembersRequestBuilder;
using Group = Microsoft.Graph.Models.Group;

namespace Repositories.GraphGroups
{
    internal class GraphGroupMembershipReader : GraphGroupRepositoryBase
    {
        private const string AgentUserODataType = "microsoft.graph.agentUser";
        private const int MaxGetByIdsCount = 1000;

        private readonly ILogger<GraphGroupMembershipReader> _graphGroupMembershipReaderLogger;
        private readonly IRetryPolicyProvider _retryPolicyProvider;

        public GraphGroupMembershipReader(GraphServiceClient graphServiceClient,
                                          GraphGroupMetricTracker graphGroupMetricTracker,
                                          IRetryPolicyProvider retryPolicyProvider,
                                          ILogger<GraphGroupMembershipReader> graphGroupMembershipReaderLogger)
                                          : base(graphServiceClient, graphGroupMembershipReaderLogger, graphGroupMetricTracker)
        {
            _retryPolicyProvider = retryPolicyProvider ?? throw new ArgumentNullException(nameof(retryPolicyProvider));
            _graphGroupMembershipReaderLogger = graphGroupMembershipReaderLogger ?? throw new ArgumentNullException(nameof(graphGroupMembershipReaderLogger));
        }

        public async Task<List<AzureADUser>> GetUsersInGroupTransitivelyAsync(Guid groupId, Guid? runId)
        {
            var counts = CreateCounts(TransitiveMemberSelection.UsersAndAgents);
            var transitiveMembers = new List<AzureADUser>();

            try
            {
                var response = await GetFirstTransitiveMembersPageAsync(groupId, null, runId);
                transitiveMembers.AddRange(response.users);
                AddCounts(counts, response.counts);

                var nextPageUrl = response.nextPageUrl;
                while (nextPageUrl != null)
                {
                    var nextPageResult = await GetNextTransitiveMembersPageAsync(groupId, nextPageUrl, null, runId);
                    transitiveMembers.AddRange(nextPageResult.users);
                    AddCounts(counts, nextPageResult.counts);
                    nextPageUrl = nextPageResult.nextPageUrl;
                }

                var nonUserGraphObjectsSummary = FormatSkippedCounts(counts);
                _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From group {groupId}, read {transitiveMembers.Count} users, and the following other directory objects:\n{nonUserGraphObjectsSummary}\n");

                return transitiveMembers;
            }
            catch (ODataError ex)
            {
                _graphGroupMembershipReaderLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
        }

        public async Task<int> GetGroupsCountAsync(Guid groupId, Guid? runId)
        {
            SetCustomActivityProperty("RunId", Convert.ToString(runId));
            var request = _graphServiceClient
                            .Groups[groupId.ToString()]
                            .TransitiveMembers
                            .GraphGroup
                            .Count
                            .ToGetRequestInformation(requestConfiguration =>
                            {
                                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                            });

            var count = await GetGroupDirectoryObjectMembersCount(request, runId);

            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From group {groupId}, transitive count of nested groups: {count}\n");

            return count;
        }

        public async Task<int> GetUsersCountAsync(Guid groupId, Guid? runId)
        {
            SetCustomActivityProperty("RunId", Convert.ToString(runId));
            var request = _graphServiceClient
                .Groups[groupId.ToString()]
                .TransitiveMembers
                .GraphUser
                .Count
                .ToGetRequestInformation(requestConfiguration =>
                {
                    requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                });

            var count = await GetGroupDirectoryObjectMembersCount(request, runId);

            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From group {groupId}, transitive user count {count}\n");

            return count;
        }

        /// <summary>
        /// Counts the Entra <c>agentUser</c> directory objects reachable transitively from a group.
        /// This is telemetry only: it issues a dedicated query and never touches the membership read.
        /// A failure never fails the sync and is never reported as a measured zero (FR-020): an exception,
        /// a missing response, a non-success status, or a missing or invalid <c>@odata.count</c> logs a
        /// warning and returns <see langword="null"/>, so the caller emits no count.
        /// </summary>
        /// <remarks>
        /// The <c>agentUser</c> subtype cannot be recovered from the membership payload - the groups delta
        /// endpoint reports agents as <c>#microsoft.graph.user</c> - so a dedicated query is required.
        /// Two type-cast primitives were measured against Graph and are unusable because both return wrong
        /// numbers with HTTP 200 and no error:
        /// <list type="number">
        /// <item><c>GET /groups/{id}/transitiveMembers/microsoft.graph.agentUser/$count</c> silently ignores the
        /// cast segment and returns the group's <b>total</b> member count.</item>
        /// <item><c>GET /groups/{id}/transitiveMembers/microsoft.graph.agentUser</c> returns an OData error object
        /// embedded inside <c>value[]</c> on any group holding both agents and users, so paging it yields <b>0</b>.</item>
        /// </list>
        /// Only <c>$filter=isof('microsoft.graph.agentUser')</c> is evaluated server-side and stays correct on mixed
        /// groups. <c>$count=true</c> is mandatory (Graph rejects the filter with HTTP 400 without it) and
        /// <c>ConsistencyLevel: eventual</c> is required. <c>$top=1</c> keeps the payload minimal because only
        /// <c>@odata.count</c> is read, making the call O(1) - one request regardless of group size.
        /// </remarks>
        public async Task<int?> GetAgentUserCountAsync(Guid groupId, Guid? runId)
        {
            SetCustomActivityProperty("RunId", Convert.ToString(runId));

            try
            {
                var count = await ReadAgentUserCountAsync(groupId, runId);
                if (count.HasValue)
                {
                    _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From group {groupId}, transitive agent count {count.Value}\n");
                }

                return count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Telemetry must never fail a sync, and a failed count is never reported as zero.
                _graphGroupMembershipReaderLogger.LogWarningWithRunId(runId, $"Unable to read the agent count for group {groupId}; no agent count is reported.\n", ex);
                return null;
            }
        }

        private async Task<int?> ReadAgentUserCountAsync(Guid groupId, Guid? runId)
        {
            var nativeResponseHandler = new NativeResponseHandler();
            var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

            var requestInformation = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = $"{_graphServiceClient.RequestAdapter.BaseUrl}/groups/{groupId}/transitiveMembers?$count=true&$top=1&$filter=isof('{AgentUserODataType}')"
            };

            requestInformation.AddRequestOptions(new List<IRequestOption> { responseHandlerOption });
            requestInformation.Headers.Add("Accept", "application/json");
            requestInformation.Headers.Add("ConsistencyLevel", "eventual");

            // When using the native response handler it sets the return value to null.
            // So we need to extract the response from the native response handler.
            await _graphServiceClient.RequestAdapter.SendPrimitiveAsync<int?>(requestInformation);

            using var nativeHttpResponse = nativeResponseHandler.Value as HttpResponseMessage;

            if (nativeHttpResponse == null)
            {
                _graphGroupMembershipReaderLogger.LogWarningWithRunId(runId, $"The agent count query for group {groupId} returned no response; no agent count is reported.\n");
                return null;
            }

            var headers = nativeHttpResponse.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

            if (!nativeHttpResponse.IsSuccessStatusCode)
            {
                _graphGroupMembershipReaderLogger.LogWarningWithRunId(runId, $"The agent count query for group {groupId} returned {(int)nativeHttpResponse.StatusCode}; no agent count is reported.\n");
                return null;
            }

            var responseContent = await nativeHttpResponse.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("@odata.count", out var odataCount) &&
                odataCount.ValueKind == JsonValueKind.Number &&
                odataCount.TryGetInt32(out var count) &&
                count >= 0)
            {
                return count;
            }

            _graphGroupMembershipReaderLogger.LogWarningWithRunId(runId, $"The agent count query for group {groupId} returned no valid @odata.count; no agent count is reported.\n");
            return null;
        }

        public async Task<(List<AzureADUser> users,
                           MemberReadCounts counts,
                           string nextPageUrl)> GetFirstTransitiveMembersPageAsync(
            Guid groupId,
            IReadOnlyList<string> identityTypes,
            Guid? runId,
            CancellationToken cancellationToken = default)
        {
            var selection = ResolveSelection(identityTypes);
            var users = new List<AzureADUser>();
            var counts = CreateCounts(selection);
            string nextLink = null;

            for (int i = 0; i < 5; i++)
            {
                var page = string.IsNullOrEmpty(nextLink)
                    ? await GetGroupTransitiveMembersPageByIdAsync(groupId.ToString(), selection, runId, cancellationToken)
                    : await GetGroupTransitiveMembersNextPageAsync(nextLink, groupId, selection, runId, cancellationToken);

                // Resource units were recorded for every attempt of this page inside the retry policy.
                await _graphGroupMetricTracker.TrackRequestAsync(page.Headers, groupId, QueryType.Transitive, runId);

                users.AddRange(ToSelectedUsers(page.Members, selection, counts));
                nextLink = page.NextLink;
                if (string.IsNullOrEmpty(nextLink))
                {
                    break;
                }
            }

            return (users, counts, nextLink);
        }

        public async Task<(List<AzureADUser> users,
                           MemberReadCounts counts,
                           string nextPageUrl)> GetNextTransitiveMembersPageAsync(
            Guid groupId,
            string nextPageUrl,
            IReadOnlyList<string> identityTypes,
            Guid? runId,
            CancellationToken cancellationToken = default)
        {
            var selection = ResolveSelection(identityTypes);
            var users = new List<AzureADUser>();
            var counts = CreateCounts(selection);
            var nextLink = nextPageUrl;

            for (int i = 0; i < 5 && !string.IsNullOrEmpty(nextLink); i++)
            {
                var page = await GetGroupTransitiveMembersNextPageAsync(nextLink, groupId, selection, runId, cancellationToken);

                // Resource units were recorded for every attempt of this page inside the retry policy.
                await _graphGroupMetricTracker.TrackRequestAsync(page.Headers, groupId, QueryType.Transitive, runId);

                users.AddRange(ToSelectedUsers(page.Members, selection, counts));
                nextLink = page.NextLink;
            }

            return (users, counts, nextLink);
        }

        public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid groupId, Guid? runId)
        {
            try
            {
                var members = new List<IAzureADObject>();
                var membersResponse = await GetGroupMembersPageByIdAsync(groupId.ToString(), runId);
                members.AddRange(ToEntities(membersResponse.Response.Value));

                await _graphGroupMetricTracker.TrackMetricsAsync(membersResponse.Headers, QueryType.Other, runId);

                while (membersResponse.Response.OdataNextLink != null)
                {
                    membersResponse = await GetGroupMembersNextPageAsync(membersResponse.Response.OdataNextLink, groupId, runId);
                    members.AddRange(ToEntities(membersResponse.Response.Value));
                    await _graphGroupMetricTracker.TrackMetricsAsync(membersResponse.Headers, QueryType.Other, runId);
                }

                return members;
            }
            catch (ServiceException ex)
            {
                _graphGroupMembershipReaderLogger.LogErrorWithRunId(runId, "Unable to retrieve group members.\n" + ex.GetBaseException().ToString(), ex);

                throw;
            }
        }

        public async Task<List<AzureADGroup>> GetDirectGroupMembersAsync(Guid groupId, Guid? runId)
        {
            var groups = new List<AzureADGroup>();
            try
            {
                _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"Reading direct group-type members of group {groupId}.");

                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient
                        .Groups[groupId.ToString()]
                        .Members
                        .GraphGroup
                        .GetAsync(requestConfig =>
                        {
                            requestConfig.QueryParameters.Top = MaxResultCount;
                            requestConfig.QueryParameters.Select = new[] { "id", "displayName" };
                            requestConfig.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                        });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse == null)
                {
                    return groups;
                }

                var initialHeaders = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(initialHeaders, QueryType.Other, runId);

                if (!nativeResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to retrieve direct group-type members. Status code: {nativeResponse.StatusCode}");
                }

                var initialResponse = await DeserializeResponseAsync(nativeResponse, GroupCollectionResponse.CreateFromDiscriminatorValue);

                if (initialResponse?.Value != null)
                {
                    groups.AddRange(initialResponse.Value.Where(g => g.Id != null).Select(g => new AzureADGroup
                    {
                        ObjectId = Guid.Parse(g.Id),
                        Name = g.DisplayName
                    }));
                }

                // Page through results if needed
                var nextLink = initialResponse?.OdataNextLink;
                while (!string.IsNullOrEmpty(nextLink))
                {
                    var requestInfo = new RequestInformation
                    {
                        HttpMethod = Method.GET,
                        UrlTemplate = nextLink,
                    };

                    var pageResponseHandler = new NativeResponseHandler();
                    requestInfo.AddRequestOptions(new IRequestOption[] { new ResponseHandlerOption { ResponseHandler = pageResponseHandler } });

                    await _graphServiceClient.RequestAdapter.SendAsync<GroupCollectionResponse>(requestInfo, GroupCollectionResponse.CreateFromDiscriminatorValue);

                    var pageResponseMessage = pageResponseHandler.Value as HttpResponseMessage;

                    if (pageResponseMessage == null)
                    {
                        break;
                    }

                    var pageHeaders = pageResponseMessage.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(pageHeaders, QueryType.Other, runId);

                    if (!pageResponseMessage.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Failed to retrieve direct group-type members page. Status code: {pageResponseMessage.StatusCode}");
                    }

                    var nextResponse = await DeserializeResponseAsync(pageResponseMessage, GroupCollectionResponse.CreateFromDiscriminatorValue);

                    if (nextResponse?.Value != null)
                    {
                        groups.AddRange(nextResponse.Value.Where(g => g.Id != null).Select(g => new AzureADGroup
                        {
                            ObjectId = Guid.Parse(g.Id),
                            Name = g.DisplayName
                        }));
                    }

                    nextLink = nextResponse?.OdataNextLink;
                }

                _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"Retrieved {groups.Count} direct group-type members of {groupId}.");
                return groups;
            }
            catch (ODataError ex)
            {
                _graphGroupMembershipReaderLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);
                throw;
            }
        }

        private async Task<int> GetGroupDirectoryObjectMembersCount(RequestInformation request, Guid? runId)
        {
            var nativeResponseHandler = new NativeResponseHandler();
            var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

            // When using the native response handler it sets the return value to null.
            // So we need to extract the response from the native response handler.
            var options = request.RequestOptions.ToList();
            options.Add(responseHandlerOption);

            request.AddRequestOptions(options);
            await _graphServiceClient.RequestAdapter.SendPrimitiveAsync<int?>(request);

            var nativeHttpResponse = nativeResponseHandler.Value as HttpResponseMessage;

            var headers = nativeHttpResponse.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

            var responseContent = await nativeHttpResponse.Content.ReadAsStringAsync();
            return int.Parse(responseContent);
        }

        public async Task<IReadOnlyDictionary<Guid, string>> GetDirectoryObjectTypesAsync(
            IReadOnlyCollection<Guid> ids,
            Guid? runId,
            CancellationToken cancellationToken)
        {
            if (ids == null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            if (ids.Count > MaxGetByIdsCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ids),
                    ids.Count,
                    $"A directory-object type lookup accepts at most {MaxGetByIdsCount} identifiers.");
            }

            if (ids.Count == 0)
            {
                return new Dictionary<Guid, string>();
            }

            // One retry owner: the shared retry policies (the SDK retry handler is disabled on the request). Resource units
            // are recorded for every attempt, including throttled and retried ones, before the policy inspects the response.
            var retryPolicy = CreateSharedRetryPolicy(runId);

            using var response = await ExecuteWithExhaustionAsync(
                retryPolicy,
                "The directory-object type lookup",
                async token =>
                {
                    var attemptResponse = await SendGetByIdsRequestAsync(ids, token);
                    var headers = attemptResponse.Headers.ToImmutableDictionary(
                        header => header.Key,
                        header => header.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(
                        headers,
                        QueryType.Other,
                        runId);
                    return attemptResponse;
                },
                cancellationToken);

            ThrowIfNotSuccess(response, "The directory-object type lookup");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseDirectoryObjectTypes(responseContent);
        }

        private async Task<TransitiveMembersPage> GetGroupTransitiveMembersPageByIdAsync(
            string groupId,
            TransitiveMemberSelection selection,
            Guid? runId,
            CancellationToken cancellationToken)
        {
            var requestUrl = selection switch
            {
                TransitiveMemberSelection.UsersOnly =>
                    $"{_graphServiceClient.RequestAdapter.BaseUrl}/groups/{groupId}/transitiveMembers/microsoft.graph.user?$filter=not isof('{AgentUserODataType}')&$count=true&$top={MaxResultCount}",
                TransitiveMemberSelection.AgentsOnly =>
                    $"{_graphServiceClient.RequestAdapter.BaseUrl}/groups/{groupId}/transitiveMembers?$filter=isof('{AgentUserODataType}')&$count=true&$top={MaxResultCount}",
                _ => null,
            };

            return await GetGroupTransitiveMembersPageAsync(requestUrl, groupId, selection, runId, isFirstPage: true, cancellationToken);
        }

        private async Task<TransitiveMembersPage> GetGroupTransitiveMembersNextPageAsync(
            string nextPageUrl,
            Guid groupId,
            TransitiveMemberSelection selection,
            Guid? runId,
            CancellationToken cancellationToken)
        {
            return await GetGroupTransitiveMembersPageAsync(nextPageUrl, groupId.ToString(), selection, runId, isFirstPage: false, cancellationToken);
        }

        private async Task<TransitiveMembersPage> GetGroupTransitiveMembersPageAsync(
            string requestUrl,
            string groupId,
            TransitiveMemberSelection selection,
            Guid? runId,
            bool isFirstPage,
            CancellationToken cancellationToken)
        {
            // One retry owner: the shared retry policies. The SDK retry handler is disabled on every send, and the
            // legacy ServiceException retry is not applied, so each policy attempt is exactly one physical request.
            var retryPolicy = CreateSharedRetryPolicy(runId);
            string requestId = string.Empty;
            string clientRequestId = Guid.NewGuid().ToString();

            using var nativeResponse = await ExecuteWithExhaustionAsync(
                retryPolicy,
                "The transitive-members request",
                async token =>
                {
                    SetCustomActivityProperty("RunId", Convert.ToString(runId));
                    SetCustomActivityProperty("ClientRequestId", Convert.ToString(clientRequestId));

                    var nativeResponseHandler = new NativeResponseHandler();
                    var requestOptions = new List<IRequestOption>
                    {
                        new ResponseHandlerOption { ResponseHandler = nativeResponseHandler },
                        new RetryHandlerOption { MaxRetry = 0 },
                    };

                    if (isFirstPage && selection == TransitiveMemberSelection.UsersAndAgents)
                    {
                        await _graphServiceClient
                            .Groups[groupId]
                            .TransitiveMembers
                            .GetAsync(requestConfiguration =>
                            {
                                requestConfiguration.QueryParameters.Top = MaxResultCount;
                                foreach (var option in requestOptions)
                                {
                                    requestConfiguration.Options.Add(option);
                                }
                            }, token);
                    }
                    else
                    {
                        var requestInformation = new RequestInformation
                        {
                            HttpMethod = Method.GET,
                            UrlTemplate = requestUrl,
                        };

                        requestInformation.AddRequestOptions(requestOptions);
                        requestInformation.Headers.Add("Accept", "application/json");

                        if (selection != TransitiveMemberSelection.UsersAndAgents)
                        {
                            requestInformation.Headers.Add("ConsistencyLevel", "eventual");
                        }

                        if (selection == TransitiveMemberSelection.UsersOnly)
                        {
                            await _graphServiceClient
                                .RequestAdapter
                                .SendAsync(requestInformation, UserCollectionResponse.CreateFromDiscriminatorValue, cancellationToken: token);
                        }
                        else
                        {
                            await _graphServiceClient
                                .RequestAdapter
                                .SendAsync(requestInformation, DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue, cancellationToken: token);
                        }
                    }

                    var attemptResponse = nativeResponseHandler.Value as HttpResponseMessage
                        ?? throw new InvalidOperationException("The transitive-members request returned no native response.");

                    // Resource units are recorded for every attempt, including throttled and retried ones, before the
                    // policy inspects the response and disposes a retried one.
                    await _graphGroupMetricTracker.TrackMetricsAsync(
                        attemptResponse.Headers.ToImmutableDictionary(header => header.Key, header => header.Value),
                        QueryType.Transitive,
                        runId);
                    return attemptResponse;
                },
                cancellationToken);

            requestId = ExtractRequestId(nativeResponse);

            ThrowIfNotSuccess(nativeResponse, "The transitive-members request");

            var response = await DeserializeTransitiveMembersPageAsync(nativeResponse, selection);

            var pageName = isFirstPage ? "first" : "subsequent";
            _graphGroupMembershipReaderLogger.LogInformationWithRunId(
                runId,
                $"From {pageName} page of transitive members for group {groupId}, member count {response.Members.Count} GraphRequestId: {requestId} ClientRequestId: {clientRequestId}");

            return response;
        }

        private async Task<TransitiveMembersPage> DeserializeTransitiveMembersPageAsync(
            HttpResponseMessage nativeResponse,
            TransitiveMemberSelection selection)
        {
            if (selection == TransitiveMemberSelection.UsersOnly)
            {
                var userResponse = await DeserializeResponseAsync(nativeResponse, UserCollectionResponse.CreateFromDiscriminatorValue);
                if (userResponse == null)
                {
                    throw new InvalidOperationException(
                        "Graph returned an unreadable transitive-members page.");
                }

                return new TransitiveMembersPage
                {
                    Members = userResponse.Value?.Cast<DirectoryObject>().ToList() ?? new List<DirectoryObject>(),
                    NextLink = userResponse.OdataNextLink,
                    Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value),
                };
            }

            var directoryObjectResponse = await DeserializeResponseAsync(
                nativeResponse,
                DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);
            if (directoryObjectResponse == null)
            {
                throw new InvalidOperationException(
                    "Graph returned an unreadable transitive-members page.");
            }

            return new TransitiveMembersPage
            {
                Members = directoryObjectResponse.Value ?? new List<DirectoryObject>(),
                NextLink = directoryObjectResponse.OdataNextLink,
                Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value),
            };
        }

        /// <summary>
        /// The single retry owner for a request whose Graph SDK retry handler is disabled: the shared 429 policy
        /// (Retry-After, configured attempts) around the shared transient policy (500/502/503/504 and
        /// <see cref="HttpRequestException"/>, exponential backoff, configured attempts), all inside one retry of an
        /// HttpClient timeout (owner, 2026-10-21: the original send plus one retry). The timeout policy is outermost so
        /// it counts timeouts across the whole request: a second timeout is exhaustion even after 429 or 5xx retries in
        /// between. When attempts are exhausted the last response is returned, or the last exception is thrown, and the
        /// caller fails the request.
        /// </summary>
        private IAsyncPolicy<HttpResponseMessage> CreateSharedRetryPolicy(Guid? runId) =>
            CreateHttpClientTimeoutPolicy(runId)
                .WrapAsync(_retryPolicyProvider.CreateRetryAfterPolicy(runId))
                .WrapAsync(_retryPolicyProvider.CreateExceptionHandlingPolicy(runId));

        /// <summary>
        /// Runs the request under its retry owner and turns a transport failure or a second HttpClient timeout that
        /// survives the retries into <see cref="GraphRetriesExhaustedException"/>. Caller or host cancellation propagates.
        /// </summary>
        private static async Task<HttpResponseMessage> ExecuteWithExhaustionAsync(
            IAsyncPolicy<HttpResponseMessage> retryPolicy,
            string requestDescription,
            Func<CancellationToken, Task<HttpResponseMessage>> send,
            CancellationToken cancellationToken)
        {
            try
            {
                return await retryPolicy.ExecuteAsync(send, cancellationToken);
            }
            catch (HttpRequestException exception) when (exception is not GraphRetriesExhaustedException && exception.StatusCode == null)
            {
                throw new GraphRetriesExhaustedException(
                    $"{requestDescription} still failed after the configured retries: {exception.Message}",
                    exception);
            }
            catch (TaskCanceledException exception) when (IsHttpClientTimeout(exception) && !cancellationToken.IsCancellationRequested)
            {
                throw new GraphRetriesExhaustedException(
                    $"{requestDescription} still failed after the configured retries: the HttpClient timeout elapsed again.",
                    exception);
            }
        }

        /// <summary>
        /// Throws for a non-success final response: <see cref="GraphRetriesExhaustedException"/> for a status the retry
        /// owner retries (the attempts are exhausted), otherwise <see cref="HttpRequestException"/>.
        /// </summary>
        private static void ThrowIfNotSuccess(HttpResponseMessage response, string requestDescription)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var message = $"{requestDescription} failed with status {(int)response.StatusCode} ({response.StatusCode}).";
            if (GraphRetriesExhaustedException.IsRetryableStatus(response.StatusCode))
            {
                throw new GraphRetriesExhaustedException(
                    $"{message} The configured retries are exhausted.",
                    null,
                    response.StatusCode);
            }

            throw new HttpRequestException(message, null, response.StatusCode);
        }

        /// <summary>The retries of an HttpClient timeout: one, so a request sees at most two timeouts.</summary>
        internal const int HttpClientTimeoutRetries = 1;

        /// <summary>
        /// Retries a send that ended because the HttpClient timeout elapsed (a <see cref="TaskCanceledException"/> whose
        /// inner exception is a <see cref="TimeoutException"/>). Caller or host cancellation has no such inner exception
        /// and is never retried.
        /// </summary>
        private IAsyncPolicy<HttpResponseMessage> CreateHttpClientTimeoutPolicy(Guid? runId) =>
            Policy<HttpResponseMessage>
                .Handle<TaskCanceledException>(IsHttpClientTimeout)
                .WaitAndRetryAsync(
                    HttpClientTimeoutRetries,
                    _ => TimeSpan.FromSeconds(2),
                    (outcome, wait, attempt, _) =>
                    {
                        _graphGroupMembershipReaderLogger.LogWarningWithRunId(
                            runId,
                            $"A Graph request timed out on the HttpClient; retry {attempt} of {HttpClientTimeoutRetries} in {wait}.");
                    });

        internal static bool IsHttpClientTimeout(Exception exception) =>
            exception is TaskCanceledException { InnerException: TimeoutException };

        private async Task<HttpResponseMessage> SendGetByIdsRequestAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken)
        {
            var requestBody = new GetByIdsPostRequestBody
            {
                Ids = ids.Select(id => id.ToString()).ToList(),
            };

            var nativeResponseHandler = new NativeResponseHandler();
            var requestInformation = _graphServiceClient
                .DirectoryObjects
                .GetByIds
                .WithUrl($"{_graphServiceClient.RequestAdapter.BaseUrl}/directoryObjects/getByIds?$select=id")
                .ToPostRequestInformation(
                    requestBody,
                    requestConfiguration =>
                    {
                        requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                        requestConfiguration.Options.Add(new RetryHandlerOption { MaxRetry = 0 });
                    });

            await _graphServiceClient
                        .RequestAdapter
                .SendNoContentAsync(requestInformation, cancellationToken: cancellationToken);

            return nativeResponseHandler.Value as HttpResponseMessage
                ?? throw new InvalidOperationException("The directory-object type lookup returned no native response.");
        }

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupMembersPageByIdAsync(string groupId, Guid? runId)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<DirectoryObjectCollectionResponse>();
            string requestId = string.Empty;
            string clientRequestId = Guid.NewGuid().ToString();

            await retryPolicy.ExecuteAsync(async () =>
            {
                SetCustomActivityProperty("RunId", Convert.ToString(runId));
                SetCustomActivityProperty("ClientRequestId", Convert.ToString(clientRequestId));

                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

                await _graphServiceClient
                       .Groups[groupId]
                       .Members
                       .GetAsync(requestConfiguration =>
                       {
                           requestConfiguration.QueryParameters.Top = MaxResultCount;
                           requestConfiguration.QueryParameters.Select = new[] { "id" };
                           requestConfiguration.Options.Add(responseHandlerOption);
                       });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                requestId = ExtractRequestId(nativeResponse);

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                       DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From first page of members for group {groupId}, member count {response.Response?.Value?.Count ?? -1} GraphRequestId: {requestId} ClientRequestId: {clientRequestId}");

            return response;
        }

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupMembersNextPageAsync(string nextPageUrl, Guid groupId, Guid? runId)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<DirectoryObjectCollectionResponse>();
            string requestId = string.Empty;
            string clientRequestId = Guid.NewGuid().ToString();

            await retryPolicy.ExecuteAsync(async () =>
            {
                SetCustomActivityProperty("RunId", Convert.ToString(runId));
                SetCustomActivityProperty("ClientRequestId", Convert.ToString(clientRequestId));

                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

                var requestInformation = new RequestInformation
                {
                    HttpMethod = Method.GET,
                    UrlTemplate = nextPageUrl,
                };

                var requestConfig = new MembersRequestBuilderGetRequestConfiguration
                {
                    Options = new List<IRequestOption> { responseHandlerOption }
                };

                requestInformation.AddRequestOptions(requestConfig.Options);
                requestInformation.Headers.Add("Accept", "application/json");

                await _graphServiceClient
                        .RequestAdapter
                        .SendAsync(requestInformation,
                                    DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                requestId = ExtractRequestId(nativeResponse);

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                           DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From subsequent page of members for group {groupId}, member count {response.Response?.Value?.Count ?? -1} GraphRequestId: {requestId} ClientRequestId: {clientRequestId}");

            return response;
        }

        public async Task<bool> IsEmailRecipientMemberOfGroupAsync(string userIdentifier, Guid groupObjectId, Guid? runId)
        {
            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"Checking on user existence to determine if it is a member of group {groupObjectId}.");

            SetCustomActivityProperty("RunId", Convert.ToString(runId));

            Guid userId;

            var identifierIsObjectId = Guid.TryParse(userIdentifier, out userId);

            if (!identifierIsObjectId)
            {
                _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, "Getting user information.");

                User user = null;
                var nativeResponseHandler = new NativeResponseHandler();
                var userByMailResponse = new UserCollectionResponse();

                try
                {

                    await _graphServiceClient.Users.GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = $"Mail eq '{userIdentifier}' or UserPrincipalName eq '{userIdentifier}'";
                        requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                    });

                    var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                    if (nativeResponse != null)
                    {
                        var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                        await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);
                    }

                    if (nativeResponse.IsSuccessStatusCode)
                    {
                        userByMailResponse = await DeserializeResponseAsync(nativeResponse, UserCollectionResponse.CreateFromDiscriminatorValue);

                        if (userByMailResponse.Value.Count == 0)
                        {
                            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, "No user was found when checking for user.");

                            return false;
                        }

                        user = userByMailResponse.Value[0];
                        Guid.TryParse(user.Id, out userId);
                    }
                    else
                    {
                        throw new Exception($"Unable to verify user existence at this time.");
                    }
                }
                catch (Exception ex)
                {
                    _graphGroupMembershipReaderLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                    throw;
                }
            }
            else
            {
                _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, "User identifier is already an object id. No Graph call required.");
            }


            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"Checking if email recipient is a member of the group {groupObjectId}.");

            return await IsGroupMemberAsync($"id eq '{userId}'", groupObjectId, runId);
        }
        private async Task<bool> IsGroupMemberAsync(string query, Guid groupObjectId, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var groupOwnersResponse = new DirectoryObjectCollectionResponse();

                await _graphServiceClient.Groups[groupObjectId.ToString()]
                                            .Members.GetAsync(requestConfiguration =>
                                            {
                                                requestConfiguration.QueryParameters.Filter = query;
                                                requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                                            });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse.IsSuccessStatusCode)
                {
                    groupOwnersResponse = await DeserializeResponseAsync(nativeResponse,
                                                                         DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                return groupOwnersResponse.Value?.Any() ?? false;
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return false;

                _graphGroupMembershipReaderLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
        }

        private IEnumerable<IAzureADObject> ToEntities(IEnumerable<DirectoryObject> fromGraph)
        {
            if (fromGraph == null)
                yield break;

            foreach (var directoryObj in fromGraph)
            {
                switch (directoryObj)
                {
                    case User user:
                        yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
                        break;
                    case Group group:
                        yield return new AzureADGroup { ObjectId = Guid.Parse(group.Id) };
                        break;
                    default:
                        break;
                }
            }
        }

        private IEnumerable<AzureADUser> ToSelectedUsers(
            IEnumerable<DirectoryObject> directoryObjects,
            TransitiveMemberSelection selection,
            MemberReadCounts counts)
        {
            if (selection == TransitiveMemberSelection.UsersAndAgents)
            {
                return ToUsers(directoryObjects, counts);
            }

            var users = new List<AzureADUser>();

            foreach (var directoryObject in directoryObjects ?? Enumerable.Empty<DirectoryObject>())
            {
                if (selection == TransitiveMemberSelection.UsersOnly)
                {
                    if (directoryObject is not AgentUser
                        && directoryObject is User user
                        && !HasUnexpectedODataType(user, "microsoft.graph.user"))
                    {
                        counts.MemberCount++;
                        counts.UserCount = (counts.UserCount ?? 0) + 1;
                        users.Add(new AzureADUser { ObjectId = Guid.Parse(user.Id) });
                        continue;
                    }

                    IncrementSkippedSelectedCount(counts, directoryObject);
                    continue;
                }

                if (directoryObject is AgentUser agentUser
                    && !HasUnexpectedODataType(agentUser, "microsoft.graph.agentUser"))
                {
                    counts.MemberCount++;
                    counts.AgentCount = (counts.AgentCount ?? 0) + 1;
                    users.Add(new AzureADUser { ObjectId = Guid.Parse(agentUser.Id) });
                    continue;
                }

                IncrementSkippedSelectedCount(counts, directoryObject);
            }

            return users;
        }

        private static bool HasUnexpectedODataType(
            DirectoryObject directoryObject,
            string expectedType)
        {
            return !string.IsNullOrWhiteSpace(directoryObject.OdataType)
                && !string.Equals(
                    directoryObject.OdataType.TrimStart('#'),
                    expectedType,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void IncrementSkippedSelectedCount(
            MemberReadCounts counts,
            DirectoryObject directoryObject)
        {
            var odataType = directoryObject?.OdataType;
            if (!string.IsNullOrWhiteSpace(odataType))
            {
                var normalizedType = odataType.TrimStart('#');
                if (string.Equals(
                    normalizedType,
                    "microsoft.graph.agentUser",
                    StringComparison.OrdinalIgnoreCase))
                {
                    counts.AgentCount = (counts.AgentCount ?? 0) + 1;
                    return;
                }

                if (string.Equals(
                    normalizedType,
                    "microsoft.graph.user",
                    StringComparison.OrdinalIgnoreCase))
                {
                    counts.UserCount = (counts.UserCount ?? 0) + 1;
                    return;
                }

                IncrementSkippedCount(counts, odataType);
                return;
            }

            switch (directoryObject)
            {
                case AgentUser:
                    counts.AgentCount = (counts.AgentCount ?? 0) + 1;
                    break;
                case User:
                    counts.UserCount = (counts.UserCount ?? 0) + 1;
                    break;
                default:
                    IncrementSkippedCount(counts, null);
                    break;
            }
        }

        private static IReadOnlyDictionary<Guid, string> ParseDirectoryObjectTypes(string responseContent)
        {
            if (string.IsNullOrWhiteSpace(responseContent))
            {
                throw new InvalidOperationException("The directory-object type lookup returned an empty response.");
            }

            try
            {
                using var document = JsonDocument.Parse(responseContent);

                if (!document.RootElement.TryGetProperty("value", out var values)
                    || values.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("The directory-object type lookup response has no value array.");
                }

                var typesById = new Dictionary<Guid, string>();

                foreach (var value in values.EnumerateArray())
                {
                    if (!value.TryGetProperty("id", out var idElement)
                        || idElement.ValueKind != JsonValueKind.String
                        || !Guid.TryParse(idElement.GetString(), out var id))
                    {
                        throw new InvalidOperationException("A directory-object type lookup result has no valid identifier.");
                    }

                    if (!value.TryGetProperty("@odata.type", out var typeElement)
                        || typeElement.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException($"The directory-object type lookup result for {id} has no type.");
                    }

                    var normalizedType = NormalizeDirectoryObjectType(typeElement.GetString());
                    typesById[id] = normalizedType;
                }

                return typesById;
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("The directory-object type lookup returned invalid JSON.", exception);
            }
        }

        private static string NormalizeDirectoryObjectType(string odataType)
        {
            if (string.Equals(odataType, "#microsoft.graph.user", StringComparison.OrdinalIgnoreCase))
            {
                return "user";
            }

            if (string.Equals(odataType, "#microsoft.graph.agentUser", StringComparison.OrdinalIgnoreCase))
            {
                return "agentUser";
            }

            throw new InvalidOperationException(
                $"The directory-object type lookup returned unsupported type '{odataType ?? "<missing>"}'.");
        }

        private static TransitiveMemberSelection ResolveSelection(IReadOnlyList<string> identityTypes)
        {
            if (identityTypes == null)
            {
                return TransitiveMemberSelection.UsersAndAgents;
            }

            var hasUsers = identityTypes.Contains("users", StringComparer.Ordinal);
            var hasAgents = identityTypes.Contains("agents", StringComparer.Ordinal);

            if (identityTypes.Count == 1 && hasUsers)
            {
                return TransitiveMemberSelection.UsersOnly;
            }

            if (identityTypes.Count == 1 && hasAgents)
            {
                return TransitiveMemberSelection.AgentsOnly;
            }

            if (identityTypes.Count == 2 && hasUsers && hasAgents)
            {
                return TransitiveMemberSelection.UsersAndAgents;
            }

            throw new ArgumentException(
                "A group identity selection must contain users, agents, or both exactly once.",
                nameof(identityTypes));
        }

        private static MemberReadCounts CreateCounts(TransitiveMemberSelection selection)
        {
            return new MemberReadCounts
            {
                UserCount = selection == TransitiveMemberSelection.AgentsOnly ? null : 0,
                AgentCount = selection == TransitiveMemberSelection.UsersOnly ? null : 0,
            };
        }

        private static void AddCounts(MemberReadCounts destination, MemberReadCounts source)
        {
            destination.MemberCount += source.MemberCount;
            destination.UserCount = AddNullableCounts(destination.UserCount, source.UserCount);
            destination.AgentCount = AddNullableCounts(destination.AgentCount, source.AgentCount);
            destination.ServicePrincipalCount += source.ServicePrincipalCount;
            destination.DeviceCount += source.DeviceCount;
            destination.ContactCount += source.ContactCount;
            destination.GroupCount += source.GroupCount;
            destination.UnknownTypeCount += source.UnknownTypeCount;
            destination.OtherCount += source.OtherCount;
        }

        private static int? AddNullableCounts(int? left, int? right)
        {
            return left.HasValue && right.HasValue
                ? left.Value + right.Value
                : left ?? right;
        }

        private static string FormatSkippedCounts(MemberReadCounts counts)
        {
            return string.Join(
                Environment.NewLine,
                new[]
                {
                    $"{counts.ServicePrincipalCount}: service principals",
                    $"{counts.DeviceCount}: devices",
                    $"{counts.ContactCount}: contacts",
                    $"{counts.GroupCount}: groups",
                    $"{counts.UnknownTypeCount}: unknown type",
                    $"{counts.OtherCount}: other",
                });
        }

        private static void SetCustomActivityProperty(string propertyName, string value)
        {
            if(string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(value)) return;
            var activity = Activity.Current;
            if (activity == null) return;
            if (activity.GetTagItem(propertyName) == null)
                activity.SetTag(propertyName, value);
            activity.AddBaggage(propertyName, value);
        }

        private static string? ExtractRequestId(HttpResponseMessage httpResponse)
        {
            string requestId = null;
            if (httpResponse?.Headers.TryGetValues("request-id", out var requestIds) == true)
            {
                requestId = requestIds.FirstOrDefault();
            }

            return requestId;
        }

        private enum TransitiveMemberSelection
        {
            UsersAndAgents,
            UsersOnly,
            AgentsOnly,
        }

        private sealed class TransitiveMembersPage
        {
            public IReadOnlyList<DirectoryObject> Members { get; init; } = Array.Empty<DirectoryObject>();

            public string NextLink { get; init; }

            public IDictionary<string, IEnumerable<string>> Headers { get; init; } =
                new Dictionary<string, IEnumerable<string>>();
        }
    }
}
