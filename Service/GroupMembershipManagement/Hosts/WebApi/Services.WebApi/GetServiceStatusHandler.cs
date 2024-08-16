// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services.WebApi
{
    public class GetServiceStatusHandler : RequestHandlerBase<GetServiceStatusRequest, GetServiceStatusResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IServiceStatusRepository _serviceStatusRepository;

        public GetServiceStatusHandler(ILoggingRepository loggingRepository,
                                       IServiceStatusRepository serviceStatusRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceStatusRepository = serviceStatusRepository ?? throw new ArgumentNullException(nameof(serviceStatusRepository));
        }

        protected override async Task<GetServiceStatusResponse> ExecuteCoreAsync(GetServiceStatusRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Retrieving service status."
            });

            var currentStatus = await _serviceStatusRepository.GetCurrentServiceStatusAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Current service status is {currentStatus}."
            });

            return new GetServiceStatusResponse
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Status = currentStatus
            };
        }
    }
}
