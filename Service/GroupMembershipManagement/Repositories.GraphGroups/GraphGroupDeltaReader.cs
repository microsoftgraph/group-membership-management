// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GraphAPIDeltaResponse = Microsoft.Graph.Groups.Delta.DeltaResponse;
using Group = Microsoft.Graph.Models.Group;

namespace Repositories.GraphGroups
{
    internal class GraphGroupDeltaReader : GraphGroupRepositoryBase
    {
        private readonly ILogger<GraphGroupDeltaReader> _graphGroupDeltaReaderLogger;

        public GraphGroupDeltaReader(GraphServiceClient graphServiceClient,
                                     GraphGroupMetricTracker graphGroupMetricTracker,
                                     ILogger<GraphGroupDeltaReader> graphGroupDeltaReaderLogger)
                                     : base(graphServiceClient, graphGroupDeltaReaderLogger, graphGroupMetricTracker)
        {
            _graphGroupDeltaReaderLogger = graphGroupDeltaReaderLogger ?? throw new ArgumentNullException(nameof(graphGroupDeltaReaderLogger));
        }

        public async Task<(List<AzureADUser> users, MemberReadCounts counts, string nextPageUrl, string deltaUrl)> GetFirstDeltaUsersPageAsync(Guid groupId, Guid? runId, int numberOfPages)
        {
            var allUsers = new List<AzureADUser>();
            var counts = new MemberReadCounts();
            string nextLink = null;
            string deltaLink = null;

            for (int i = 0; i < numberOfPages; i++)
            {
                var deltaResponse = string.IsNullOrEmpty(nextLink)
                    ? await GetGroupUsersPageByIdAsync(groupId.ToString())
                    : await GetGroupUsersNextPageAsync(nextLink);

                await _graphGroupMetricTracker.TrackMetricsAsync(deltaResponse.Headers, QueryType.Delta, runId);
                await _graphGroupMetricTracker.TrackRequestAsync(deltaResponse.Headers, groupId, QueryType.Delta, runId);

                var (users, pageCounts) = ExtractDeltaMembers(deltaResponse.Response.Value.FirstOrDefault());

                _graphGroupDeltaReaderLogger.LogInformationWithRunId(runId, $"Number of users from first page using delta - {users.Count}");

                allUsers.AddRange(users);
                AddCounts(counts, pageCounts);
                nextLink = deltaResponse.Response.OdataNextLink;
                deltaLink = deltaResponse.Response.OdataDeltaLink;

                if (string.IsNullOrEmpty(nextLink))
                {
                    break;
                }
            }

            return (allUsers, counts, nextLink, deltaLink);
        }

        public async Task<(List<AzureADUser> users, MemberReadCounts counts, string nextPageUrl, string deltaUrl)> GetNextDeltaUsersPagesAsync(Guid groupId, string nextPageUrl, Guid? runId, int numberOfPages)
        {
            var allUsers = new List<AzureADUser>();
            var counts = new MemberReadCounts();

            var nextLink = nextPageUrl;
            string deltaLink = null;

            for (int i = 0; i < numberOfPages && deltaLink == null; i++) {

                var deltaResponse = await GetGroupUsersNextPageAsync(nextLink);

                await _graphGroupMetricTracker.TrackMetricsAsync(deltaResponse.Headers, QueryType.Delta, runId);
                await _graphGroupMetricTracker.TrackRequestAsync(deltaResponse.Headers, groupId, QueryType.Delta, runId);

                var (users, pageCounts) = ExtractDeltaMembers(deltaResponse.Response.Value.FirstOrDefault());

                _graphGroupDeltaReaderLogger.LogInformationWithRunId(runId, $"Number of users from next page using delta - {users.Count}");

                allUsers.AddRange(users);
                AddCounts(counts, pageCounts);

                nextLink = deltaResponse.Response.OdataNextLink;
                deltaLink = deltaResponse.Response.OdataDeltaLink;
            }

            return (allUsers, counts, nextLink, deltaLink);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, MemberReadCounts counts, string nextPageUrl, string deltaUrl)>
            GetFirstDeltaLinkUsersPageAsync(Guid groupId, string deltaLink, Guid? runId, int numberOfPages)
        {
            var usersToAdd = new List<AzureADUser>();
            var usersToRemove = new List<AzureADUser>();
            var counts = new MemberReadCounts();
            string nextLink = deltaLink;
            string deltaUrl = null;

            for (int i = 0; i < numberOfPages; i++)
            {
                var deltaLinkResponse = await GetGroupUsersNextPageAsync(nextLink);

                await _graphGroupMetricTracker.TrackMetricsAsync(deltaLinkResponse.Headers, QueryType.DeltaLink, runId);
                await _graphGroupMetricTracker.TrackRequestAsync(deltaLinkResponse.Headers, groupId, QueryType.DeltaLink, runId);

                var (users, pageCounts) = ExtractDeltaMembers(
                    deltaLinkResponse.Response.Value.FirstOrDefault(),
                    includeMembersToRemove: true);

                usersToAdd.AddRange(users.Where(x => x.MembershipAction == MembershipAction.Add));
                usersToRemove.AddRange(users.Where(x => x.MembershipAction == MembershipAction.Remove));
                AddCounts(counts, pageCounts);

                nextLink = deltaLinkResponse.Response.OdataNextLink;
                deltaUrl = deltaLinkResponse.Response.OdataDeltaLink;

                if (string.IsNullOrEmpty(nextLink))
                {
                    break;
                }
            }

            return (usersToAdd, usersToRemove, counts, nextLink, deltaUrl);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, MemberReadCounts counts, string nextPageUrl, string deltaUrl)>
            GetNextDeltaLinkUsersPagesAsync(Guid groupId, string nextPageUrl, Guid? runId, int numberOfPages)
        {
            var usersToAdd = new List<AzureADUser>();
            var usersToRemove = new List<AzureADUser>();
            var counts = new MemberReadCounts();

            var nextLink = nextPageUrl;
            string deltaLink = null;

            for (int i = 0; i < numberOfPages && deltaLink == null; i++)
            {
                var deltaLinkResponse = await GetGroupUsersNextPageAsync(nextLink);

                await _graphGroupMetricTracker.TrackMetricsAsync(deltaLinkResponse.Headers, QueryType.DeltaLink, runId);
                await _graphGroupMetricTracker.TrackRequestAsync(deltaLinkResponse.Headers, groupId, QueryType.DeltaLink, runId);

                var (users, pageCounts) = ExtractDeltaMembers(
                    deltaLinkResponse.Response.Value.FirstOrDefault(),
                    includeMembersToRemove: true);

                _graphGroupDeltaReaderLogger.LogInformationWithRunId(runId, $"Number of users from next page using deltaLink - {users.Count}");

                usersToAdd.AddRange(users.Where(x => x.MembershipAction == MembershipAction.Add).ToList());
                usersToRemove.AddRange(users.Where(x => x.MembershipAction == MembershipAction.Remove).ToList());
                AddCounts(counts, pageCounts);

                nextLink = deltaLinkResponse.Response.OdataNextLink;
                deltaLink = deltaLinkResponse.Response.OdataDeltaLink;
            }

            return (usersToAdd, usersToRemove, counts, nextLink, deltaLink);
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

        private (List<AzureADUser> users, MemberReadCounts counts) ExtractDeltaMembers(
            Group group,
            bool includeMembersToRemove = false)
        {
            var users = new List<AzureADUser>();
            var counts = new MemberReadCounts();

            if (group != null && group.AdditionalData.TryGetValue("members@delta", out object membersJson))
            {
                var members = (membersJson as UntypedArray)?.GetValue();
                if (members == null) return (users, counts);

                foreach (UntypedObject memberObject in members)
                {
                    var member = memberObject.GetValue();
                    if (member == null) continue;

                    member.TryGetValue("@odata.type", out var rawMemberType);
                    member.TryGetValue("id", out var rawMemberId);

                    var memberType = (rawMemberType as UntypedString)?.GetValue();
                    var memberId = (rawMemberId as UntypedString)?.GetValue();
                    var isUserFamily = string.Equals(
                                           memberType,
                                           "#microsoft.graph.user",
                                           StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(
                                           memberType,
                                           "#microsoft.graph.agentUser",
                                           StringComparison.OrdinalIgnoreCase);

                    if (member.ContainsKey("@removed"))
                    {
                        // Graph can omit the type or report directoryObject on a removal. Removing an
                        // identifier that is not in the user-family cache is a no-op, so the type is not checked.
                        if (includeMembersToRemove && Guid.TryParse(memberId, out var removedMemberId))
                        {
                            users.Add(new AzureADUser
                            {
                                ObjectId = removedMemberId,
                                MembershipAction = MembershipAction.Remove,
                            });
                        }

                        continue;
                    }

                    if (isUserFamily && Guid.TryParse(memberId, out var addedMemberId))
                    {
                        users.Add(new AzureADUser
                        {
                            ObjectId = addedMemberId,
                            MembershipAction = MembershipAction.Add,
                        });
                        counts.MemberCount++;
                    }
                    else
                    {
                        IncrementSkippedCount(counts, memberType);
                    }
                }
            }

            return (users, counts);
        }

        private static void AddCounts(MemberReadCounts destination, MemberReadCounts source)
        {
            destination.MemberCount += source.MemberCount;
            destination.ServicePrincipalCount += source.ServicePrincipalCount;
            destination.DeviceCount += source.DeviceCount;
            destination.ContactCount += source.ContactCount;
            destination.GroupCount += source.GroupCount;
            destination.UnknownTypeCount += source.UnknownTypeCount;
            destination.OtherCount += source.OtherCount;
        }
    }
}
