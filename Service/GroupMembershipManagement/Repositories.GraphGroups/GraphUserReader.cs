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
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphUserReader : GraphGroupRepositoryBase
    {
        public GraphUserReader(GraphServiceClient graphServiceClient,
                                          ILoggingRepository loggingRepository,
                                          GraphGroupMetricTracker graphGroupMetricTracker)
                                          : base(graphServiceClient, loggingRepository, graphGroupMetricTracker)
        { }

        public async Task<List<AzureADUser>> GetTenantUsersAsync(int userCount, Guid? runId)
        {
            var tenantUsers = new HashSet<AzureADUser>();

            var userResponse = await _graphServiceClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Top = userCount <= 999 ? userCount : 999;
            });

            var pageIterator = PageIterator<User, UserCollectionResponse>
                                .CreatePageIterator(
                                    _graphServiceClient,
                                    userResponse,
                                    (user) =>
                                    {
                                        tenantUsers.Add(new AzureADUser { ObjectId = new Guid(user.Id) });
                                        return tenantUsers.Count < userCount;
                                    }
                                );

            await pageIterator.IterateAsync();
            return tenantUsers.ToList();
        }

        public async Task<AzureADUser> GetUserByEmailAsync(string emailAddress, Guid? runId)
        {
            AzureADUser userDetails = null;

            try
            {
                var user = await _graphServiceClient.Users[emailAddress].GetAsync();
                if (user != null) userDetails = new AzureADUser { ObjectId = Guid.Parse(user.Id) };
            }

            catch (Exception exception)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Exception: {exception}, FailedMethod: {nameof(GetUserByEmailAsync)}, UserEmail: {emailAddress}"
                });
            }

            return userDetails;
        }

        public async Task<(List<AzureADUser> users,
                   Dictionary<string, int> nonUserGraphObjects,
                   string nextPageUrl)> GetFirstMembersPageAsync(string url, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            var usersResponse = await GetFirstMembersAsync(url);

            await _graphGroupMetricTracker.TrackMetricsAsync(usersResponse.Headers, QueryType.Other, runId);

            users.AddRange(ToUsers(usersResponse.Response.Value, nonUserGraphObjects));

            return (users, nonUserGraphObjects, usersResponse.Response.OdataNextLink);
        }

        public async Task<(List<AzureADUser> users,
                   Dictionary<string, int> nonUserGraphObjects,
                   string nextPageUrl)> GetNextMembersPageAsync(string nextPageUrl, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            var usersResponse = await GetNextMembersAsync(nextPageUrl);

            await _graphGroupMetricTracker.TrackMetricsAsync(usersResponse.Headers, QueryType.Other, runId);

            users.AddRange(ToUsers(usersResponse.Response.Value, nonUserGraphObjects));
            return (users, nonUserGraphObjects, usersResponse.Response.OdataNextLink);
        }

        private async Task<GraphObjectResponse<UserCollectionResponse>> GetFirstMembersAsync(string url)
        {
            var response = new GraphObjectResponse<UserCollectionResponse>();
            var nativeResponseHandler = new NativeResponseHandler();
            var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

            var requestInformation = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = url,
            };

            requestInformation.AddRequestOptions(new List<IRequestOption> { responseHandlerOption });
            requestInformation.Headers.Add("Accept", "application/json");
            requestInformation.Headers.Add("ConsistencyLevel", "eventual");

            await _graphServiceClient
                        .RequestAdapter
                        .SendAsync(requestInformation,
                                   UserCollectionResponse.CreateFromDiscriminatorValue);

            var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

            if (nativeResponse.IsSuccessStatusCode)
            {
                var x = await nativeResponse.Content.ReadAsStringAsync();
                var directoryObjectCollectionResponse = await DeserializeResponseAsync(nativeResponse,
                                                                                       UserCollectionResponse.CreateFromDiscriminatorValue);

                response.Response = directoryObjectCollectionResponse;
                response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
            }

            return response;
        }

        private async Task<GraphObjectResponse<UserCollectionResponse>> GetNextMembersAsync(string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            var response = new GraphObjectResponse<UserCollectionResponse>();

            await retryPolicy.ExecuteAsync(async () =>
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

                var requestInformation = new RequestInformation
                {
                    HttpMethod = Method.GET,
                    UrlTemplate = nextPageUrl,
                };

                requestInformation.AddRequestOptions(new List<IRequestOption> { responseHandlerOption });

                await _graphServiceClient
                       .RequestAdapter
                       .SendAsync(requestInformation,
                                   UserCollectionResponse.CreateFromDiscriminatorValue);

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var usersResponse = await DeserializeResponseAsync(nativeResponse,
                                                                       UserCollectionResponse.CreateFromDiscriminatorValue);

                    response.Response = usersResponse;
                    response.Headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                }

                return nativeResponse;
            });

            return response;
        }
    }
}
