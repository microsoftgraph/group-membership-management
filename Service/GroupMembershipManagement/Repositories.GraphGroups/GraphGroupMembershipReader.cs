// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphGroupMembershipReader
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphGroupMetricTracker _graphGroupMetricTracker;

        public GraphGroupMembershipReader(GraphServiceClient graphServiceClient,
                               ILoggingRepository loggingRepository,
                               GraphGroupMetricTracker graphGroupMetricTracker)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupMetricTracker = graphGroupMetricTracker ?? throw new ArgumentNullException(nameof(graphGroupMetricTracker));
        }

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

        private IEnumerable<AzureADUser> ToUsers(IEnumerable<DirectoryObject> fromGraph, Dictionary<string, int> nonUserGraphObjects)
        {
            foreach (var directoryObj in fromGraph)
            {
                switch (directoryObj)
                {
                    case User user:
                        yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
                        break;
                    // We only care about users
                    // I'd prefer to be able to filter these out from the results on Graph's side, but the library doesn't support that yet.
                    // we do want to log the count of non-user graph objects, though
                    default:
                        if (nonUserGraphObjects.TryGetValue(directoryObj.OdataType, out int count))
                            nonUserGraphObjects[directoryObj.OdataType] = count + 1;
                        else
                            nonUserGraphObjects[directoryObj.OdataType] = 1;
                        break;
                }
            }
        }
    }
}
