// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Models;
using Models.Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphGroupInformationRepository : GraphGroupRepositoryBase
    {
        public GraphGroupInformationRepository(GraphServiceClient graphServiceClient,
                                  ILoggingRepository loggingRepository,
                                  GraphGroupMetricTracker graphGroupMetricTracker)
                                  : base(graphServiceClient, loggingRepository, graphGroupMetricTracker)
        { }

        private int MaxGroupResultCount { get; set; } = 25;

        public async Task<bool> GroupExistsAsync(Guid groupId, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };
                var retryHandlerOption = new RetryHandlerOption { MaxRetry = 4, Delay = 20 };

                bool? groupExists = null;

                await _graphServiceClient.Groups[groupId.ToString()].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Options.Add(retryHandlerOption);
                    requestConfiguration.Options.Add(responseHandlerOption);
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                if (nativeResponse.IsSuccessStatusCode)
                {
                    var group = await DeserializeResponseAsync(nativeResponse, Group.CreateFromDiscriminatorValue);
                    groupExists = group?.Id != null;
                }
                else if (nativeResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    groupExists = false;
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                if (!groupExists.HasValue)
                {
                    throw new Exception($"Unable to determine if group {groupId} exists. Status code: {nativeResponse.StatusCode}");
                }

                return groupExists.Value;
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        public async Task<bool> GroupExistsAsync(string groupName, Guid? runId)
        {
            try
            {
                var groupCollectionPage = await _graphServiceClient.Groups
                                                   .GetAsync(requestConfiguration =>
                                                   {
                                                       requestConfiguration
                                                        .QueryParameters
                                                        .Filter = $"startswith(displayName, '{groupName}')";
                                                   });

                return groupCollectionPage.Value?.Any() ?? false;
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

        public async Task<AzureADGroup> GetGroupAsync(string groupName, Guid? runId)
        {
            try
            {
                var groupCollectionPage = await _graphServiceClient.Groups
                                                                   .GetAsync(requestConfiguration =>
                                                                   {
                                                                       requestConfiguration
                                                                        .QueryParameters
                                                                        .Filter = $"startswith(displayName, '{groupName}')";
                                                                   });

                var group = groupCollectionPage.Value?.FirstOrDefault();
                if (group == null)
                    return null;

                return new AzureADGroup { ObjectId = new Guid(group.Id) };
            }
            catch (ODataError ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                return null;
            }
        }

        public async Task<string> GetGroupNameAsync(Guid groupId, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };
                Group group = null;

                await _graphServiceClient.Groups[groupId.ToString()].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Options.Add(responseHandlerOption);
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                if (nativeResponse.IsSuccessStatusCode)
                {
                    group = await DeserializeResponseAsync(nativeResponse, Group.CreateFromDiscriminatorValue);
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                return group != null ? group.DisplayName : string.Empty;
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return string.Empty;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        public async Task<Dictionary<Guid, string>> GetGroupNamesAsync(List<Guid> groupIds)
        {
            var groupNames = new Dictionary<Guid, string>();
            var batchRequest = new BatchRequestContentCollection(_graphServiceClient);

            // requestId, groupId
            var requestIdTracker = new Dictionary<string, Guid>();

            foreach (var groupId in groupIds.Distinct())
            {
                var requestInformation = _graphServiceClient
                                            .Groups[groupId.ToString()]
                                            .ToGetRequestInformation(requestConfiguration =>
                                            {
                                                requestConfiguration.QueryParameters.Select = new[] { "displayName" };
                                            });



                var requestId = await batchRequest.AddBatchRequestStepAsync(requestInformation);
                requestIdTracker.Add(requestId, groupId);
            }

            var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);

            foreach (var statusCodeResponse in await batchResponse.GetResponsesStatusCodesAsync())
            {
                using var response = await batchResponse.GetResponseByIdAsync(statusCodeResponse.Key);
                if (response.IsSuccessStatusCode)
                {
                    var responseHandler = new ResponseHandler<Group>();
                    var group = await responseHandler.HandleResponseAsync<HttpResponseMessage, Group>(response, null);
                    if (group != null)
                        groupNames.Add(requestIdTracker[statusCodeResponse.Key], group.DisplayName);
                }
                else
                {
                    groupNames.Add(requestIdTracker[statusCodeResponse.Key], null);
                }
            }

            return groupNames;
        }

        public async Task<Dictionary<string, string>> GetTeamsChannelsNamesAsync(List<AzureADTeamsChannel> channels)
        {
            var channelNames = new Dictionary<string, string>();
            var batchRequest = new BatchRequestContentCollection(_graphServiceClient);

            // requestId, groupId
            var requestIdTracker = new Dictionary<string, string>();

            foreach (var channel in channels.Distinct())
            {
                var requestInformation = _graphServiceClient
                                            .Teams[channel.ObjectId.ToString()]
                                            .Channels[channel.ChannelId.ToString()]
                                            .ToGetRequestInformation(requestConfiguration =>
                                            {
                                                requestConfiguration.QueryParameters.Select = new[] { "displayName" };
                                            });



                var requestId = await batchRequest.AddBatchRequestStepAsync(requestInformation);
                requestIdTracker.Add(requestId, channel.ChannelId);
            }

            var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);

            foreach (var statusCodeResponse in await batchResponse.GetResponsesStatusCodesAsync())
            {
                using var response = await batchResponse.GetResponseByIdAsync(statusCodeResponse.Key);
                if (response.IsSuccessStatusCode)
                {
                    var responseHandler = new ResponseHandler<Channel>();
                    var channel = await responseHandler.HandleResponseAsync<HttpResponseMessage, Channel>(response, null);
                    if (channel != null)
                        channelNames.Add(requestIdTracker[statusCodeResponse.Key], channel.DisplayName);
                }
                else
                {
                    channelNames.Add(requestIdTracker[statusCodeResponse.Key], null);
                }
            }

            return channelNames;
        }
        public async Task<Dictionary<Guid, List<Guid>>> GetGroupOwnersAsync(List<Guid> groupIds)
        {
            var groupOwners = new Dictionary<Guid, List<Guid>>();
            var batchRequest = new BatchRequestContentCollection(_graphServiceClient);

            // requestId, groupId
            var requestIdTracker = new Dictionary<string, Guid>();

            foreach (var groupId in groupIds.Distinct())
            {
                var requestInformation = _graphServiceClient
                                            .Groups[groupId.ToString()]
                                            .Owners
                                            .ToGetRequestInformation(requestConfiguration =>
                                            {
                                                requestConfiguration.QueryParameters.Select = new[] { "id" };
                                                requestConfiguration.QueryParameters.Top = 200;
                                            });



                var requestId = await batchRequest.AddBatchRequestStepAsync(requestInformation);
                requestIdTracker.Add(requestId, groupId);
            }

            var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);

            foreach (var statusCodeResponse in await batchResponse.GetResponsesStatusCodesAsync())
            {
                using var response = await batchResponse.GetResponseByIdAsync(statusCodeResponse.Key);
                if (response.IsSuccessStatusCode)
                {
                    var responseHandler = new ResponseHandler<DirectoryObjectCollectionResponse>();
                    var directoryObjectCollectionResponse = await responseHandler.HandleResponseAsync<HttpResponseMessage, DirectoryObjectCollectionResponse>(response, null);
                    if (directoryObjectCollectionResponse != null)
                        groupOwners.Add(requestIdTracker[statusCodeResponse.Key], directoryObjectCollectionResponse.Value.Select(o => Guid.Parse(o.Id)).ToList());
                }
                else
                {
                    groupOwners.Add(requestIdTracker[statusCodeResponse.Key], null);
                }
            }

            return groupOwners;
        }

        public async Task<List<string>> GetGroupEndpointsAsync(Guid groupId, Guid? runId)
        {
            var endpoints = new List<string>();

            try
            {
                var batchRequest = new BatchRequestContentCollection(_graphServiceClient);
                var outlookRequestInformation = _graphServiceClient
                                                    .Groups[groupId.ToString()]
                                                    .ToGetRequestInformation(requestConfiguration =>
                                                    {
                                                        requestConfiguration.QueryParameters.Select = new[] { "mailEnabled", "groupTypes", "securityEnabled" };
                                                    });

                var outlookRequestId = await batchRequest.AddBatchRequestStepAsync(outlookRequestInformation);

                var sharepointRequestInformation = _graphServiceClient
                                    .Groups[groupId.ToString()].Sites["root"]
                                    .ToGetRequestInformation();

                var sharepointRequestId = await batchRequest.AddBatchRequestStepAsync(sharepointRequestInformation);

                var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);

                var group = await batchResponse.GetResponseByIdAsync<Group>(outlookRequestId);
                if (group != null)
                {
                    var isMailEnabled = group.MailEnabled ?? false;
                    var groupTypes = group.GroupTypes ?? new List<string>();
                    var isSecurityEnabled = group.SecurityEnabled ?? false;

                    if (isMailEnabled && groupTypes.Contains("Unified"))
                        endpoints.Add("Outlook");
                    else if (isSecurityEnabled && !groupTypes.Any())
                        endpoints.Add("SecurityGroup");
                }

                var siteResponse = await batchResponse.GetResponseByIdAsync(sharepointRequestId);
                if (siteResponse.IsSuccessStatusCode)
                {
                    endpoints.Add("SharePoint");
                }
            }
            catch (ApiException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });
            }

            try
            {
                var baseUrl = "https://graph.microsoft.com/beta";
                var endpointsUrl = $"{baseUrl}/groups/{groupId}/endpoints";
                var getRequestInformation = _graphServiceClient.Groups.ToGetRequestInformation();
                getRequestInformation.URI = new Uri(endpointsUrl);
                getRequestInformation.PathParameters["baseurl"] = baseUrl;

                var endpointCollectionResponse = await _graphServiceClient
                                                            .RequestAdapter
                                                            .SendAsync(getRequestInformation,
                                                            factory: EndpointCollectionResponse.CreateFromDiscriminatorValue);

                if (endpointCollectionResponse.Value?.Any() ?? false)
                    endpoints.AddRange(endpointCollectionResponse.Value.Select(x => x.ProviderName));

            }
            catch (ODataError ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });
            }

            return endpoints;
        }

        public async Task CreateGroupAsync(string newGroupName, TestGroupType testGroupType, List<Guid> groupOwnerIds, Guid? runId)
        {
            try
            {
                if (await GroupExistsAsync(newGroupName, runId))
                {
                    return;
                }

                var groupDefinition = new Group
                {
                    DisplayName = newGroupName,
                    MailEnabled = false,
                    SecurityEnabled = true
                };

                if (testGroupType == TestGroupType.IntegrationTesting)
                {
                    groupDefinition.Description = $"Integration test group: {newGroupName}";
                    groupDefinition.MailNickname = new Guid().ToString();
                }
                else
                {
                    groupDefinition.Description = $"Load test group: {newGroupName}";
                    groupDefinition.GroupTypes = new List<string> { "Unified" };
                    groupDefinition.MailNickname = Guid.NewGuid().ToString();
                }

                if (groupOwnerIds != null && groupOwnerIds.Count > 1)
                {
                    groupOwnerIds.ForEach((ownerId) =>
                    {
                        groupDefinition.Owners.Add(new DirectoryObject
                        {
                            Id = $"{ownerId}"
                        });
                    });
                }

                var group = await _graphServiceClient.Groups.PostAsync(groupDefinition);
            }
            catch (Exception e)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Error creating group: {e}" });
            }
        }

        public async Task<List<AzureADGroup>> GetGroupsAsync(List<Guid> groupIds, Guid? runId)
        {
            var groups = new List<AzureADGroup>();

            try
            {
                foreach (var groupIdsChunk in groupIds.Distinct().Chunk(20))
                {
                    var batchRequest = new BatchRequestContentCollection(_graphServiceClient);
                    var requestIds = new Dictionary<string, Guid>();

                    foreach (var groupId in groupIdsChunk)
                    {
                        var getRequestInformation = _graphServiceClient.Groups[groupId.ToString()].ToGetRequestInformation(requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Select = new[] { "id", "mailEnabled", "groupTypes", "securityEnabled", "displayName" };
                        });

                        var requestId = await batchRequest.AddBatchRequestStepAsync(getRequestInformation);
                        requestIds.Add(requestId, groupId);
                    }

                    var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);

                    foreach (var requestId in requestIds.Keys)
                    {
                        var group = new AzureADGroup
                        {
                            ObjectId = requestIds[requestId],
                            Type = "Unknown",
                        };

                        var graphGroupResonse = await batchResponse.GetResponseByIdAsync(requestId);

                        if (graphGroupResonse.IsSuccessStatusCode)
                        {
                            var graphGroup = await DeserializeResponseAsync(graphGroupResonse, Group.CreateFromDiscriminatorValue);
                            var isMailEnabled = graphGroup.MailEnabled ?? false;
                            var groupTypes = graphGroup.GroupTypes ?? new List<string>();
                            var isSecurityEnabled = graphGroup.SecurityEnabled ?? false;

                            //table defining group types can be found here
                            // https://learn.microsoft.com/en-us/graph/api/resources/groups-overview
                            // ?view=graph-rest-1.0&tabs=http#group-types-in-azure-ad-and-microsoft-graph
                            if (groupTypes.Contains("Unified") && isMailEnabled)
                                group.Type = "Microsoft 365";
                            else if (!groupTypes.Any() && !isMailEnabled && isSecurityEnabled)
                                group.Type = "Security";
                            else if (!groupTypes.Any() && isMailEnabled && isSecurityEnabled)
                                group.Type = "Mail enabled security";
                            else if (!groupTypes.Any() && isMailEnabled && !isSecurityEnabled)
                                group.Type = "Distribution";

                            group.Name = graphGroup.DisplayName;
                        }

                        groups.Add(group);
                    }

                }
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Unable to retrieve group types\n{ex.GetBaseException()}",
                    RunId = runId
                });
            }

            return groups;
        }

        public async Task<List<AzureADGroup>> SearchGroupsAsync(string filter)
        {
            try
            {
                var results = new List<AzureADGroup>();
                var groupCollectionPage = await _graphServiceClient.Groups
                                   .GetAsync(requestConfiguration =>
                                   {
                                       requestConfiguration
                                        .QueryParameters
                                        .Filter = filter;
                                       requestConfiguration
                                        .QueryParameters
                                        .Top = MaxGroupResultCount;
                                   });

                if (groupCollectionPage.Value.Count > 0)
                {
                    foreach (var group in groupCollectionPage.Value)
                    {
                        var azureAdGroup = new AzureADGroup
                        {
                            ObjectId = new Guid(group.Id),
                            Name = group.DisplayName,
                        };

                        results.Add(azureAdGroup);
                    }
                }

                return results;

            }
            catch (Exception e)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Error searching for groups: {e}" });
                throw;
            }
        }
    }
}
