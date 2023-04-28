// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GraphAPIDeltaResponse = Microsoft.Graph.Groups.Delta.DeltaResponse;

namespace Repositories.GraphGroups
{
    internal class GraphGroupDeltaReader : GraphGroupRepositoryBase
    {

        public GraphGroupDeltaReader(GraphServiceClient graphServiceClient,
                                    ILoggingRepository loggingRepository,
                                    GraphGroupMetricTracker graphGroupMetricTracker)
                                    : base(graphServiceClient, loggingRepository, graphGroupMetricTracker)
        { }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetFirstUsersPageAsync(Guid groupId, Guid? runId)
        {
            var deltaResponse = await GetGroupUsersPageByIdAsync(groupId.ToString());

            await _graphGroupMetricTracker.TrackMetricsAsync2(deltaResponse.Headers, QueryType.Delta, runId);
            await _graphGroupMetricTracker.TrackRequestAsync2(deltaResponse.Headers, runId);

            var users = ExtractDeltaMembers(deltaResponse.Response.Value.FirstOrDefault());

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Number of users from first page using delta - {users.Count}",
                RunId = runId
            });

            return (users, deltaResponse.Response.OdataNextLink, deltaResponse.Response.OdataDeltaLink);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl)> GetNextUsersPageAsync(string nextPageUrl, Guid? runId)
        {
            var deltaResponse = await GetGroupUsersNextPageAsync(nextPageUrl);

            await _graphGroupMetricTracker.TrackMetricsAsync2(deltaResponse.Headers, QueryType.Delta, runId);
            await _graphGroupMetricTracker.TrackRequestAsync2(deltaResponse.Headers, runId);

            var users = ExtractDeltaMembers(deltaResponse.Response.Value.FirstOrDefault());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Number of users from next page using delta - {users.Count}", RunId = runId });

            return (users, deltaResponse.Response.OdataNextLink, deltaResponse.Response.OdataDeltaLink);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl)>
            GetNextDeltaUsersPageAsync(string deltaLink, Guid? runId)
        {
            List<AzureADUser> usersToAdd;
            List<AzureADUser> usersToRemove;

            var deltaResponse = await GetGroupUsersNextPageAsync(deltaLink);

            await _graphGroupMetricTracker.TrackMetricsAsync2(deltaResponse.Headers, QueryType.DeltaLink, runId);
            await _graphGroupMetricTracker.TrackRequestAsync2(deltaResponse.Headers, runId);

            var users = ExtractDeltaMembers(deltaResponse.Response.Value.FirstOrDefault(), includeMembersToRemove: true);

            usersToAdd = users.Where(x => x.MembershipAction == MembershipAction.Add).ToList();
            usersToRemove = users.Where(x => x.MembershipAction == MembershipAction.Remove).ToList();

            return (usersToAdd, usersToRemove, deltaResponse.Response.OdataNextLink, deltaResponse.Response.OdataDeltaLink);
        }

        private async Task<GraphObjectResponse<GraphAPIDeltaResponse>> GetGroupUsersPageByIdAsync(string groupId)
        {
            var response = new GraphObjectResponse<GraphAPIDeltaResponse>();

            var retryPolicy = GetRetryPolicy();
            await retryPolicy.ExecuteAsync(async () =>
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

                await _graphServiceClient.Groups.Delta.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Options.Add(responseHandlerOption);
                    requestConfiguration.QueryParameters.Select = new[] { "members" };
                    requestConfiguration.QueryParameters.Filter = $"id  eq '{groupId}'";
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                           GraphAPIDeltaResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            return response;
        }

        private async Task<GraphObjectResponse<GraphAPIDeltaResponse>> GetGroupUsersNextPageAsync(string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<GraphAPIDeltaResponse>();

            await retryPolicy.ExecuteAsync(async () =>
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

                var requestInformation = new RequestInformation
                {
                    HttpMethod = Method.GET,
                    UrlTemplate = nextPageUrl,
                };

                var requestConfig = new Microsoft.Graph.Groups.Delta.DeltaRequestBuilder.DeltaRequestBuilderGetRequestConfiguration
                {
                    Options = new List<IRequestOption> { responseHandlerOption }
                };

                requestInformation.AddRequestOptions(requestConfig.Options);
                requestInformation.Headers.Add("Accept", "application/json");

                await _graphServiceClient
                        .RequestAdapter
                        .SendAsync(requestInformation,
                                   GraphAPIDeltaResponse.CreateFromDiscriminatorValue);

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                           GraphAPIDeltaResponse.CreateFromDiscriminatorValue);

                    response.Response = directoryObjectCollectionResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            return response;
        }

        private List<AzureADUser> ExtractDeltaMembers(Group group, bool includeMembersToRemove = false)
        {
            var users = new List<AzureADUser>();

            if (group != null && group.AdditionalData.TryGetValue("members@delta", out object membersJson))
            {
                var memberArray = JsonArray.Parse(membersJson.ToString()).AsArray();
                foreach (var member in memberArray)
                {
                    if (member["@odata.type"].ToString().Equals("#microsoft.graph.user", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (member["@removed"] == null)
                            users.Add(new AzureADUser { ObjectId = Guid.Parse((string)member["id"]), MembershipAction = MembershipAction.Add });
                        else if (includeMembersToRemove)
                            users.Add(new AzureADUser { ObjectId = Guid.Parse((string)member["id"]), MembershipAction = MembershipAction.Remove });
                    }
                }
            }

            return users;
        }
    }
}
