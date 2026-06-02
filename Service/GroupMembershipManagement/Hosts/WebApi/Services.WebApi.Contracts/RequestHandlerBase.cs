// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Services.Messages.Contracts.Requests;
using Services.Messages.Contracts.Responses;

namespace Services.Contracts
{
    public abstract class RequestHandlerBase<TRequestBase, TResponseBase> : IRequestHandler<TRequestBase, TResponseBase>
                where TRequestBase : RequestBase
                where TResponseBase : ResponseBase, new()
    {
        private readonly ILogger _logger;

        protected ILogger Logger => _logger;

        public RequestHandlerBase(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TResponseBase> ExecuteAsync(TRequestBase request)
        {
            _logger.RequestStarted(request.GetType().Name, request.InstanceId);

            var response = await ExecuteCoreAsync(request);

            _logger.RequestCompleted(request.GetType().Name, request.InstanceId);

            return response;
        }

        protected abstract Task<TResponseBase> ExecuteCoreAsync(TRequestBase request);
    }
}
