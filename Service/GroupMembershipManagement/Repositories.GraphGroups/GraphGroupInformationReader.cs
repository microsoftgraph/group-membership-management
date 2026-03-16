// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Models;
using Models.Entities;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Group = Microsoft.Graph.Models.Group;

namespace Repositories.GraphGroups
{
    internal class GraphGroupInformationRepository : GraphGroupRepositoryBase
    {
        private readonly ILogger<GraphGroupInformationRepository> _graphGroupInformationRepositoryLogger;

        public GraphGroupInformationRepository(GraphServiceClient graphServiceClient,
                                              GraphGroupMetricTracker graphGroupMetricTracker,
                                              ILogger<GraphGroupInformationRepository> graphGroupInformationRepositoryLogger)
                                              : base(graphServiceClient, graphGroupInformationRepositoryLogger, graphGroupMetricTracker)
        {
            _graphGroupInformationRepositoryLogger = graphGroupInformationRepositoryLogger ?? throw new ArgumentNullException(nameof(graphGroupInformationRepositoryLogger));
        }

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

                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
            catch (Exception ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
        }

        public async Task<bool> IsGroupSyncedOnPremisesAsync(Guid groupId, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };
                var retryHandlerOption = new RetryHandlerOption { MaxRetry = 4, Delay = 20 };

                await _graphServiceClient.Groups[groupId.ToString()].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[] { "onPremisesSyncEnabled" };
                    requestConfiguration.Options.Add(retryHandlerOption);
                    requestConfiguration.Options.Add(responseHandlerOption);
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                if (nativeResponse.IsSuccessStatusCode)
                {
                    var group = await DeserializeResponseAsync(nativeResponse, Group.CreateFromDiscriminatorValue);

                    // null or false means cloud-native, only true means on-prem synced
                    return group?.OnPremisesSyncEnabled == true;
                }
                else if (nativeResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                throw new Exception($"Unable to determine if group {groupId} is on-premises synced. Status code: {nativeResponse.StatusCode}");
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return false;

                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
            catch (Exception ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
        }

        public async Task<bool> GroupExistsAsync(string groupName, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Groups
                                   .GetAsync(requestConfiguration =>
                                   {
                                       requestConfiguration
                                        .QueryParameters
                                        .Filter = $"startswith(displayName, '{groupName}')";
                                       requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                                   });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse == null)
                {
                    return false;
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                if (!nativeResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to verify group existence. Status code: {nativeResponse.StatusCode}");
                }

                var groupCollectionPage = await DeserializeResponseAsync(nativeResponse, GroupCollectionResponse.CreateFromDiscriminatorValue);

                return groupCollectionPage?.Value?.Any() ?? false;
            }
            catch (ODataError ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

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
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

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

                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

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

        public async Task<string> GetGroupEmailAsync(Guid groupId, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };
                Group group = null;

                await _graphServiceClient.Groups[groupId.ToString()].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Options.Add(responseHandlerOption);
                    requestConfiguration.QueryParameters.Select = new[] { "mail" };
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                if (nativeResponse.IsSuccessStatusCode)
                {
                    group = await DeserializeResponseAsync(nativeResponse, Group.CreateFromDiscriminatorValue);
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                return group != null ? group.Mail : string.Empty;
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return string.Empty;

                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
        }

        public async Task<Dictionary<Guid, string>> GetGroupEmailsAsync(List<Guid> groupIds)
        {
            var groupEmails = new Dictionary<Guid, string>();
            var batchRequest = new BatchRequestContentCollection(_graphServiceClient);

            // requestId, groupId
            var requestIdTracker = new Dictionary<string, Guid>();

            foreach (var groupId in groupIds.Distinct())
            {
                var requestInformation = _graphServiceClient
                                            .Groups[groupId.ToString()]
                                            .ToGetRequestInformation(requestConfiguration =>
                                            {
                                                requestConfiguration.QueryParameters.Select = new[] { "mail" };
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
                        groupEmails.Add(requestIdTracker[statusCodeResponse.Key], group.Mail);
                }
                else
                {
                    groupEmails.Add(requestIdTracker[statusCodeResponse.Key], null);
                }
            }

            return groupEmails;
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

                var headers = response.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId: null);

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

                var outlookResponse = await batchResponse.GetResponseByIdAsync(outlookRequestId);
                Group group = null;

                if (outlookResponse != null)
                {
                    var outlookHeaders = outlookResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(outlookHeaders, QueryType.Other, runId);

                    if (outlookResponse.IsSuccessStatusCode)
                    {
                        group = await DeserializeResponseAsync(outlookResponse, Group.CreateFromDiscriminatorValue);
                    }
                }

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

                if (siteResponse != null)
                {
                    var siteHeaders = siteResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(siteHeaders, QueryType.Other, runId);

                    if (siteResponse.IsSuccessStatusCode || siteResponse.StatusCode == HttpStatusCode.Forbidden)
                    {
                        endpoints.Add("SharePoint");
                    }
                }
            }
            catch (ApiException ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);
            }

            try
            {
                var baseUrl = "https://graph.microsoft.com/beta";
                var endpointsUrl = $"{baseUrl}/groups/{groupId}/endpoints";
                var getRequestInformation = _graphServiceClient.Groups.ToGetRequestInformation();
                getRequestInformation.URI = new Uri(endpointsUrl);
                getRequestInformation.PathParameters["baseurl"] = baseUrl;

                var nativeResponseHandler = new NativeResponseHandler();

                getRequestInformation.AddRequestOptions(new IRequestOption[] { new ResponseHandlerOption { ResponseHandler = nativeResponseHandler } });

                await _graphServiceClient
                        .RequestAdapter
                        .SendAsync<EndpointCollectionResponse>(getRequestInformation,
                        EndpointCollectionResponse.CreateFromDiscriminatorValue);

                var endpointResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (endpointResponse != null)
                {
                    var endpointHeaders = endpointResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(endpointHeaders, QueryType.Other, runId);

                    if (endpointResponse.IsSuccessStatusCode)
                    {
                        var endpointCollectionResponse = await DeserializeResponseAsync(endpointResponse, EndpointCollectionResponse.CreateFromDiscriminatorValue);

                        if (endpointCollectionResponse?.Value?.Any() ?? false)
                            endpoints.AddRange(endpointCollectionResponse.Value.Select(x => x.ProviderName));
                    }
                }

            }
            catch (ODataError ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);
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

                var group = await _graphServiceClient.Groups.PostAsync(groupDefinition);

                if (group != null && groupOwnerIds != null && groupOwnerIds.Any())
                {
                    foreach (var ownerId in groupOwnerIds)
                    {
                        await _graphServiceClient.Groups[group.Id]
                            .Owners
                            .Ref
                            .PostAsync(new ReferenceCreate
                            {
                                OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{ownerId}"
                            });
                    }
                }
            }
            catch (ODataError ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
            catch (Exception e)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, $"Error creating group: {e}", e);
            }
        }

        public async Task<AzureADGroup> CreateGroupFromUIAsync(string newGroupName, Guid groupOwnerId, string newGroupAlias, Guid? runId)
        {
            try
            {
                var groupDefinition = new Group
                {
                    DisplayName = newGroupName,
                    MailEnabled = false,
                    SecurityEnabled = true,
                    Description = $"{newGroupName}",
                    GroupTypes = new List<string> { "Unified" },
                    MailNickname = newGroupAlias ?? Guid.NewGuid().ToString()
                };

                Group createdGroup = null;
                var createResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Groups.PostAsync(groupDefinition, requestConfiguration =>
                {
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = createResponseHandler });
                });

                var createResponse = createResponseHandler.Value as HttpResponseMessage;

                if (createResponse != null)
                {
                    var createHeaders = createResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(createHeaders, QueryType.Other, runId);

                    if (createResponse.IsSuccessStatusCode)
                    {
                        createdGroup = await DeserializeResponseAsync(createResponse, Group.CreateFromDiscriminatorValue);
                    }
                    else
                    {
                        throw new HttpRequestException($"Failed to create group. Status code: {createResponse.StatusCode}");
                    }
                }

                if (createdGroup == null)
                {
                    return null;
                }

                var ownerResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Groups[createdGroup.Id]
                                            .Owners
                                            .Ref
                                            .PostAsync(new ReferenceCreate
                                            {
                                                OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{groupOwnerId}"
                                            }, requestConfiguration =>
                                            {
                                                requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = ownerResponseHandler });
                                            });

                var ownerResponse = ownerResponseHandler.Value as HttpResponseMessage;

                if (ownerResponse != null)
                {
                    var ownerHeaders = ownerResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(ownerHeaders, QueryType.Other, runId);

                    if (!ownerResponse.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Failed to assign owner to newly created group. Status code: {ownerResponse.StatusCode}");
                    }
                }


                if (await GroupExistsAsync(new Guid(createdGroup.Id), runId))
                {
                    return new AzureADGroup
                    {
                        ObjectId = new Guid(createdGroup.Id),
                        Name = createdGroup.DisplayName
                    };
                }
                else
                {
                    _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, "Error creating group");
                    return null;
                }
            }
            catch (ODataError ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
            catch (Exception e)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, $"Error creating group: {e}", e);
                return null;
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
                            requestConfiguration.QueryParameters.Select = new[] { "id", "mailEnabled", "groupTypes", "securityEnabled", "displayName", "mail", "visibility" };
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
                            group.Email = graphGroup.Mail;
                            group.Visibility = graphGroup.Visibility?.ToString();
                        }

                        var headers = graphGroupResonse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                        await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                        groups.Add(group);
                    }

                }
            }
            catch (ODataError ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
            catch (Exception ex)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(runId, $"Unable to retrieve group types\n{ex.GetBaseException()}", ex);
            }

            return groups;
        }

        public async Task<List<AzureADGroup>> SearchGroupsAsync(string filter)
        {
            try
            {
                var results = new List<AzureADGroup>();
                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Groups
                                   .GetAsync(requestConfiguration =>
                                   {
                                       requestConfiguration
                                        .QueryParameters
                                        .Filter = filter;
                                       requestConfiguration
                                        .QueryParameters
                                        .Top = MaxGroupResultCount;
                                       requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                                   });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse == null)
                {
                    return results;
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId: null);

                if (!nativeResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to search for groups. Status code: {nativeResponse.StatusCode}");
                }

                var groupCollectionPage = await DeserializeResponseAsync(nativeResponse, GroupCollectionResponse.CreateFromDiscriminatorValue);

                if (groupCollectionPage?.Value?.Count > 0)
                {
                    foreach (var group in groupCollectionPage.Value)
                    {
                        var azureAdGroup = new AzureADGroup
                        {
                            ObjectId = new Guid(group.Id),
                            Name = group.DisplayName,
                            Email = group.Mail
                        };

                        results.Add(azureAdGroup);
                    }
                }

                return results;

            }
            catch (Exception e)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(null, $"Error searching for groups: {e}", e);
                throw;
            }
        }

        public async Task<List<AzureADGroup>> GetGroupsByFilterAsync(string filter)
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
                                        .Top = 999;
                                   });

                while (groupCollectionPage.Value.Count > 0)
                {
                    foreach (var group in groupCollectionPage.Value)
                    {
                        var azureAdGroup = new AzureADGroup
                        {
                            ObjectId = new Guid(group.Id),
                            Name = group.DisplayName,
                            Email = group.Mail
                        };

                        results.Add(azureAdGroup);
                    }

                    if (groupCollectionPage.OdataNextLink != null)
                    {
                        groupCollectionPage = await _graphServiceClient.Groups.WithUrl(groupCollectionPage.OdataNextLink).GetAsync();
                    }
                    else
                    {
                        break;
                    }
                }

                return results;

            }
            catch (Exception e)
            {
                _graphGroupInformationRepositoryLogger.LogErrorWithRunId(null, $"Error searching for groups: {e}", e);
                throw;
            }
        }

        public async Task<List<string>> GetAllGroupNamesAsync()
        {
            var groupNames = new List<string>();
            var page = await _graphServiceClient.Groups
                .GetAsync(config => config.QueryParameters.Select = new[] { "displayName" });

            while (page != null && page.Value != null)
            {
                groupNames.AddRange(page.Value.Select(g => g.DisplayName));

                if (page.OdataNextLink != null)
                {
                    page = await _graphServiceClient.Groups.WithUrl(page.OdataNextLink).GetAsync();
                }
                else
                {
                    page = null;
                }
            }

            _graphGroupInformationRepositoryLogger.LogInformationWithRunId(null, $"Fetched {groupNames.Count} group names.");

            return groupNames;
        }
    }
}
