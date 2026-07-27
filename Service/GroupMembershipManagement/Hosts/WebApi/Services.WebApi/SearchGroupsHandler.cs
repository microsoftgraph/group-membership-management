// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.OData.Query;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;
using DestinationDTO = WebApi.Models.DTOs.Destination;

namespace Services
{
    public class SearchGroupsHandler : RequestHandlerBase<SearchGroupsRequest, SearchGroupsResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        public SearchGroupsHandler(ILogger<SearchGroupsHandler> logger,
                              IGraphGroupRepository graphGroupRepository) : base(logger)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<SearchGroupsResponse> ExecuteCoreAsync(SearchGroupsRequest request)
        {
            var response = new SearchGroupsResponse();

            int minQueryLength = 1;
            if (string.IsNullOrEmpty(request.Query) || request.Query.Length < minQueryLength)
            {
                return response;
            }

            List<AzureADGroup> groups;

            try
            {
                if (Guid.TryParse(request.Query, out var groupId))
                {
                    // Microsoft Graph does not support "$filter=id eq '...'" on /groups (it returns 400),
                    // so resolve an exact group id with a direct lookup instead. Ids that don't resolve to
                    // a group come back without a name, so filter those out to keep search results clean.
                    var groupsById = await _graphGroupRepository.GetGroupsAsync(new List<Guid> { groupId });
                    groups = groupsById.Where(g => !string.IsNullOrEmpty(g.Name)).ToList();
                }
                else
                {
                    // Escape single quotes for OData string literals (a single quote is escaped by doubling it).
                    var safeQuery = request.Query.Replace("'", "''");
                    var filter = $"startswith(displayName,'{safeQuery}') or startswith(mail,'{safeQuery}') or startswith(mailNickname,'{safeQuery}')";
                    groups = await _graphGroupRepository.SearchDestinationsAsync(filter);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                // A type-ahead search box should degrade to "no matches" only when Graph rejects the query
                // itself (HTTP 400 - e.g. an OData quirk on startswith across multiple properties). Auth
                // (401/403), throttling (429), and server/transient errors (5xx) are deliberately NOT caught
                // here so they surface via the controller's 500 handler and stay visible to telemetry/alerting
                // instead of being silently masked as an empty result.
                Logger.LogWarning(ex, "Graph returned a bad request (400) while searching groups; returning no matches.");
                return response;
            }

            foreach (var group in groups)
            {
                var dto = new DestinationDTO(group.ObjectId, group.Name, group.Email);

                response.Model.Add(dto);
            }

            return response;
        }
    }
}