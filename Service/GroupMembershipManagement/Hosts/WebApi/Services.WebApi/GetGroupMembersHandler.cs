// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetGroupMembersHandler : RequestHandlerBase<GetGroupMembersRequest, GetGroupMembersResponse>
    {
        private readonly ILogger<GetGroupMembersHandler> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public GetGroupMembersHandler(ILogger<GetGroupMembersHandler> logger, IGraphGroupRepository graphGroupRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetGroupMembersResponse> ExecuteCoreAsync(GetGroupMembersRequest request)
        {
            if (request.GroupId == Guid.Empty)
                throw new ArgumentException("GroupId cannot be empty.");

            var response = new GetGroupMembersResponse { GroupId = request.GroupId };

            try
            {
                var groups = await _graphGroupRepository.GetDirectGroupTypeMembersAsync(request.GroupId);
                response.GroupMemberCount = groups.Count;
                response.Groups = groups.Select(g => new GroupMemberGroup(g.ObjectId, g.Name ?? string.Empty)).ToList();

                _logger.GroupMembersRetrieved(groups.Count, request.GroupId, string.Join(", ", groups.Select(g => g.ObjectId)));
            }
            catch (Exception ex)
            {
                _logger.GroupMembersRetrievalFailed(request.GroupId, ex);
                throw;
            }

            return response;
        }
    }
}