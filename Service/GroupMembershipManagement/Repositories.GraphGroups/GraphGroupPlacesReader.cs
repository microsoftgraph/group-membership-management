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
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Repositories.GraphGroups
{
    internal class GraphGroupPlacesReader : GraphGroupRepositoryBase
    {
        private readonly GraphUserReader _graphUserReader;
        private readonly ILogger<GraphGroupPlacesReader> _graphGroupPlacesReaderLogger;

        public GraphGroupPlacesReader(GraphServiceClient graphServiceClient,
                                      GraphGroupMetricTracker graphGroupMetricTracker,
                                      ILogger<GraphGroupPlacesReader> graphGroupPlacesReaderLogger,
                                      ILogger<GraphUserReader> graphUserReaderLogger)
                                      : base(graphServiceClient, graphGroupPlacesReaderLogger, graphGroupMetricTracker)
        {
            _graphGroupPlacesReaderLogger = graphGroupPlacesReaderLogger ?? throw new ArgumentNullException(nameof(graphGroupPlacesReaderLogger));
            _graphUserReader = new GraphUserReader(graphServiceClient, graphGroupMetricTracker, graphUserReaderLogger);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var roomsResponse = await GetRoomsAsync(url, top, skip, runId);
            if (roomsResponse.Value.Count > 0)
            {
                foreach (var room in roomsResponse.Value)
                {
                    var user = await _graphUserReader.GetUserByUpnOrIdAsync(room.EmailAddress, runId);
                    if (user != null) users.Add(user);
                }
            }

            if (roomsResponse.OdataCount > 0)
            {
                var numberOfRequests = roomsResponse.OdataCount / top;
                for (int i = 0; i <= numberOfRequests; i++)
                {
                    skip += top;
                    roomsResponse = await GetRoomsAsync(url, top, skip, runId);
                    if (roomsResponse.Value.Count > 0)
                    {
                        foreach (var room in roomsResponse.Value)
                        {
                            var user = await _graphUserReader.GetUserByUpnOrIdAsync(room.EmailAddress, runId);
                            if (user != null) users.Add(user);
                        }
                    }
                }
            }

            return (users, roomsResponse.OdataNextLink);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl)> GetWorkSpacesPageAsync(string url, int top, int skip, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var response = await GetWorkSpacesAsync(url, top, skip, runId);

            if (response.Value.Count > 0)
            {
                foreach (var room in response.Value)
                {
                    var user = await _graphUserReader.GetUserByUpnOrIdAsync(room.EmailAddress, runId);
                    if (user != null) users.Add(user);
                }
            }

            var total = response.AdditionalData.TryGetValue("@odata.count", out object count) ? (int)(long)count : 0;

            if (total > 0)
            {
                var numberOfRequests = total / top;
                for (int i = 0; i <= numberOfRequests; i++)
                {
                    skip += top;
                    response = await GetWorkSpacesAsync(url, top, skip, runId);
                    if (response.Value.Count > 0)
                    {
                        foreach (var room in response.Value)
                        {
                            var user = await _graphUserReader.GetUserByUpnOrIdAsync(room.EmailAddress, runId);
                            if (user != null) users.Add(user);
                        }
                    }
                }
            }

            return (users, response.OdataNextLink);
        }

        private async Task<RoomCollectionResponse> GetRoomsAsync(string url, int top, int skip, Guid? runId)
        {
            var queryParamValues = HttpUtility.ParseQueryString(url, Encoding.UTF8);
            var filterValue = queryParamValues["$filter"];
            var selectValue = queryParamValues["$select"];
            var selectValues = selectValue?.Split(',');

            if (selectValues != null && !selectValues.Contains("emailAddress", StringComparer.InvariantCultureIgnoreCase))
            {
                selectValues = selectValues.Append("emailAddress").ToArray();
            }

            var nativeResponseHandler = new NativeResponseHandler();
            var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

            await _graphServiceClient
                    .Places
                    .GraphRoom
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Top = top;
                        requestConfiguration.QueryParameters.Skip = skip;
                        requestConfiguration.QueryParameters.Filter = filterValue;
                        requestConfiguration.QueryParameters.Select = selectValues;
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.Options.Add(responseHandlerOption);
                    });

            var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
            return await DeserializeRoomResponseAsync(nativeResponse, runId);
        }

        private async Task<RoomCollectionResponse> GetWorkSpacesAsync(string url, int top, int skip, Guid? runId)
        {
            var requestInformation = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = url
            };

            if (top > 0)
                requestInformation.QueryParameters.Add("$top", top);
            if (skip > 0)
                requestInformation.QueryParameters.Add("$skip", skip);

            var nativeResponseHandler = new NativeResponseHandler();
            var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };

            requestInformation.AddRequestOptions(new List<IRequestOption> { responseHandlerOption });

            await _graphServiceClient
                    .RequestAdapter
                    .SendAsync(requestInformation,
                               RoomCollectionResponse.CreateFromDiscriminatorValue);

            var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
            return await DeserializeRoomResponseAsync(nativeResponse, runId);
        }

        private async Task<RoomCollectionResponse> DeserializeRoomResponseAsync(HttpResponseMessage nativeResponse, Guid? runId)
        {
            if (nativeResponse == null)
            {
                throw new InvalidOperationException("Unable to obtain a response from Microsoft Graph for the places query.");
            }

            var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
            await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

            if (!nativeResponse.IsSuccessStatusCode)
            {
                var errorContent = await nativeResponse.Content.ReadAsStringAsync();
                _graphGroupPlacesReaderLogger.LogErrorWithRunId(runId, $"Failed to retrieve place information from Microsoft Graph. StatusCode {(int)nativeResponse.StatusCode} - {nativeResponse.StatusCode}. Response: {errorContent}");

                nativeResponse.EnsureSuccessStatusCode();
            }

            var response = await DeserializeResponseAsync(nativeResponse, RoomCollectionResponse.CreateFromDiscriminatorValue) ?? new RoomCollectionResponse();
            nativeResponse.Dispose();
            return response;
        }
    }
}
