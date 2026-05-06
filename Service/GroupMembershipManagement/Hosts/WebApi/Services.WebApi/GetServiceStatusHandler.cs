// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;

namespace Services.WebApi
{
    public class GetServiceStatusHandler : RequestHandlerBase<GetServiceStatusRequest, GetServiceStatusResponse>
    {
        private readonly ILogger<GetServiceStatusHandler> _logger;
        private readonly IServiceStatusRepository _serviceStatusRepository;

        public GetServiceStatusHandler(ILogger<GetServiceStatusHandler> logger,
                                       IServiceStatusRepository serviceStatusRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceStatusRepository = serviceStatusRepository ?? throw new ArgumentNullException(nameof(serviceStatusRepository));
        }

        protected override async Task<GetServiceStatusResponse> ExecuteCoreAsync(GetServiceStatusRequest request)
        {
            _logger.ServiceStatusRetrieving();

            var currentStatus = await _serviceStatusRepository.GetCurrentServiceStatusAsync();

            _logger.ServiceStatusCurrent(currentStatus.ToString());

            return new GetServiceStatusResponse
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Status = currentStatus
            };
        }
    }
}
