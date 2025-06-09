// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph.Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using ChannelDTO = WebApi.Models.DTOs.Channel;

namespace Services
{
    public class SearchChannelsHandler : RequestHandlerBase<SearchChannelsRequest, SearchChannelsResponse>
    {
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        public SearchChannelsHandler(ILoggingRepository loggingRepository,
                              ITeamsChannelRepository teamsChannelRepository) : base(loggingRepository)
        {
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
        }

        protected override async Task<SearchChannelsResponse> ExecuteCoreAsync(SearchChannelsRequest request)
        {
            var response = new SearchChannelsResponse();

            int minQueryLength = 1;
            if (string.IsNullOrEmpty(request.Query) || request.Query.Length < minQueryLength)
            {
                return response;
            }

            string filter = $"startswith(tolower(displayName),tolower('{request.Query}'))";

            var channels = await _teamsChannelRepository.SearchTeamsChannelsAsync(request.TeamId, filter);

            foreach (var channel in channels)
            {
                if (channel.MembershipType != ChannelMembershipType.Private && channel.MembershipType != ChannelMembershipType.Standard)
                {
                    var dto = new ChannelDTO(request.TeamId, channel.Id!, channel.DisplayName!);
                    response.Model.Add(dto);
                }
            }

            return response;
        }
    }
}