// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.WebApi;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;
using GroupOwnerDTO = WebApi.Models.DTOs.GroupOwner;

namespace Services
{
    public class GetGroupOwnersHandler : RequestHandlerBase<GetGroupOwnersRequest, GetGroupOwnersResponse>
    {
        private readonly ILogger<GetGroupOwnersHandler> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public GetGroupOwnersHandler(
            ILogger<GetGroupOwnersHandler> logger,
            IGraphGroupRepository graphGroupRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetGroupOwnersResponse> ExecuteCoreAsync(GetGroupOwnersRequest request)
        {
            var response = new GetGroupOwnersResponse();

            try
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(request.GroupId);
                response.Owners = owners.Select(owner => new GroupOwnerDTO(
                    owner.ObjectId,
                    owner.DisplayName ?? "",
                    owner.Mail ?? "")).ToList();
            }
            catch (Exception ex)
            {
                _logger.GroupOwnersRetrievalFailed(request.GroupId, ex);
                throw;
            }

            return response;
        }
    }
}
