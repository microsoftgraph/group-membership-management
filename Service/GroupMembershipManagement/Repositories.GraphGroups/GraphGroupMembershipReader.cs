// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Models;
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
using System.Threading.Tasks;
using static Microsoft.Graph.Chats.Item.Members.MembersRequestBuilder;
using static Microsoft.Graph.Groups.Item.TransitiveMembers.TransitiveMembersRequestBuilder;
using Group = Microsoft.Graph.Models.Group;

namespace Repositories.GraphGroups
{
    internal class GraphGroupMembershipReader : GraphGroupRepositoryBase
    {
        private readonly ILogger<GraphGroupMembershipReader> _graphGroupMembershipReaderLogger;

        public GraphGroupMembershipReader(GraphServiceClient graphServiceClient,
                                          GraphGroupMetricTracker graphGroupMetricTracker,
                                          ILogger<GraphGroupMembershipReader> graphGroupMembershipReaderLogger)
                                          : base(graphServiceClient, graphGroupMembershipReaderLogger, graphGroupMetricTracker)
        {
            _graphGroupMembershipReaderLogger = graphGroupMembershipReaderLogger ?? throw new ArgumentNullException(nameof(graphGroupMembershipReaderLogger));
        }

        public async Task<List<AzureADUser>> GetUsersInGroupTransitivelyAsync(Guid groupId, Guid? runId)
        {
            var nonUserGraphObjects = new List<KeyValuePair<string, int>>();
            var transitiveMembers = new List<AzureADUser>();

            try
            {
                var response = await GetFirstTransitiveMembersPageAsync(groupId, runId);
                transitiveMembers.AddRange(response.users);
                nonUserGraphObjects.AddRange(response.nonUserGraphObjects);

                var nextPageUrl = response.nextPageUrl;
                while (nextPageUrl != null)
                {
                    var nextPageResult = await GetNextTransitiveMembersPageAsync(groupId, nextPageUrl, runId);
                    transitiveMembers.AddRange(nextPageResult.users);
                    nonUserGraphObjects.AddRange(nextPageResult.nonUserGraphObjects);
                    nextPageUrl = nextPageResult.nextPageUrl;
                }

                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, nonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
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

        public async Task<(List<AzureADUser> users,
                   Dictionary<string, int> nonUserGraphObjects,
                   string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid groupId, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            string nextLink = null;

            for (int i = 0; i < 5; i++)
            {
                var usersResponse = string.IsNullOrEmpty(nextLink)
                    ? await GetGroupTransitiveMembersPageByIdAsync(groupId.ToString(), runId)
                    : await GetGroupTransitiveMembersNextPageAsync(nextLink, groupId, runId);

                await _graphGroupMetricTracker.TrackMetricsAsync(usersResponse.Headers, QueryType.Transitive, runId);
                await _graphGroupMetricTracker.TrackRequestAsync(usersResponse.Headers, groupId, QueryType.Transitive, runId);

                users.AddRange(ToUsers(usersResponse.Response.Value, nonUserGraphObjects));
                nextLink = usersResponse.Response.OdataNextLink;
                if (string.IsNullOrEmpty(nextLink))
                {
                    break;
                }
            }
            return (users, nonUserGraphObjects, nextLink);
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl)> GetNextTransitiveMembersPageAsync(Guid groupId, string nextPageUrl, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            var nextLink = nextPageUrl;

            for (int i = 0; i < 5 && !string.IsNullOrEmpty(nextLink); i++)
            {
                var usersResponse = await GetGroupTransitiveMembersNextPageAsync(nextLink, groupId, runId);

                await _graphGroupMetricTracker.TrackMetricsAsync(usersResponse.Headers, QueryType.Transitive, runId);
                await _graphGroupMetricTracker.TrackRequestAsync(usersResponse.Headers, groupId, QueryType.Transitive, runId);

                users.AddRange(ToUsers(usersResponse.Response.Value, nonUserGraphObjects));
                nextLink = usersResponse.Response.OdataNextLink;
            }
            return (users, nonUserGraphObjects, nextLink);
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

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupTransitiveMembersPageByIdAsync(string groupId, Guid? runId)
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
                       .TransitiveMembers
                       .GetAsync(requestConfiguration =>
                       {
                           requestConfiguration.QueryParameters.Top = MaxResultCount;
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

            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From first page of transitive members for group {groupId}, member count {response.Response?.Value?.Count ?? -1} GraphRequestId: {requestId} ClientRequestId: {clientRequestId}");

            return response;
        }

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupTransitiveMembersNextPageAsync(string nextPageUrl, Guid groupId, Guid? runId)
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

                var requestConfig = new TransitiveMembersRequestBuilderGetRequestConfiguration
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

            _graphGroupMembershipReaderLogger.LogInformationWithRunId(runId, $"From subsequent page of transitive members for group {groupId}, member count {response.Response?.Value?.Count ?? -1} GraphRequestId: {requestId} ClientRequestId: {clientRequestId}");

            return response;
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
            if (httpResponse.Headers.TryGetValues("request-id", out var requestIds))
            {
                requestId = requestIds.FirstOrDefault();
            }

            return requestId;
        }
    }
}
