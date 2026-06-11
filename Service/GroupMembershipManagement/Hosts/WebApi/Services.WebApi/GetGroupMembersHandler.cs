// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;

namespace Services
{
    public class GetGroupMembersHandler : RequestHandlerBase<GetGroupMembersRequest, GetGroupMembersResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;

        public GetGroupMembersHandler(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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

                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    Message = $"Retrieved {groups.Count} group-type members for group {request.GroupId}. Group IDs: {string.Join(", ", groups.Select(g => g.ObjectId))}"
                });
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    Message = $"Unable to retrieve group-type members for group {request.GroupId}\n{ex.Message}"
                });
                throw;
            }

            return response;
        }
    }
}