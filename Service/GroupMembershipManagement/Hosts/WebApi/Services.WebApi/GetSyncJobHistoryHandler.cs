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
        private readonly IAdfRunRepository _adfRunRepository;

        public GetSyncJobHistoryHandler(ILogger<GetSyncJobHistoryHandler> logger,
                                        ISyncJobHistoryRepository syncJobHistoryRepository,
                                        IAdfRunRepository adfRunRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
            _adfRunRepository = adfRunRepository ?? throw new ArgumentNullException(nameof(adfRunRepository));
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

                await AttachCustomMessagesAsync(history);

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

        private async Task AttachCustomMessagesAsync(List<Models.SyncJobHistory.SyncJobHistory>? history)
        {
            if (history == null || history.Count == 0)
                return;

            var adfRunIds = history
                .Where(h => h.AdfRunId.HasValue)
                .Select(h => h.AdfRunId!.Value.ToString())
                .Distinct()
                .ToList();

            if (adfRunIds.Count == 0)
                return;

            var notesByAdfRunId = await _adfRunRepository.GetNotesByAdfRunIdsAsync(adfRunIds);
            if (notesByAdfRunId.Count == 0)
                return;

            foreach (var item in history)
            {
                if (item.AdfRunId.HasValue
                    && notesByAdfRunId.TryGetValue(item.AdfRunId.Value.ToString(), out var message))
                {
                    item.CustomMessage = message;
                }
            }
        }
    }
}
