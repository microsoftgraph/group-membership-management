// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Net;
using WebApi.Models;
using Microsoft.Extensions.Logging;

namespace Services.WebApi
{
    public class PostOperationHandler : RequestHandlerBase<PostOperationRequest, PostOperationResponse>
    {
        private readonly ILogger<PostOperationHandler> _logger;
        private readonly IServiceStatusRepository _serviceStatusRepository;
        private readonly IOperationsTaskQueue _backgroundTaskService;

        public PostOperationHandler(ILogger<PostOperationHandler> logger,
                                IServiceStatusRepository serviceStatusRepository,
                                IOperationsTaskQueue backgroundTaskService) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceStatusRepository = serviceStatusRepository ?? throw new ArgumentNullException(nameof(serviceStatusRepository));
            _backgroundTaskService = backgroundTaskService ?? throw new ArgumentNullException(nameof(backgroundTaskService));
        }

        protected override async Task<PostOperationResponse> ExecuteCoreAsync(PostOperationRequest request)
        {
            try
            {
                _logger.OperationProcessing(request.Operation);

                var currentStatus = await _serviceStatusRepository.GetCurrentServiceStatusAsync();

                if (request.Operation == Operations.Stop && currentStatus != ServiceStatuses.Running)
                {
                    _logger.OperationServiceAlreadyInStatus(request.Operation, currentStatus);

                    return new PostOperationResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        Status = currentStatus
                    };
                }

                if (request.Operation == Operations.Reset && currentStatus == ServiceStatuses.Resetting)
                {
                    _logger.OperationServiceAlreadyInStatus(request.Operation, currentStatus);

                    return new PostOperationResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        Status = currentStatus
                    };
                }

                if (request.Operation == Operations.Start && currentStatus == ServiceStatuses.Running)
                {
                    _logger.OperationServiceAlreadyInStatus(request.Operation, currentStatus);

                    return new PostOperationResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        Status = currentStatus
                    };
                }

                var tempStatus = request.Operation switch
                {
                    Operations.Start => ServiceStatuses.Starting,
                    Operations.Stop => ServiceStatuses.Stopping,
                    Operations.Reset => ServiceStatuses.Resetting,
                    Operations.Reschedule => ServiceStatuses.Rescheduling,
                    _ => throw new InvalidOperationException($"Invalid operation {request.Operation}")
                };

                var operationDetails = new OperationDetails
                {
                    Operation = request.Operation,
                    RequestorId = request.RequestorId
                };

                await _backgroundTaskService.QueueAsync(operationDetails);
                await _serviceStatusRepository.SetServiceStatusAsync(tempStatus, request.RequestorId);
                currentStatus = await _serviceStatusRepository.GetCurrentServiceStatusAsync();

                return new PostOperationResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    Status = currentStatus
                };
            }
            catch (Exception ex)
            {
                _logger.PostOperationHandlerFailed(request.Operation, ex);

                return new PostOperationResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ErrorCode = "Error",
                    ResponseData = new List<string> { $"Unable to process {nameof(PostOperationRequest)} to {request.Operation}" }
                };
            }
        }
    }
}
