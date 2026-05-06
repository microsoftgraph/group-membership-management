// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetSyncJobHistoryHandler : RequestHandlerBase<GetSyncJobHistoryRequest, GetSyncJobHistoryResponse>
    {
        private readonly ILogger<GetSyncJobHistoryHandler> _logger;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;

        public GetSyncJobHistoryHandler(ILogger<GetSyncJobHistoryHandler> logger,
                                        ISyncJobHistoryRepository syncJobHistoryRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
        }

        protected override async Task<GetSyncJobHistoryResponse> ExecuteCoreAsync(GetSyncJobHistoryRequest request)
        {
            var response = new GetSyncJobHistoryResponse();

            try
            {
                var history = await _syncJobHistoryRepository.GetBySyncJobIdAsync(
                    request.SyncJobId,
                    request.PageSize,
                    request.PageNumber);

                response.History = history;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger.SyncJobHistoryRetrievalFailed(ex);

                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
    }
}
