// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphUserReader : GraphGroupRepositoryBase
    {
        private readonly ILogger<GraphUserReader> _graphUserReaderLogger;

        public GraphUserReader(GraphServiceClient graphServiceClient,
                               GraphGroupMetricTracker graphGroupMetricTracker,
                               ILogger<GraphUserReader> graphUserReaderLogger)
                               : base(graphServiceClient, graphUserReaderLogger, graphGroupMetricTracker)
        {
            _graphUserReaderLogger = graphUserReaderLogger ?? throw new ArgumentNullException(nameof(graphUserReaderLogger));
        }

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

        public async Task<AzureADUser> GetUserByUpnOrIdAsync(string userIdentifier, Guid? runId, bool includeMailProperty = false)
        {
            AzureADUser userDetails = null;

            try
            {
                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Users[userIdentifier].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "userPrincipalName", "onPremisesImmutableId", "mail" };
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse != null)
                {
                    var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                    if (nativeResponse.IsSuccessStatusCode)
                    {
                        var user = await DeserializeResponseAsync(nativeResponse, User.CreateFromDiscriminatorValue);

                        if (user != null)
                        {
                            userDetails = new AzureADUser
                            {
                                ObjectId = Guid.Parse(user.Id),
                                DisplayName = user.DisplayName,
                                UserPrincipalName = user.UserPrincipalName,
                                OnPremisesImmutableId = user.OnPremisesImmutableId,
                                Mail = includeMailProperty ? user.Mail : null
                            };
                        }
                    }
                }
            }

            catch (Exception exception)
            {
                _graphUserReaderLogger.LogErrorWithRunId(runId, $"Exception: {exception}, FailedMethod: {nameof(GetUserByUpnOrIdAsync)}, UserIdentifier: {userIdentifier}", exception);
            }

            return userDetails;
        }

        public async Task<bool?> GetUserAccountEnabledAsync(string userIdentifier, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Users[userIdentifier].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new string[] { "id", "accountEnabled" };
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse != null)
                {
                    var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                    if (nativeResponse.IsSuccessStatusCode)
                    {
                        var user = await DeserializeResponseAsync(nativeResponse, User.CreateFromDiscriminatorValue);
                        return user?.AccountEnabled;
                    }
                }
            }
            catch (Exception exception)
            {
                _graphUserReaderLogger.LogErrorWithRunId(runId, $"Exception: {exception}, FailedMethod: {nameof(GetUserAccountEnabledAsync)}, UserIdentifier: {userIdentifier}", exception);
            }

            return null;
        }

        public async Task<AzureADUser> GetUserWithOnPremisesImmutableIdAsync(string userIdentifier, Guid? runId)
        {
            AzureADUser userDetails = null;

            try
            {
                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Users[userIdentifier]
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = new string[] { "id", "userPrincipalName", "onPremisesImmutableId" };
                        requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                    });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse != null)
                {
                    var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                    if (nativeResponse.IsSuccessStatusCode)
                    {
                        var user = await DeserializeResponseAsync(nativeResponse, User.CreateFromDiscriminatorValue);

                        if (user != null)
                        {
                            userDetails = new AzureADUser
                            {
                                ObjectId = Guid.Parse(user.Id),
                                UserPrincipalName = user.UserPrincipalName,
                                OnPremisesImmutableId = user.OnPremisesImmutableId
                            };
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                _graphUserReaderLogger.LogErrorWithRunId(runId, $"Exception: {exception}, FailedMethod: {nameof(GetUserWithOnPremisesImmutableIdAsync)}, UserIdentifier: {userIdentifier}", exception);
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

        public async Task<Guid> GetObjectIdFromServicePrincipalAsync(Guid appId, Guid? runId)
        {
            try
            {
                var servicePrincipals = await _graphServiceClient.ServicePrincipals
                    .GetAsync(requestConfig =>
                    {
                        requestConfig.QueryParameters.Filter = $"appId eq '{appId}'";
                    });

                var servicePrincipal = servicePrincipals?.Value?.FirstOrDefault();

                if (servicePrincipal != null)
                {
                    return Guid.Parse(servicePrincipal.Id);
                }

                return Guid.Empty;
            }
            catch (Exception ex)
            {
                _graphUserReaderLogger.LogErrorWithRunId(runId, $"Exception: {nameof(GetObjectIdFromServicePrincipalAsync)} failed with error {ex.Message}", ex);
                return Guid.Empty;
            }
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

        /// <summary>Reads the assistants published on a leader's directory profile.</summary>
        /// <remarks>Empty means no assistant; an unreadable profile throws so callers can fail closed.</remarks>
        public async Task<List<AzureADUser>> GetAssistantsAsync(Guid leaderObjectId, Guid? runId)
        {
            var baseUrl = "https://graph.microsoft.com/beta";
            var positionsUrl = $"{baseUrl}/users/{leaderObjectId}/profile/positions";

            var requestInformation = _graphServiceClient.Users.ToGetRequestInformation();
            requestInformation.URI = new Uri(positionsUrl);
            requestInformation.PathParameters["baseurl"] = baseUrl;

            var nativeResponseHandler = new NativeResponseHandler();
            requestInformation.AddRequestOptions(new IRequestOption[] { new ResponseHandlerOption { ResponseHandler = nativeResponseHandler } });

            await _graphServiceClient
                    .RequestAdapter
                    .SendAsync(requestInformation, UserCollectionResponse.CreateFromDiscriminatorValue);

            using var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

            if (nativeResponse == null)
                throw new InvalidOperationException("The profile positions request returned no response.");

            var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
            await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

            // Never downgraded to "no assistant" so the caller can fail closed.
            if (!nativeResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"The profile positions request failed with status {(int)nativeResponse.StatusCode}.");

            var content = await nativeResponse.Content.ReadAsStringAsync();
            return ParseAssistants(content);
        }

        // Shape: { "value": [ { ..., "colleagues": [ { "relationship": "assistant", "userId": "..." } ] } ] }
        // Only the object id is projected so no directory identifier can reach a log.
        private static List<AzureADUser> ParseAssistants(string content)
        {
            var assistants = new List<AzureADUser>();

            if (string.IsNullOrWhiteSpace(content))
                return assistants;

            using var document = JsonDocument.Parse(content);

            if (!document.RootElement.TryGetProperty("value", out var positions) || positions.ValueKind != JsonValueKind.Array)
                return assistants;

            foreach (var position in positions.EnumerateArray())
            {
                if (!position.TryGetProperty("colleagues", out var colleagues) || colleagues.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var colleague in colleagues.EnumerateArray())
                {
                    if (!colleague.TryGetProperty("relationship", out var relationship)
                        || !string.Equals(relationship.GetString(), "assistant", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (colleague.TryGetProperty("userId", out var userId)
                        && Guid.TryParse(userId.GetString(), out var assistantObjectId)
                        && !assistants.Any(a => a.ObjectId == assistantObjectId))
                    {
                        assistants.Add(new AzureADUser { ObjectId = assistantObjectId });
                    }
                }
            }

            return assistants;
        }
    }
}
