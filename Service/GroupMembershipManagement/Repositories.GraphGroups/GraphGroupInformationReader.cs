// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Models;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphGroupInformationReader
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphGroupMetricTracker _graphGroupMetricTracker;

        public GraphGroupInformationReader(GraphServiceClient graphServiceClient,
                                           ILoggingRepository loggingRepository,
                                           GraphGroupMetricTracker graphGroupMetricTracker)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupMetricTracker = graphGroupMetricTracker ?? throw new ArgumentNullException(nameof(graphGroupMetricTracker));
        }

        public async Task<bool> GroupExistsAsync(Guid groupId, Guid? runId)
        {
            try
            {
                var group = await _graphServiceClient.Groups[groupId.ToString()].GetAsync();
                await _graphGroupMetricTracker.TrackMetricsAsync(group.AdditionalData, QueryType.Other, runId);

                return group != null;
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
                var group = await _graphServiceClient.Groups[groupId.ToString()].GetAsync();
                await _graphGroupMetricTracker.TrackMetricsAsync(group.AdditionalData, QueryType.Other, runId);

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

        public async Task<List<string>> GetGroupEndpointsAsync(Guid groupId, Guid? runId)
        {
            var endpoints = new List<string>();
            var baseUrl = "https://graph.microsoft.com";

            try
            {
                var batchRequest = new BatchRequestContent(_graphServiceClient);
                var outlookRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1.0/groups/{groupId}?$select=mailEnabled,groupTypes,securityEnabled");
                var outlookStep = new BatchRequestStep("outlook", outlookRequest);
                var sharepointRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1.0/groups/{groupId}/sites/root");
                var sharepointStep = new BatchRequestStep("sharepoint", sharepointRequest);

                batchRequest.AddBatchRequestStep(outlookStep);
                batchRequest.AddBatchRequestStep(sharepointStep);

                var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);
                var individualResponses = await batchResponse.GetResponsesAsync();

                if (individualResponses.ContainsKey("outlook") && individualResponses["outlook"].IsSuccessStatusCode)
                {
                    var content = await individualResponses["outlook"].Content.ReadAsStringAsync();
                    var jObject = JObject.Parse(content);
                    var isMailEnabled = jObject.Value<bool>("mailEnabled");
                    var isSecurityEnabled = jObject.Value<bool>("securityEnabled");
                    var groupTypes = jObject.Value<JArray>("groupTypes").Values<string>().ToList();

                    if (isMailEnabled && groupTypes.Contains("Unified"))
                        endpoints.Add("Outlook");
                    else if (isSecurityEnabled && !groupTypes.Any())
                        endpoints.Add("SecurityGroup");
                }

                if (individualResponses.ContainsKey("sharepoint") && individualResponses["sharepoint"].IsSuccessStatusCode)
                {
                    endpoints.Add("SharePoint");
                }
            }
            catch (ODataError ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });
            }

            try
            {
                var endpointsUrl = $"{baseUrl}/beta/groups/{groupId}/endpoints";
                var getRequestInformation = _graphServiceClient.Groups.ToGetRequestInformation();
                getRequestInformation.URI = new Uri(endpointsUrl);
                getRequestInformation.PathParameters["baseurl"] = "https://graph.microsoft.com/beta";

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
    }
}
