// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.OData.Query;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using GroupDTO = WebApi.Models.DTOs.Group;

namespace Services
{
    public class GetGroupHandler : RequestHandlerBase<GetGroupRequest, GetGroupResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        public GetGroupHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetGroupResponse> ExecuteCoreAsync(GetGroupRequest request)
        {
            var response = new GetGroupResponse();
            var groups = await _graphGroupRepository.SearchGroupsAsync(request.Query);

            foreach ( var group in groups )
            {
                var dto = new GroupDTO(group.ObjectId, group.Name);
                response.Model.Add(dto);
            }

            return response;
        }
    }
}