// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Models;
using Repositories.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static Microsoft.Graph.Chats.Item.Members.MembersRequestBuilder;
using static Microsoft.Graph.Groups.Item.TransitiveMembers.TransitiveMembersRequestBuilder;

namespace Repositories.GraphGroups
{
    internal class GraphGroupMembershipReader : GraphGroupRepositoryBase
    {
        public GraphGroupMembershipReader(GraphServiceClient graphServiceClient,
                                          ILoggingRepository loggingRepository,
                                          GraphGroupMetricTracker graphGroupMetricTracker)
                                          : base(graphServiceClient, loggingRepository, graphGroupMetricTracker)
        { }

        public async Task<List<AzureADUser>> GetUsersInGroupTransitivelyAsync(Guid groupId, Guid? runId)
        {
            var nonUserGraphObjects = new Dictionary<string, int>();

            try
            {
                var membersResponse = await _graphServiceClient
                                                   .Groups[groupId.ToString()]
                                                   .TransitiveMembers
                                                   .GetAsync(requestConfiguration =>
                                                   {
                                                       requestConfiguration.QueryParameters.Top = 999;
                                                       requestConfiguration.QueryParameters.Select = new[] { "id" };
                                                   });

                await _graphGroupMetricTracker.TrackMetricsAsync(membersResponse.AdditionalData, QueryType.Other, runId);

                var members = new List<AzureADUser>(ToUsers(membersResponse.Value, nonUserGraphObjects));
                var nextPageLink = membersResponse.OdataNextLink;

                while (nextPageLink != null)
                {
                    var nextPageRequest = new RequestInformation
                    {
                        HttpMethod = Method.GET,
                        UrlTemplate = nextPageLink
                    };

                    var nextPageResult = await _graphServiceClient
                                                    .RequestAdapter
                                                    .SendAsync(nextPageRequest,
                                                               DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    members.AddRange(ToUsers(nextPageResult.Value, nonUserGraphObjects));

                    nextPageLink = nextPageResult.OdataNextLink;

                    await _graphGroupMetricTracker.TrackMetricsAsync(nextPageResult.AdditionalData, QueryType.Other, runId);
                }

                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, nonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = runId, Message = $"From group {groupId}, read {members.Count} users, and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                return members;
            }
            catch (ODataError ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        public async Task<int> GetGroupsCountAsync(Guid groupId, Guid? runId)
        {
            var request = _graphServiceClient
                            .Groups[groupId.ToString()]
                            .TransitiveMembers
                            .GraphGroup
                            .Count
                            .ToGetRequestInformation(requestConfiguration =>
                            {
                                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                            });

            return await GetGroupDirectoryObjectMembersCount(request, runId);
        }

        public async Task<int> GetUsersCountAsync(Guid groupId, Guid? runId)
        {
            var request = _graphServiceClient
                .Groups[groupId.ToString()]
                .TransitiveMembers
                .GraphUser
                .Count
                .ToGetRequestInformation(requestConfiguration =>
                {
                    requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                });

            return await GetGroupDirectoryObjectMembersCount(request, runId);
        }

        public async Task<(List<AzureADUser> users,
                   Dictionary<string, int> nonUserGraphObjects,
                   string nextPageUrl)> GetFirstTransitiveMembersPageAsync(Guid groupId, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            var usersResponse = await GetGroupTransitiveMembersPageByIdAsync(groupId.ToString());

            await _graphGroupMetricTracker.TrackMetricsAsync2(usersResponse.Headers, QueryType.Transitive, runId);
            await _graphGroupMetricTracker.TrackRequestAsync2(usersResponse.Headers, runId);

            users.AddRange(ToUsers(usersResponse.Response.Value, nonUserGraphObjects));
            return (users, nonUserGraphObjects, usersResponse.Response.OdataNextLink);
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl)> GetNextTransitiveMembersPageAsync(string nextPageUrl, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            var usersResponse = await GetGroupTransitiveMembersNextPageAsync(nextPageUrl);

            await _graphGroupMetricTracker.TrackMetricsAsync2(usersResponse.Headers, QueryType.Transitive, runId);
            await _graphGroupMetricTracker.TrackRequestAsync2(usersResponse.Headers, runId);

            users.AddRange(ToUsers(usersResponse.Response.Value, nonUserGraphObjects));
            return (users, nonUserGraphObjects, usersResponse.Response.OdataNextLink);
        }

        public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid groupId, Guid? runId)
        {
            try
            {
                var members = new List<IAzureADObject>();
                var membersResponse = await GetGroupMembersPageByIdAsync(groupId.ToString());
                members.AddRange(ToEntities(membersResponse.Response.Value));

                await _graphGroupMetricTracker.TrackMetricsAsync2(membersResponse.Headers, QueryType.Other, runId);

                while (membersResponse.Response.OdataNextLink != null)
                {
                    membersResponse = await GetGroupMembersNextPageAsync(membersResponse.Response.OdataNextLink);
                    members.AddRange(ToEntities(membersResponse.Response.Value));
                    await _graphGroupMetricTracker.TrackMetricsAsync2(membersResponse.Headers, QueryType.Other, runId);
                }

                return members;
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = "Unable to retrieve group members.\n" + ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        private async Task<int> GetGroupDirectoryObjectMembersCount(RequestInformation request, Guid? runId)
        {
            var resourceUnitsUsed = _graphGroupMetricTracker.GetMetric(nameof(Metric.ResourceUnitsUsed));
            var throttleLimitPercentage = _graphGroupMetricTracker.GetMetric(nameof(Metric.ThrottleLimitPercentage));

            var nativeResponseHandler = new NativeResponseHandler();
            var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

            // When using the native response handler it sets the return value to null.
            // So we need to extract the response from the native response handler.
            var options = request.RequestOptions.ToList();
            options.Add(responseHandlerOption);

            request.AddRequestOptions(options);
            await _graphServiceClient.RequestAdapter.SendPrimitiveAsync<int?>(request);

            var nativeHttpResponse = nativeResponseHandler.Value as HttpResponseMessage;

            if (nativeHttpResponse.Headers.TryGetValues(GraphResponseHeader.ResourceUnitHeader, out var resourceValues))
            {
                int ruu = GraphGroupMetricTracker.ParseFirst<int>(resourceValues, int.TryParse);
                await _loggingRepository.LogMessageAsync(
                    new LogMessage { Message = $"Resource unit cost of {Enum.GetName(typeof(QueryType), QueryType.Other)} - {ruu}", RunId = runId });
                _graphGroupMetricTracker.TrackResourceUnitsUsedByTypeEvent(ruu, QueryType.Other, runId);
                resourceUnitsUsed.TrackValue(ruu);
            }

            if (nativeHttpResponse.Headers.TryGetValues(GraphResponseHeader.ThrottlePercentageHeader, out var throttleValues))
                throttleLimitPercentage.TrackValue(GraphGroupMetricTracker.ParseFirst<double>(throttleValues, double.TryParse));

            var responseContent = await nativeHttpResponse.Content.ReadAsStringAsync();
            return int.Parse(responseContent);
        }

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupTransitiveMembersPageByIdAsync(string groupId)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<DirectoryObjectCollectionResponse>();

            await retryPolicy.ExecuteAsync(async () =>
            {
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

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                       DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            return response;
        }

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupTransitiveMembersNextPageAsync(string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<DirectoryObjectCollectionResponse>();

            await retryPolicy.ExecuteAsync(async () =>
            {
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

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                           DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            return response;
        }

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupMembersPageByIdAsync(string groupId)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<DirectoryObjectCollectionResponse>();

            await retryPolicy.ExecuteAsync(async () =>
            {
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

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                       DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            return response;
        }

        private async Task<GraphObjectResponse<DirectoryObjectCollectionResponse>> GetGroupMembersNextPageAsync(string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<DirectoryObjectCollectionResponse>();

            await retryPolicy.ExecuteAsync(async () =>
            {
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

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                           DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            return response;
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
    }
}
