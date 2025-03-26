// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.OData.Query;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using DestinationDTO = WebApi.Models.DTOs.Destination;

namespace Services
{
    public class SearchGroupsHandler : RequestHandlerBase<SearchGroupsRequest, SearchGroupsResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        public SearchGroupsHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
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

            string filter;

            if (Guid.TryParse(request.Query, out _))
            {
                filter = $"id eq '{request.Query}'";
            }
            else
            {
                filter = $"startswith(displayName,'{request.Query}') or startswith(mail,'{request.Query}') or startswith(mailNickname,'{request.Query}')";
            }

            var groups = await _graphGroupRepository.SearchDestinationsAsync(filter);

            foreach (var group in groups)
            {
                var dto = new DestinationDTO(group.ObjectId, group.Name, group.Email);

                response.Model.Add(dto);
            }

            return response;
        }
    }
}