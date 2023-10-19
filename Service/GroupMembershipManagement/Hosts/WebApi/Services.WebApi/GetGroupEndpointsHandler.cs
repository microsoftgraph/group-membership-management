// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetGroupEndpointsHandler : RequestHandlerBase<GetGroupEndpointsRequest, GetGroupEndpointsResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;

        public GetGroupEndpointsHandler(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Unable to retrieve group endpoints\n{ex.GetBaseException()}"
                });
            }

            return response;
        }

    }
}