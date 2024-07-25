// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Net;
using WebApi.Models;

namespace Services.WebApi
{
    public class PostOperationHandler : RequestHandlerBase<PostOperationRequest, PostOperationResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IServiceStatusRepository _serviceStatusRepository;
        private readonly IOperationsTaskQueue _backgroundTaskService;

        public PostOperationHandler(ILoggingRepository loggingRepository,
                                IServiceStatusRepository serviceStatusRepository,
                                IOperationsTaskQueue backgroundTaskService) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceStatusRepository = serviceStatusRepository ?? throw new ArgumentNullException(nameof(serviceStatusRepository));
            _backgroundTaskService = backgroundTaskService ?? throw new ArgumentNullException(nameof(backgroundTaskService));
        }

        protected override async Task<PostOperationResponse> ExecuteCoreAsync(PostOperationRequest request)
        {
            try
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Processing operation {request.Operation}."
                });

                var currentStatus = await _serviceStatusRepository.GetCurrentServiceStatusAsync();

                if (request.Operation == Operations.Stop && currentStatus != ServiceStatuses.Running)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Operation {request.Operation}. Service is already {currentStatus}"
                    });

                    return new PostOperationResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        ResponseData = new List<string> { currentStatus.ToString() }
                    };
                }

                if (request.Operation == Operations.Reset && currentStatus == ServiceStatuses.Resetting)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Operation {request.Operation}. Service is already {currentStatus}"
                    });

                    return new PostOperationResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        ResponseData = new List<string> { currentStatus.ToString() }
                    };
                }

                if (request.Operation == Operations.Start && currentStatus == ServiceStatuses.Running)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Operation {request.Operation}. Service is already {currentStatus}"
                    });

                    return new PostOperationResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        ResponseData = new List<string> { currentStatus.ToString() }
                    };
                }

                var tempStatus = request.Operation switch
                {
                    Operations.Start => ServiceStatuses.Starting,
                    Operations.Stop => ServiceStatuses.Stopping,
                    Operations.Reset => ServiceStatuses.Resetting,
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
                    ResponseData = new List<string> { currentStatus.ToString() }
                };
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error in {nameof(PostOperationHandler)} with operation {request.Operation}\n{ex.Message}"
                });

                return new PostOperationResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ResponseData = new List<string> { $"Unable to process {nameof(PostOperationRequest)} to {request.Operation}" }
                };
            }
        }
    }
}
