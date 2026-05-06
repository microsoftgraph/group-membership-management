// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.WebApi;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetGroupEndpointsHandler : RequestHandlerBase<GetGroupEndpointsRequest, GetGroupEndpointsResponse>
    {
        private readonly ILogger<GetGroupEndpointsHandler> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public GetGroupEndpointsHandler(
            ILogger<GetGroupEndpointsHandler> logger,
            IGraphGroupRepository graphGroupRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetGroupEndpointsResponse> ExecuteCoreAsync(GetGroupEndpointsRequest request)
        {
            var response = new GetGroupEndpointsResponse();

            try
            {
                var endpoints = new List<string>();
                endpoints = await _graphGroupRepository.GetGroupEndpointsAsync(request.GroupId);
                response.Endpoints = endpoints;
            }
            catch (Exception ex)
            {
                _logger.GroupEndpointsRetrievalFailed(ex.GetBaseException());
            }

            return response;
        }

    }
}