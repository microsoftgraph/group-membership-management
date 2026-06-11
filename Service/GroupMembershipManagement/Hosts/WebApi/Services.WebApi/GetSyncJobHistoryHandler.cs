// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;

namespace Services
{
    public class GetSyncJobHistoryHandler : RequestHandlerBase<GetSyncJobHistoryRequest, GetSyncJobHistoryResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;

        public GetSyncJobHistoryHandler(ILoggingRepository loggingRepository,
                                        ISyncJobHistoryRepository syncJobHistoryRepository) : base(loggingRepository)
        {
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error getting sync job history: {ex.Message}",
                });

                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
    }
}
