// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Repositories.GraphGroups
{
    internal class GraphGroupPlacesReader : GraphGroupRepositoryBase
    {
        private readonly GraphUserReader _graphUserReader;

        public GraphGroupPlacesReader(GraphServiceClient graphServiceClient,
                            ILoggingRepository loggingRepository,
                            GraphGroupMetricTracker graphGroupMetricTracker)
                            : base(graphServiceClient, loggingRepository, graphGroupMetricTracker)
        {
            _graphUserReader = new GraphUserReader(graphServiceClient, loggingRepository, graphGroupMetricTracker);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl)> GetRoomsPageAsync(string url, int top, int skip, Guid? runId)
        {
            var users = new List<AzureADUser>();
            var roomsResponse = await GetRoomsAsync(url, top, skip);
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
                    roomsResponse = await GetRoomsAsync(url, top, skip);
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
            var response = await GetWorkSpacesAsync(url, top, skip);

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
                    response = await GetWorkSpacesAsync(url, top, skip);
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

        private async Task<RoomCollectionResponse> GetRoomsAsync(string url, int top, int skip)
        {
            var queryParamValues = HttpUtility.ParseQueryString(url, Encoding.UTF8);
            var filterValue = queryParamValues["$filter"];
            var selectValue = queryParamValues["$select"];
            var selectValues = selectValue?.Split(',');

            if (selectValues != null && !selectValues.Contains("emailAddress", StringComparer.InvariantCultureIgnoreCase))
            {
                selectValues = selectValues.Append("emailAddress").ToArray();
            }

            var roomsResponse = await _graphServiceClient
                                .Places
                                .GraphRoom
                                .GetAsync(requestConfiguration =>
                                {
                                    requestConfiguration.QueryParameters.Top = top;
                                    requestConfiguration.QueryParameters.Skip = skip;
                                    requestConfiguration.QueryParameters.Filter = filterValue;
                                    requestConfiguration.QueryParameters.Select = selectValues;
                                    requestConfiguration.QueryParameters.Count = true;
                                });

            return roomsResponse;
        }

        private async Task<RoomCollectionResponse> GetWorkSpacesAsync(string url, int top, int skip)
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

            var workspaceResponse = await _graphServiceClient
                                            .RequestAdapter
                                            .SendAsync(requestInformation,
                                                       RoomCollectionResponse.CreateFromDiscriminatorValue);

            return workspaceResponse;
        }
    }
}
