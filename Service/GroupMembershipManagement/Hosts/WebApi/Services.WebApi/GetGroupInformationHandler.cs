// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.OData.Query;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using GroupDTO = WebApi.Models.DTOs.AzureADGroup;

namespace Services
{
    public class GetGroupInformationHandler : RequestHandlerBase<GetGroupInformationRequest, GetGroupInformationResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        public GetGroupInformationHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetGroupInformationResponse> ExecuteCoreAsync(GetGroupInformationRequest request)
        {
            var response = new GetGroupInformationResponse();
            var groups = await _graphGroupRepository.SearchGroupsAsync(request.Query);

            foreach ( var group in groups )
            {
                var endpoints = await _graphGroupRepository.GetGroupEndpointsAsync(group.ObjectId);
                var dto = new GroupDTO(group.ObjectId, group.Name, endpoints);

                response.Model.Add(dto);
            }

            return response;
        }
    }
}