// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using Services.Messages.Contracts.Requests;
using Services.Messages.Contracts.Responses;

namespace Services.Contracts
{
    public abstract class RequestHandlerBase<TRequestBase, TResponseBase> : IRequestHandler<TRequestBase, TResponseBase>
                where TRequestBase : RequestBase
                where TResponseBase : ResponseBase, new()
    {
        private readonly ILoggingRepository _loggingRepository;

        public RequestHandlerBase(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<TResponseBase> ExecuteAsync(TRequestBase request)
        {
            await _loggingRepository.LogMessageAsync(
            new LogMessage
            {
                InstanceId = request.InstanceId,
                MessageTypeName = request.GetType().Name,
                Message = "Started execution of request"
            });

            var response = await ExecuteCoreAsync(request);

            await _loggingRepository.LogMessageAsync(
            new LogMessage
            {
                InstanceId = request.InstanceId,
                MessageTypeName = request.GetType().Name,
                Message = "Completed execution of request"
            });

            return response;
        }

        protected abstract Task<TResponseBase> ExecuteCoreAsync(TRequestBase request);
    }
}
